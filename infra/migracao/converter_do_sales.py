#!/usr/bin/env python3
"""Converte o backup do Sales da Avalia (avalia:exportar) no arquivo de placas
do importador (`--importar-placas`, formato em docs/OPERACAO.md).

Só lê o arquivo: não conecta em banco nenhum. O backup é SQL com INSERT em
SQL comum (texto com aspas dobradas), que carrega num SQLite em memória.

    python3 -I infra/migracao/converter_do_sales.py avalia-2026-10-22-0200.sql.gz \\
        --saida placas.csv \\
        --socio socio1@antigo --socio socio2@antigo \\
        --vendedor socio1@antigo=login1@novo --vendedor socio2@antigo=login2@novo

As regras do Sales que pesam aqui:
- comissão de 25% sobre o valor da venda, só de venda paga, nunca de sócio;
- venda paga é a que tem `pix_pago_em`, ou que é anterior a 11/10/2026, quando
  o pagamento passou a ser registrado no sistema;
- datas em UTC (o Laravel do Sales roda em UTC); `vence_em` é data.
"""

import argparse
import csv
import gzip
import sqlite3
import sys
from collections import Counter, defaultdict
from decimal import ROUND_HALF_UP, Decimal

COMISSAO_PCT = 25
CUSTO_PADRAO_CENTS = 600
PAGAMENTO_NO_SISTEMA_DESDE = "2026-10-11"
PRODUTO_DA_PLACA = "placa-avaliacao-google"
TABELAS = {"etiquetas", "staff", "negocios", "produtos"}

COLUNAS = [
    "codigo", "situacao", "destino", "vendida_em", "vence_em", "valor_cents", "custo_cents",
    "cliente_nome", "cliente_responsavel", "cliente_telefone", "cliente_documento",
    "vendedor_email", "produto", "produto_preco_cents", "comissao_devida_cents", "total_leituras",
]


def comandos(texto):
    """INSERT por INSERT. Texto com quebra de linha fica inteiro: só fecha o comando fora de aspas."""
    atual, dentro = [], False
    for linha in texto.splitlines(keepends=True):
        if not atual and (linha.startswith("--") or not linha.strip()):
            continue
        atual.append(linha)
        dentro ^= linha.count("'") % 2 == 1
        if not dentro and linha.rstrip().endswith(";"):
            yield "".join(atual)
            atual = []


def sem_crase(sql):
    """Crase do MySQL vira aspas duplas, só fora de texto: uma crase dentro de um nome fica."""
    saida, dentro = [], False
    for c in sql:
        if c == "'":
            dentro = not dentro
        saida.append('"' if c == "`" and not dentro else c)
    return "".join(saida)


def carregar(caminho):
    bruto = gzip.open(caminho, "rt", encoding="utf-8") if caminho.endswith(".gz") else open(caminho, encoding="utf-8")
    with bruto as arquivo:
        texto = arquivo.read()
    banco = sqlite3.connect(":memory:")
    criadas = set()
    for sql in comandos(texto):
        sql = sem_crase(sql)
        if not sql.startswith("INSERT INTO"):
            continue
        tabela = sql.split('"', 2)[1]
        if tabela not in TABELAS:
            continue
        if tabela not in criadas:
            colunas = sql[sql.index("(") + 1: sql.index(")")]
            banco.execute(f'CREATE TABLE "{tabela}" ({colunas})')
            criadas.add(tabela)
        banco.execute(sql)
    faltando = {"etiquetas", "staff"} - criadas
    if faltando:
        sys.exit(f"O backup não tem as tabelas {', '.join(sorted(faltando))}. É o arquivo do avalia:exportar?")
    for tabela in TABELAS - criadas:
        banco.execute(f'CREATE TABLE "{tabela}" (id)')
    banco.row_factory = sqlite3.Row
    return banco


def coluna(linha, nome):
    return linha[nome] if nome in linha.keys() else None


def comissao(valor_cents):
    return int((Decimal(valor_cents) * COMISSAO_PCT / 100).quantize(Decimal(1), rounding=ROUND_HALF_UP))


def data_e_hora(valor):
    """`2026-10-12 14:03:00` em UTC vira ISO com fuso, que o importador lê sem supor Brasília."""
    return None if not valor else str(valor).replace(" ", "T")[:19] + "+00:00"


def converter(banco, mapa, socios):
    staff = {s["id"]: s for s in banco.execute("SELECT * FROM staff")}
    negocios = {n["id"]: n for n in banco.execute("SELECT * FROM negocios")} if banco.execute("SELECT count(*) FROM negocios").fetchone()[0] else {}
    produtos = {p["chave"]: p for p in banco.execute("SELECT * FROM produtos")} if "chave" in [c[1] for c in banco.execute("PRAGMA table_info(produtos)")] else {}

    linhas, avisos = [], []
    totais = Counter()
    devida_por_vendedor = defaultdict(int)
    for e in banco.execute("SELECT * FROM etiquetas ORDER BY codigo"):
        if coluna(e, "sem_placa") in (1, "1", True, "true"):
            totais["venda sem placa (fica de fora)"] += 1
            continue

        situacao = e["situacao"]
        vendida = e["vendida_em"]
        if situacao in ("ativa", "suspensa") and not vendida:
            avisos.append(f"{e['codigo']}: {situacao} sem data de venda (o importador recusa)")

        vendedor = staff.get(e["vendedor_id"]) if e["vendedor_id"] is not None else None
        email_antigo = (vendedor["email"] or "").strip().lower() if vendedor else ""
        socio = email_antigo in socios
        paga = bool(coluna(e, "pix_pago_em")) or (vendida is not None and str(vendida) < PAGAMENTO_NO_SISTEMA_DESDE)
        valor = int(e["valor_cents"] or 0)
        devida = comissao(valor) if vendida and paga and vendedor and not socio and not coluna(e, "comissao_paga_em") else 0

        chave = coluna(e, "produto") or PRODUTO_DA_PLACA
        produto = produtos.get(chave)
        negocio = negocios.get(coluna(e, "negocio_id"))
        email = mapa.get(email_antigo, email_antigo) if vendedor else ""

        linhas.append({
            "codigo": e["codigo"],
            "situacao": situacao,
            "destino": e["destino"] or "",
            "vendida_em": data_e_hora(vendida) or "",
            "vence_em": str(e["vence_em"])[:10] if e["vence_em"] else "",
            "valor_cents": valor if vendida else "",
            "custo_cents": e["custo_cents"] if e["custo_cents"] is not None else CUSTO_PADRAO_CENTS,
            "cliente_nome": (negocio["nome"] if negocio else None) or e["cliente_nome"] or "",
            "cliente_responsavel": (negocio["responsavel"] if negocio else None) or "",
            "cliente_telefone": (negocio and (negocio["whatsapp"] or negocio["telefone"])) or e["cliente_contato"] or "",
            "cliente_documento": (negocio["documento"] if negocio else None) or "",
            "vendedor_email": email,
            "produto": chave if vendida else "",
            "produto_preco_cents": (produto["preco_cents"] if produto else "") if vendida else "",
            "comissao_devida_cents": devida or "",
            "total_leituras": e["total_acessos"] or 0,
        })

        totais[situacao] += 1
        if vendida:
            totais["vendidas"] += 1
            totais["valor vendido (centavos)"] += valor
            totais["vendidas não pagas"] += 0 if paga else 1
        if devida:
            devida_por_vendedor[email] += devida

    return linhas, totais, devida_por_vendedor, avisos


def main():
    argumentos = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    argumentos.add_argument("backup", help="arquivo .sql ou .sql.gz do avalia:exportar")
    argumentos.add_argument("--saida", required=True, help="CSV para o --importar-placas")
    argumentos.add_argument("--vendedor", action="append", default=[], metavar="ANTIGO=NOVO",
                            help="e-mail do Sales = login no app, uma vez por pessoa")
    argumentos.add_argument("--socio", action="append", required=True,
                            help="e-mail de sócio no Sales (ETIQUETAS_SOCIOS), uma vez por sócio; venda de sócio não comissiona")
    opcoes = argumentos.parse_args()

    mapa = {}
    for par in opcoes.vendedor:
        antigo, _, novo = par.partition("=")
        if not novo:
            sys.exit(f"--vendedor {par}: use antigo@email=novo@email")
        mapa[antigo.strip().lower()] = novo.strip().lower()
    socios = {s.strip().lower() for s in opcoes.socio}

    linhas, totais, devidas, avisos = converter(carregar(opcoes.backup), mapa, socios)
    with open(opcoes.saida, "w", newline="", encoding="utf-8") as arquivo:
        escritor = csv.DictWriter(arquivo, fieldnames=COLUNAS)
        escritor.writeheader()
        escritor.writerows(linhas)

    print(f"{len(linhas)} placas em {opcoes.saida}")
    for nome, total in sorted(totais.items()):
        print(f"  {nome:32} {total}")
    print("Comissão devida por vendedor (centavos):")
    for email, total in sorted(devidas.items()):
        print(f"  {email:40} {total}")
    for aviso in avisos:
        print(f"Aviso - {aviso}")


if __name__ == "__main__":
    main()
