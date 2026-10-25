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

Com `--historico historico.csv`, grava também o arquivo do `--importar-historico`:
uma linha por venda paga, com a data do pagamento, o valor e a comissão total
(paga ou não), para o razão receber a receita e a comissão nos meses de origem.
Rodar com as mesmas opções da conversão das placas, para o vendedor e a comissão
baterem com o que já foi importado.
"""

import argparse
import csv
import re
import gzip
import sqlite3
import sys
from collections import Counter, defaultdict
from datetime import datetime
from decimal import ROUND_HALF_UP, Decimal
from zoneinfo import ZoneInfo

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
COLUNAS_DO_HISTORICO = ["codigo", "pago_em", "valor_cents", "comissao_cents", "comissao_paga_em", "vendedor_email"]
BRASILIA = ZoneInfo("America/Sao_Paulo")
# O domínio para no primeiro ponto depois do primeiro caractere: sem retrocesso, casa o mesmo que [^@\s]+\.[^@\s]+.
EMAIL = re.compile(r"^[^@\s]+@[^@\s][^@\s.]*\.[^@\s]+$")
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


def instante(valor):
    """Data sem hora passa como está (o importador lê meia-noite em Brasília); com hora, em UTC, como a da venda."""
    if not valor:
        return None
    texto = str(valor).strip()
    return texto[:10] if len(texto) <= 10 else data_e_hora(texto)


def mes_em_brasilia(texto):
    """O mês do razão (competência) para o instante do arquivo, como o importador calcula."""
    if len(texto) == 10:
        return texto[:7]
    quando = datetime.fromisoformat(texto)
    return quando.astimezone(BRASILIA).strftime("%Y-%m")


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

    linhas, historico, avisos = [], [], []
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

        # O histórico financeiro: toda venda paga, com a comissão inteira (paga ou não) e quando foi paga.
        if vendida and paga:
            total = comissao(valor) if vendedor and not socio else 0
            paga_em = instante(coluna(e, "comissao_paga_em"))
            if total and not paga_em and comissao_paga:
                # Acertada fora do Sales, sem data: conta como paga junto com a venda.
                paga_em = instante(coluna(e, "pix_pago_em")) or instante(vendida)
                totais["comissão paga sem data (na data da venda)"] += 1
            if total and not email:
                avisos.append(f"{e['codigo']}: comissão de {email_antigo} sem vendedor mapeado"
                              + ("" if paga_em else " e não paga (o --importar-historico recusa)"))
            historico.append({
                "codigo": e["codigo"],
                "pago_em": instante(coluna(e, "pix_pago_em")) or instante(vendida),
                "valor_cents": valor,
                "comissao_cents": total,
                "comissao_paga_em": (paga_em or "") if total else "",
                "vendedor_email": email,
            })

    return linhas, totais, devida_por_vendedor, avisos, historico


def totais_do_historico(historico):
    """Por mês de Brasília: vendas, valor e comissão no mês do pagamento; comissão paga no mês em que foi paga."""
    meses = defaultdict(Counter)
    for linha in historico:
        if not linha["valor_cents"]:
            continue
        mes = meses[mes_em_brasilia(linha["pago_em"])]
        mes["vendas"] += 1
        mes["valor"] += linha["valor_cents"]
        mes["comissao"] += linha["comissao_cents"]
        if linha["comissao_paga_em"]:
            meses[mes_em_brasilia(linha["comissao_paga_em"])]["paga"] += linha["comissao_cents"]
    return sorted(meses.items())


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
    argumentos.add_argument("--saida", help="CSV para o --importar-placas")
    argumentos.add_argument("--historico", metavar="ARQUIVO",
                            help="CSV para o --importar-historico: receita e comissão de cada venda paga, no mês de origem")
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
    if not opcoes.saida and not opcoes.historico:
        argumentos.error("use --saida, --historico ou os dois")

    mapa = {}
    for par in opcoes.vendedor:
        antigo, _, novo = par.partition("=")
        if not novo:
            sys.exit(f"--vendedor {par}: use antigo@email=novo@email")
        mapa[antigo.strip().lower()] = novo.strip().lower()
    socios = {s.strip().lower() for s in opcoes.socio}

    banco = carregar(opcoes.backup)
    linhas, totais, devidas, avisos, historico = converter(banco, mapa, socios, opcoes.no_ar_sem_venda_a_zero, opcoes.so_os_mapeados, opcoes.comissao_paga)
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
    if opcoes.historico:
        with open(opcoes.historico, "w", newline="", encoding="utf-8") as arquivo:
            escritor = csv.DictWriter(arquivo, fieldnames=COLUNAS_DO_HISTORICO)
            escritor.writeheader()
            escritor.writerows(historico)
        print(f"{len(historico)} vendas pagas em {opcoes.historico} (centavos)")
        print(f"  {'mês':8} {'vendas':>6} {'valor':>12} {'comissão':>10} {'comissão paga':>14}")
        for mes, t in totais_do_historico(historico):
            print(f"  {mes:8} {t['vendas']:6} {t['valor']:12} {t['comissao']:10} {t['paga']:14}")
    if opcoes.saida:
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
