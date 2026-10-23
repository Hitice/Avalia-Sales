// Exemplo impresso dos modelos que a Plataforma preenche (src/AvaliaSales.Infrastructure/Impressao/Modelos),
// para revisar o texto e o layout sem subir o Gotenberg. Usa o Playwright instalado em web/, o mesmo
// Chromium do conversor. Os dados são de exemplo; na Plataforma vêm do cadastro (DocumentosEmPdf).
// Uso: node docs/contrato/gerar.mjs  ->  docs/contrato/exemplo-*.pdf
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, readdirSync, copyFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const pasta = path.dirname(fileURLToPath(import.meta.url));
const modelos = path.resolve(pasta, '../../src/AvaliaSales.Infrastructure/Impressao/Modelos');
const { chromium } = createRequire(path.resolve(pasta, '../../web/package.json'))('playwright');

const comuns = {
  nome: 'Padaria Pão Quente Ltda.',
  cnpj: '11.222.333/0001-81',
  endereco: 'Rua das Flores, 10, Sala 2, Centro, Uberlândia/MG, CEP 38400-100',
  email: 'dono@padaria.com.br',
  representante: 'Marta Souza',
  local: 'Uberlândia/MG',
  data: '22 de outubro de 2026',
  '{assinatura_avalia': '<span class="linha">Aceito pela AVALIA na emissão deste documento, em 22 de outubro de 2026.</span>',
};
const exemplos = {
  'contrato-de-revenda.html': comuns,
  'distrato.html': {
    ...comuns,
    data_do_contrato: '22 de abril de 2026',
    prazo_de_exportacao: '30 (trinta) dias após a Data Efetiva',
    saldo_do_revendedor: 'R$ 5,97',
    vencimento_do_revendedor: '10 (dez) dias após a Data Efetiva',
    placas_em_estoque: '7',
    lotes: 'LT-0003, LT-0004',
    transacoes: '12 transações, total de R$ 1.798,80',
    pendentes: '1 cobrança aguardando pagamento.',
    '{clientes_ativos':
      '<table class="tabela"><thead><tr><th>Cliente Final</th><th>Placa</th><th>Venda</th><th>Validade</th><th>Renovação</th></tr></thead><tbody>' +
      '<tr><td>Café Central</td><td>AB12CD34</td><td>22/07/2026</td><td>até 22/07/2027</td><td>Em dia</td></tr>' +
      '<tr><td>Restaurante Sabor de Minas</td><td>EF56GH78</td><td>10/09/2025</td><td>até 10/09/2026</td><td>Vencida, na carência</td></tr>' +
      '</tbody></table>',
  },
};

const preencher = (modelo, campos) =>
  modelo.replace(/\{\{\{([a-z_]+)\}\}\}|\{\{([a-z_]+)\}\}/g, (_, cru, campo) => {
    const valor = cru ? campos['{' + cru] : campos[campo];
    if (valor === undefined) throw new Error(`Campo sem valor: ${cru ?? campo}`);
    return valor;
  });

// Os arquivos vão para uma pasta só, como no Gotenberg, que lê tudo pelo nome.
const trabalho = mkdtempSync(path.join(tmpdir(), 'contrato-'));
for (const arquivo of readdirSync(modelos)) copyFileSync(path.join(modelos, arquivo), path.join(trabalho, arquivo));

const navegador = await chromium.launch();
for (const [modelo, campos] of Object.entries(exemplos)) {
  const html = preencher(readFileSync(path.join(modelos, modelo), 'utf8'), campos);
  const arquivo = path.join(trabalho, `preenchido-${modelo}`);
  writeFileSync(arquivo, html);
  const pagina = await navegador.newPage();
  await pagina.goto(pathToFileURL(arquivo).href, { waitUntil: 'load' });
  const titulo = await pagina.title();
  const rodape = preencher(readFileSync(path.join(modelos, 'rodape.html'), 'utf8'), { titulo });
  const destino = path.join(pasta, `exemplo-${modelo.replace(/\.html$/, '.pdf')}`);
  await pagina.pdf({
    path: destino,
    preferCSSPageSize: true,
    printBackground: true,
    displayHeaderFooter: true,
    headerTemplate: '<span></span>',
    footerTemplate: rodape.replace(/[\s\S]*<body>|<\/body>[\s\S]*/g, '').replace('<div>', '<div style="width:100%;font:7.5px Helvetica,Arial,sans-serif;color:#55526a;padding:0 22mm;display:flex;justify-content:space-between">'),
  });
  await pagina.close();
  console.log(destino);
}
await navegador.close();
