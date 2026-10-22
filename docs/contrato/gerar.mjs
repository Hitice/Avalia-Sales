// Gera o PDF do contrato de revenda a partir do HTML ao lado.
// Uso: node docs/contrato/gerar.mjs (usa o Playwright instalado em web/).
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const pasta = path.dirname(fileURLToPath(import.meta.url));
const { chromium } = createRequire(path.resolve(pasta, '../../web/package.json'))('playwright');
const destino = path.resolve(pasta, '../../src/AvaliaSales.Infrastructure/Recursos/contrato-de-revenda.pdf');

const navegador = await chromium.launch();
const pagina = await navegador.newPage();
await pagina.goto(pathToFileURL(path.join(pasta, 'contrato-de-revenda.html')).href, { waitUntil: 'load' });
await pagina.pdf({
  path: destino,
  format: 'A4',
  preferCSSPageSize: true,
  printBackground: true,
  displayHeaderFooter: true,
  headerTemplate: '<span></span>',
  footerTemplate:
    '<div style="width:100%;font:8px Helvetica,Arial,sans-serif;color:#5b5870;padding:0 20mm;display:flex;justify-content:space-between">' +
    '<span>AVALIA ONE NEGÓCIOS CORPORATIVOS LTDA - CNPJ 68.715.987/0001-64</span>' +
    '<span>Página <span class="pageNumber"></span> de <span class="totalPages"></span></span></div>',
});
await navegador.close();
console.log(destino);
