// /now - palhetas do quadro, situação lida do /api/health e mapas de rota.
// Sem dependência; o texto real fica no HTML e as palhetas só desenham por cima.

const reduz = window.matchMedia('(prefers-reduced-motion: reduce)');
const ALFABETO = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
const GIRO_MS = 58;
const SAUDE_MS = 30_000;
const SAUDE_PRAZO_MS = 8_000;

/* ---------- Palhetas ---------- */

const fila = new Set();
let girando = false;

function letra(c) {
  return c === ' ' ? '' : c;
}

function mostrar(t, c) {
  if (t.textContent === c) return;
  t.textContent = c;
  // Alterna entre duas animações iguais para reiniciar a dobra a cada troca.
  const a = t.classList.contains('va');
  t.classList.toggle('va', !a);
  t.classList.toggle('vb', a);
}

function girar(agora) {
  for (const t of fila) {
    const j = t._giro;
    const passo = Math.floor((agora - j.inicio) / GIRO_MS);
    if (passo < 0 || passo === j.passo) continue;
    j.passo = passo;
    if (passo >= j.voltas) {
      mostrar(t, j.alvo);
      fila.delete(t);
    } else {
      mostrar(t, ALFABETO[Math.floor(Math.random() * ALFABETO.length)]);
    }
  }
  if (fila.size) requestAnimationFrame(girar);
  else girando = false;
}

function agendar(t, alvo, atraso) {
  if (reduz.matches) {
    fila.delete(t);
    t.textContent = alvo;
    return;
  }
  t._giro = { alvo, inicio: performance.now() + atraso, voltas: 3 + Math.floor(Math.random() * 6), passo: -1 };
  fila.add(t);
  if (!girando) {
    girando = true;
    requestAnimationFrame(girar);
  }
}

function palheta() {
  const t = document.createElement('span');
  t.className = 't';
  return t;
}

// Monta as palhetas de um elemento [data-pal]. data-pal="titulo" quebra por palavra;
// um número fixa a largura em palhetas, para a coluna não mudar quando o texto muda.
function montar(el) {
  const texto = el.textContent.trim();
  const modo = el.dataset.pal;
  const vh = document.createElement('span');
  vh.className = 'vh';
  const pal = document.createElement('span');
  pal.className = 'pal';
  pal.setAttribute('aria-hidden', 'true');
  el.textContent = '';
  el.append(vh, pal);
  el._pal = { vh, pal, modo, largura: Number.parseInt(modo, 10) || 0, tiles: [] };
  if (modo === 'titulo') {
    const tiles = [];
    const alvos = [];
    for (const palavra of texto.toLocaleUpperCase('pt-BR').split(/\s+/)) {
      const grupo = document.createElement('span');
      grupo.className = 'pal-palavra';
      for (const c of palavra) {
        const t = palheta();
        grupo.append(t);
        tiles.push(t);
        alvos.push(c);
      }
      pal.append(grupo);
    }
    vh.textContent = texto;
    el._pal.tiles = tiles;
    el._pal.alvos = alvos;
  } else {
    escrever(el, texto, -1);
  }
}

// Troca o texto; atraso < 0 só prepara as palhetas vazias, sem girar.
function escrever(el, texto, atraso) {
  const p = el._pal;
  p.vh.textContent = texto;
  const chars = [...texto.toLocaleUpperCase('pt-BR')];
  const total = Math.max(p.largura, chars.length);
  while (p.tiles.length < total) {
    const t = palheta();
    p.pal.append(t);
    p.tiles.push(t);
  }
  while (p.tiles.length > total) p.tiles.pop().remove();
  p.alvos = p.tiles.map((_, i) => letra(chars[i] ?? ' '));
  if (atraso >= 0) cascata(el, atraso);
}

function cascata(el, atraso, passo = 30) {
  const { tiles, alvos } = el._pal;
  tiles.forEach((t, i) => {
    if (alvos[i] === '' && t.textContent === '') {
      // Palheta já vazia: só desmarca um giro anterior que ainda não começou.
      fila.delete(t);
      return;
    }
    agendar(t, alvos[i], atraso + i * passo);
  });
}

function prepararQuadro() {
  document.querySelectorAll('[data-pal]').forEach(montar);

  const titulo = document.querySelector('[data-pal="titulo"]');
  if (titulo) cascata(titulo, 0, 22);

  document.querySelectorAll('#quadro tbody tr').forEach((tr, linha) => {
    const base = 180 + linha * 70;
    tr.querySelectorAll('[data-pal]').forEach((el, coluna) => cascata(el, base + coluna * 220));
  });

  document.querySelectorAll('.numeros [data-pal]').forEach((el, i) => cascata(el, 900 + i * 90, 40));
}

/* ---------- Situação da produção ---------- */

const ESTADOS = {
  wait: { situacao: 'Lendo', selo: 'Lendo' },
  ok: { situacao: 'Operacional', selo: 'Ao vivo' },
  off: { situacao: 'Fora do ar', selo: 'Fora do ar' },
};
let estadoAtual = 'wait';

function aplicar(estado) {
  const hora = new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  const aovivo = document.getElementById('aovivo');
  const rotuloHora = document.getElementById('aovivo-hora');
  if (rotuloHora) rotuloHora.textContent = `lido às ${hora}`;
  if (aovivo) aovivo.dataset.estado = estado;
  if (estado === estadoAtual) return;
  estadoAtual = estado;

  const celula = document.getElementById('sit-producao');
  if (celula) {
    celula.dataset.estado = estado;
    if (celula._pal) escrever(celula, ESTADOS[estado].situacao, 0);
    else celula.textContent = ESTADOS[estado].situacao;
  }
  const selo = document.getElementById('selo-producao');
  if (selo) {
    selo.dataset.estado = estado;
    selo.textContent = ESTADOS[estado].selo;
  }
}

async function lerSaude() {
  if (document.hidden) return;
  const controle = new AbortController();
  const prazo = setTimeout(() => controle.abort(), SAUDE_PRAZO_MS);
  let ok = false;
  try {
    const resposta = await fetch('/api/health', {
      cache: 'no-store',
      headers: { Accept: 'text/plain' },
      signal: controle.signal,
    });
    ok = resposta.ok && (await resposta.text()).trim() === 'Healthy';
  } catch {
    ok = false;
  } finally {
    clearTimeout(prazo);
  }
  aplicar(ok ? 'ok' : 'off');
}

function vigiarSaude() {
  lerSaude();
  setInterval(lerSaude, SAUDE_MS);
  document.addEventListener('visibilitychange', () => {
    if (!document.hidden) lerSaude();
  });
}

/* ---------- Mapas de rota ---------- */

const SVG = 'http://www.w3.org/2000/svg';
const PARADA_MS = 170;
const TRECHO_MS = 520;

function suave(k) {
  return k < 0.5 ? 4 * k * k * k : 1 - (-2 * k + 2) ** 3 / 2;
}

// Caminho ortogonal com cantos arredondados.
function caminho(v, raio = 14) {
  let d = `M${v[0].x},${v[0].y}`;
  for (let i = 1; i < v.length - 1; i++) {
    const p = v[i - 1];
    const c = v[i];
    const n = v[i + 1];
    const d1 = Math.hypot(c.x - p.x, c.y - p.y);
    const d2 = Math.hypot(n.x - c.x, n.y - c.y);
    const reto = Math.abs((c.x - p.x) * (n.y - c.y) - (c.y - p.y) * (n.x - c.x)) < 0.5;
    if (reto || d1 === 0 || d2 === 0) {
      d += ` L${c.x},${c.y}`;
      continue;
    }
    const r = Math.min(raio, d1 / 2, d2 / 2);
    const ax = c.x - ((c.x - p.x) / d1) * r;
    const ay = c.y - ((c.y - p.y) / d1) * r;
    const bx = c.x + ((n.x - c.x) / d2) * r;
    const by = c.y + ((n.y - c.y) / d2) * r;
    d += ` L${ax},${ay} Q${c.x},${c.y} ${bx},${by}`;
  }
  const u = v[v.length - 1];
  return `${d} L${u.x},${u.y}`;
}

function elementoSvg(nome, classe) {
  const e = document.createElementNS(SVG, nome);
  e.setAttribute('class', classe);
  return e;
}

function medir(r) {
  const caixa = r.svg.getBoundingClientRect();
  if (!caixa.width || !caixa.height) return false;
  const horizontal = getComputedStyle(r.ol).getPropertyValue('--trilho').trim() === 'horizontal';
  const pts = r.lis.map((li) => {
    const b = li.querySelector('.ponto').getBoundingClientRect();
    return { x: b.left + b.width / 2 - caixa.left, y: b.top + b.height / 2 - caixa.top };
  });

  const verts = [pts[0]];
  const indices = [0];
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1];
    const b = pts[i];
    const mesmaFila = Math.abs(a.y - b.y) < 2;
    const mesmaColuna = Math.abs(a.x - b.x) < 2;
    if (mesmaFila || (mesmaColuna && !horizontal)) {
      verts.push(b);
    } else if (mesmaColuna) {
      // Serpente: a rota sai pela margem, desce e volta, sem cruzar o texto da estação.
      const gx = a.x > caixa.width / 2 ? caixa.width - 18 : -18;
      verts.push({ x: gx, y: a.y }, { x: gx, y: b.y }, b);
    } else {
      const my = (a.y + b.y) / 2;
      verts.push({ x: a.x, y: my }, { x: b.x, y: my }, b);
    }
    indices.push(verts.length - 1);
  }

  const d = caminho(verts);
  r.base.setAttribute('d', d);
  r.aceso.setAttribute('d', d);
  r.L = r.aceso.getTotalLength();
  const regua = document.createElementNS(SVG, 'path');
  r.svg.append(regua);
  r.marcos = indices.map((ix) => {
    if (ix === 0) return 0;
    regua.setAttribute('d', caminho(verts.slice(0, ix + 1)));
    return regua.getTotalLength();
  });
  regua.remove();
  r.aceso.style.strokeDasharray = `${r.L} ${r.L}`;
  return true;
}

function quadroFinal(r, feito) {
  r.aceso.style.strokeDashoffset = feito ? '0' : String(r.L);
  r.pulso.style.opacity = '0';
  r.lis.forEach((li) => {
    li.classList.toggle('passou', feito);
    li.classList.remove('chegou');
  });
}

function parar(r) {
  if (r.raf) cancelAnimationFrame(r.raf);
  r.raf = 0;
  if (r.fim) {
    const fim = r.fim;
    r.fim = null;
    fim();
  }
}

function percorrer(r) {
  parar(r);
  if (!r.L) return Promise.resolve();
  if (reduz.matches) {
    r.estado = 'feito';
    quadroFinal(r, true);
    return Promise.resolve();
  }
  r.estado = 'andando';
  quadroFinal(r, false);
  const n = r.marcos.length;
  const ciclo = PARADA_MS + TRECHO_MS;
  const inicio = performance.now();
  r.pulso.style.opacity = '1';

  return new Promise((resolve) => {
    r.fim = () => {
      r.estado = 'feito';
      quadroFinal(r, true);
      resolve();
    };
    const passo = (agora) => {
      const t = Math.max(0, agora - inicio);
      const j = Math.floor(t / ciclo);
      if (j >= n - 1) {
        const p = r.aceso.getPointAtLength(r.L);
        r.pulso.setAttribute('cx', p.x);
        r.pulso.setAttribute('cy', p.y);
        r.aceso.style.strokeDashoffset = '0';
        r.lis.forEach((li) => li.classList.add('passou'));
        r.lis[n - 1].classList.add('chegou');
        if (t >= (n - 1) * ciclo + PARADA_MS * 2) {
          r.raf = 0;
          if (r.fim) {
            const fim = r.fim;
            r.fim = null;
            fim();
          }
          return;
        }
        r.raf = requestAnimationFrame(passo);
        return;
      }
      const local = t - j * ciclo;
      const k = local < PARADA_MS ? 0 : suave((local - PARADA_MS) / TRECHO_MS);
      const s = r.marcos[j] + (r.marcos[j + 1] - r.marcos[j]) * k;
      const p = r.aceso.getPointAtLength(s);
      r.pulso.setAttribute('cx', p.x);
      r.pulso.setAttribute('cy', p.y);
      r.aceso.style.strokeDashoffset = String(r.L - s);
      r.lis.forEach((li, i) => {
        li.classList.toggle('passou', i <= j);
        li.classList.toggle('chegou', i === j && local < PARADA_MS);
      });
      r.raf = requestAnimationFrame(passo);
    };
    r.raf = requestAnimationFrame(passo);
  });
}

function prepararRota(el) {
  const svg = el.querySelector('.trilho');
  const ol = el.querySelector('.estacoes');
  if (!svg || !ol) return null;
  const r = {
    el,
    svg,
    ol,
    lis: [...ol.children],
    base: elementoSvg('path', 'trilho-base'),
    aceso: elementoSvg('path', 'trilho-aceso'),
    pulso: elementoSvg('circle', 'trilho-pulso'),
    L: 0,
    marcos: [],
    estado: 'antes',
    raf: 0,
    fim: null,
  };
  r.pulso.setAttribute('r', '6');
  r.pulso.style.opacity = '0';
  svg.append(r.base, r.aceso, r.pulso);
  if (!medir(r)) return r;
  el.classList.add('viva');
  quadroFinal(r, reduz.matches);
  if (reduz.matches) r.estado = 'feito';
  return r;
}

function remedir(r) {
  const andando = r.estado === 'andando';
  if (andando) parar(r);
  if (!medir(r)) return;
  r.el.classList.add('viva');
  quadroFinal(r, r.estado === 'feito' || reduz.matches);
}

async function percorrerFluxo(fluxo) {
  const vez = (fluxo._vez = (fluxo._vez || 0) + 1);
  fluxo._rotas.forEach((r) => {
    parar(r);
    r.estado = 'antes';
    quadroFinal(r, false);
  });
  for (const r of fluxo._rotas) {
    if (fluxo._vez !== vez) return;
    await percorrer(r);
  }
}

function prepararFluxos() {
  const fluxos = [...document.querySelectorAll('.fluxo')];
  const todas = [];
  for (const f of fluxos) {
    f._rotas = [...f.querySelectorAll('.rota')].map(prepararRota).filter(Boolean);
    todas.push(...f._rotas);
  }

  let pendente = 0;
  const remedirTudo = () => {
    cancelAnimationFrame(pendente);
    pendente = requestAnimationFrame(() => todas.forEach(remedir));
  };
  if ('ResizeObserver' in window) {
    const ro = new ResizeObserver(remedirTudo);
    todas.forEach((r) => ro.observe(r.el));
  } else {
    window.addEventListener('resize', remedirTudo);
  }
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(remedirTudo);

  if ('IntersectionObserver' in window && !reduz.matches) {
    const io = new IntersectionObserver(
      (entradas) => {
        for (const e of entradas) {
          if (!e.isIntersecting) continue;
          io.unobserve(e.target);
          percorrerFluxo(e.target);
        }
      },
      { threshold: 0.35 },
    );
    fluxos.forEach((f) => io.observe(f));
  } else {
    todas.forEach((r) => {
      r.estado = 'feito';
      quadroFinal(r, true);
    });
  }

  document.querySelectorAll('[data-replay]').forEach((botao) => {
    botao.addEventListener('click', () => {
      const f = document.getElementById(botao.dataset.replay);
      if (f && f._rotas) percorrerFluxo(f);
    });
  });
}

prepararQuadro();
vigiarSaude();
prepararFluxos();
