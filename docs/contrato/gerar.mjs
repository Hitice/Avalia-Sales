// Gera o PDF de cada documento desta pasta a partir do HTML (usa o Playwright instalado em web/).
// Uso: node docs/contrato/gerar.mjs [documento.html ...] (sem argumento, gera todos)
// O PDF sai ao lado do HTML. O contrato que a Plataforma envia é o embutido em
// src/AvaliaSales.Infrastructure/Recursos e só é trocado de propósito, copiando o PDF.
import { createRequire } from 'node:module';
import { readdirSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const pasta = path.dirname(fileURLToPath(import.meta.url));
const { chromium } = createRequire(path.resolve(pasta, '../../web/package.json'))('playwright');
const documentos = process.argv.length > 2
  ? process.argv.slice(2).map((d) => path.basename(d))
  : readdirSync(pasta).filter((f) => f.endsWith('.html'));

const rodape = (titulo) =>
  '<div style="width:100%;font:7.5px Helvetica,Arial,sans-serif;color:#55526a;padding:0 22mm;display:flex;justify-content:space-between">' +
  `<span>${titulo}</span>` +
  '<span>Página <span class="pageNumber"></span> de <span class="totalPages"></span></span></div>';

const navegador = await chromium.launch();
for (const documento of documentos) {
  const pagina = await navegador.newPage();
  await pagina.goto(pathToFileURL(path.join(pasta, documento)).href, { waitUntil: 'load' });
  const destino = path.join(pasta, documento.replace(/\.html$/, '.pdf'));
  await pagina.pdf({
    path: destino,
    format: 'A4',
    preferCSSPageSize: true,
    printBackground: true,
    displayHeaderFooter: true,
    headerTemplate: '<span></span>',
    footerTemplate: rodape(`AVALIA ONE NEGÓCIOS CORPORATIVOS LTDA. - CNPJ 68.715.987/0001-64 - ${await pagina.title()}`),
  });
  await pagina.close();
  console.log(destino);
}
await navegador.close();
