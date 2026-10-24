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

Placa no ar sem venda (cadastrada antes de o Sales registrar venda, em 01/10/2026)
o importador recusa. Com `--no-ar-sem-venda-a-zero` ela entra como venda de R$ 0
na data do cadastro, sem vendedor (fica com o dono) e sem comissão.
"""

import argparse
import csv
import re
import gzip
import sqlite3
import sys
from collections import Counter, defaultdict
from decimal import ROUND_HALF_UP, Decimal

COMISSAO_PCT = 25
CUSTO_PADRAO_CENTS = 600
PAGAMENTO_NO_SISTEMA_DESDE = "2026-10-11"
PRODUTO_DA_PLACA = "placa-avaliacao-google"
TABELAS = {"etiquetas", "staff", "negocios", "produtos", "lotes_etiquetas"}

COLUNAS = [
    "codigo", "situacao", "destino", "vendida_em", "vence_em", "valor_cents", "custo_cents",
    "cliente_nome", "cliente_responsavel", "cliente_telefone", "cliente_documento",
    "vendedor_email", "produto", "produto_preco_cents", "comissao_devida_cents", "total_leituras",
    "cliente_email", "cliente_lugar_no_google",
]
EMAIL = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")
LUGAR_NO_DESTINO = re.compile(r"[?&]placeid=([A-Za-z0-9_-]+)")


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


def lugar_no_google(negocio, destino):
    """O place_id do negócio no Sales; sem ele, o do link de avaliação (placeid=...). O g.page não traz."""
    if negocio is not None and coluna(negocio, "place_id"):
        return negocio["place_id"].strip()
    achado = LUGAR_NO_DESTINO.search(destino or "")
    return achado.group(1) if achado else ""


def converter(banco, mapa, socios, no_ar_a_zero=False, so_mapeados=False, comissao_paga=False):
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
        a_zero = situacao in ("ativa", "suspensa") and not vendida
        if a_zero and not no_ar_a_zero:
            avisos.append(f"{e['codigo']}: {situacao} sem data de venda (o importador recusa)")
        a_zero = a_zero and no_ar_a_zero
        if a_zero:
            vendida = e["created_at"]

        vendedor = staff.get(e["vendedor_id"]) if e["vendedor_id"] is not None and not a_zero else None
        email_antigo = (vendedor["email"] or "").strip().lower() if vendedor else ""
        socio = email_antigo in socios
        paga = bool(coluna(e, "pix_pago_em")) or (vendida is not None and str(vendida) < PAGAMENTO_NO_SISTEMA_DESDE)
        valor = 0 if a_zero else int(e["valor_cents"] or 0)
        devida = comissao(valor) if vendida and paga and vendedor and not socio and not coluna(e, "comissao_paga_em") and not comissao_paga else 0

        chave = coluna(e, "produto") or PRODUTO_DA_PLACA
        produto = produtos.get(chave)
        negocio = negocios.get(coluna(e, "negocio_id"))
        email_do_cliente = ((coluna(negocio, "email") if negocio is not None else None) or "").strip().lower()
        email = mapa.get(email_antigo, "" if so_mapeados else email_antigo) if vendedor else ""

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
            "cliente_email": email_do_cliente if vendida and EMAIL.match(email_do_cliente) else "",
            "cliente_lugar_no_google": lugar_no_google(negocio, e["destino"]) if vendida else "",
        })

        totais[situacao] += 1
        if vendida:
            totais["vendidas"] += 1
            totais["valor vendido (centavos)"] += valor
            totais["vendidas não pagas"] += 0 if paga or a_zero else 1
            totais["no ar sem venda, a R$ 0"] += 1 if a_zero else 0
        if devida:
            devida_por_vendedor[email] += devida

    return linhas, totais, devida_por_vendedor, avisos


def categoria_do_lote(titulo):
    """Pelo título que a equipe deu no Sales (Pedro, 24/10/2026): teste, crachá e cardápio pelo nome, campanha é placa, o resto é avulso."""
    t = (titulo or "").strip().lower()
    if "teste" in t:
        return "Teste"
    if "crach" in t:
        return "Cracha"
    if "menu" in t or "card" in t:
        return "Cardapio"
    return "Placa" if t.startswith("campanha") else "Outro"


def lotes_de_origem(banco):
    """Código, lote do Sales, título, categoria e data: para o --lotes-de-origem. Lote de teste sai para arquivar."""
    lotes = {l["id"]: l for l in banco.execute("SELECT * FROM lotes_etiquetas")} if "codigo" in [c[1] for c in banco.execute("PRAGMA table_info(lotes_etiquetas)")] else {}
    linhas = []
    for e in banco.execute("SELECT codigo, lote_id, sem_placa FROM etiquetas ORDER BY codigo"):
        lote = lotes.get(e["lote_id"])
        if lote is None or e["sem_placa"] in (1, "1", True, "true"):
            continue
        categoria = categoria_do_lote(lote["titulo"])
        linhas.append({
            "codigo": e["codigo"],
            "lote": lote["codigo"],
            "titulo": (lote["titulo"] or "").strip(),
            "categoria": categoria,
            "criado_em": data_e_hora(lote["created_at"]) or "",
            "arquivar": "sim" if categoria == "Teste" else "",
        })
    return linhas


def main():
    argumentos = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    argumentos.add_argument("backup", help="arquivo .sql ou .sql.gz do avalia:exportar")
    argumentos.add_argument("--saida", required=True, help="CSV para o --importar-placas")
    argumentos.add_argument("--vendedor", action="append", default=[], metavar="ANTIGO=NOVO",
                            help="e-mail do Sales = login no app, uma vez por pessoa")
    argumentos.add_argument("--socio", action="append", required=True,
                            help="e-mail de sócio no Sales (ETIQUETAS_SOCIOS), uma vez por sócio; venda de sócio não comissiona")
    argumentos.add_argument("--no-ar-sem-venda-a-zero", action="store_true",
                            help="placa ativa ou suspensa sem venda entra como venda de R$ 0 na data do cadastro, sem vendedor")
    argumentos.add_argument("--comissao-paga", action="store_true",
                            help="toda comissão do Sales já foi paga, mesmo sem comissao_paga_em: nada entra como devida")
    argumentos.add_argument("--lotes", metavar="ARQUIVO",
                            help="também grava o CSV do --lotes-de-origem: o lote do Sales de cada código, com título e categoria")
    argumentos.add_argument("--so-os-mapeados", action="store_true",
                            help="venda de quem não está no --vendedor fica com o dono, em vez de ir pelo e-mail do Sales")
    opcoes = argumentos.parse_args()

    mapa = {}
    for par in opcoes.vendedor:
        antigo, _, novo = par.partition("=")
        if not novo:
            sys.exit(f"--vendedor {par}: use antigo@email=novo@email")
        mapa[antigo.strip().lower()] = novo.strip().lower()
    socios = {s.strip().lower() for s in opcoes.socio}

    banco = carregar(opcoes.backup)
    linhas, totais, devidas, avisos = converter(banco, mapa, socios, opcoes.no_ar_sem_venda_a_zero, opcoes.so_os_mapeados, opcoes.comissao_paga)
    if opcoes.lotes:
        de_origem = lotes_de_origem(banco)
        with open(opcoes.lotes, "w", newline="", encoding="utf-8") as arquivo:
            escritor = csv.DictWriter(arquivo, fieldnames=["codigo", "lote", "titulo", "categoria", "criado_em", "arquivar"])
            escritor.writeheader()
            escritor.writerows(de_origem)
        por_lote = Counter((l["lote"], l["categoria"], l["arquivar"]) for l in de_origem)
        print(f"{len(de_origem)} códigos em {len(por_lote)} lotes em {opcoes.lotes}")
        for (lote, categoria, arquivar), total in sorted(por_lote.items()):
            print(f"  {lote}  {categoria:9} {total:4}{'  arquivar' if arquivar else ''}")
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
