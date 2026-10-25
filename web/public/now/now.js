// /now - a produção lida ao vivo, a venda simulada por passos e a amostra de marca. A página se lê
// inteira sem este arquivo: aqui só entra o que responde ao leitor.
// Sem dependência e sem build.

const estreito = matchMedia('(max-width: 959px)');

/* ---------------------------------------------------------------------------
   QR de exemplo: os três quadros de posição e módulos sorteados por semente.
   Não codifica nada; a nota da simulação diz que é exemplo.
   --------------------------------------------------------------------------- */
function desenharQr(alvo) {
  const lado = 25;
  let semente = Number(alvo.dataset.qr) || 1;
  const acaso = () => {
    semente = (semente * 1664525 + 1013904223) % 4294967296;

    return semente / 4294967296;
  };

  const emQuadro = (x, y) => [[0, 0], [lado - 7, 0], [0, lado - 7]].some(([qx, qy]) => x >= qx - 1 && x <= qx + 7 && y >= qy - 1 && y <= qy + 7);
  let caminho = '';

  for (let y = 0; y < lado; y++) {
    for (let x = 0; x < lado; x++) {
      if (! emQuadro(x, y) && acaso() > 0.52) {
        caminho += `M${x} ${y}h1v1h-1z`;
      }
    }
  }

  const quadro = (x, y) => `M${x} ${y}h7v7h-7zM${x + 1} ${y + 1}v5h5v-5zM${x + 2} ${y + 2}h3v3h-3z`;

  alvo.innerHTML = `<svg viewBox="-1 -1 ${lado + 2} ${lado + 2}" shape-rendering="crispEdges"><rect x="-1" y="-1" width="${lado + 2}" height="${lado + 2}" fill="#fff"/><path fill="#18181b" fill-rule="evenodd" d="${quadro(0, 0)}${quadro(lado - 7, 0)}${quadro(0, lado - 7)}${caminho}"/></svg>`;
}

/* ---------------------------------------------------------------------------
   A venda: cada passo da lista mostra a tela dele no celular; as setas andam
   um passo. No celular, escolher na lista leva a vista até o aparelho.
   --------------------------------------------------------------------------- */
function venda(caixa) {
  const telas = [...caixa.querySelectorAll('[data-tela]')];
  const passos = [...caixa.querySelectorAll('[data-ir]')];
  const anterior = caixa.querySelector('[data-anterior]');
  const proximo = caixa.querySelector('[data-proximo]');
  const contagem = caixa.querySelector('[data-contagem]');
  const palco = caixa.querySelector('.venda-palco');
  const total = telas.length;
  let atual = 1;

  const ir = (passo) => {
    atual = Math.min(total, Math.max(1, passo));
    telas.forEach((tela) => { tela.hidden = Number(tela.dataset.tela) !== atual; });
    passos.forEach((botao) => botao.setAttribute('aria-pressed', String(Number(botao.dataset.ir) === atual)));
    contagem.textContent = `Passo ${atual} de ${total}`;
    anterior.disabled = atual === 1;
    proximo.disabled = atual === total;
  };

  passos.forEach((botao) => botao.addEventListener('click', () => {
    ir(Number(botao.dataset.ir));

    if (estreito.matches && palco.getBoundingClientRect().top < 0) {
      palco.scrollIntoView({ block: 'start' });
    }
  }));

  anterior.addEventListener('click', () => ir(atual - 1));
  proximo.addEventListener('click', () => ir(atual + 1));

  ir(1);
}

/* ---------------------------------------------------------------------------
   A marca: luminância relativa e contraste do WCAG, a mesma regra da
   plataforma. O texto do botão é o que mais contrasta com a cor; o destaque
   sobre o branco escurece até 4,5:1.
   --------------------------------------------------------------------------- */
const canal = (v) => {
  const c = v / 255;

  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
};

const rgb = (hex) => [1, 3, 5].map((i) => Number.parseInt(hex.slice(i, i + 2), 16));
const luminancia = (hex) => {
  const [r, g, b] = rgb(hex).map(canal);

  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};

const contraste = (a, b) => {
  const [claro, escuro] = [luminancia(a), luminancia(b)].sort((x, y) => y - x);

  return (claro + 0.05) / (escuro + 0.05);
};

const hexDe = (valores) => `#${valores.map((v) => Math.round(Math.max(0, Math.min(255, v))).toString(16).padStart(2, '0')).join('')}`;
const razao = (valor) => `${valor.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}:1`;

function marca(figura) {
  const mini = figura.querySelector('[data-mini]');
  const leitura = figura.querySelector('[data-contraste]');
  const livre = figura.querySelector('[data-cor-livre]');
  const cores = [...figura.querySelectorAll('input[name="cor"]')];

  const aplicar = (cor) => {
    if (! /^#[0-9a-f]{6}$/i.test(cor)) {
      return;
    }

    const branco = contraste(cor, '#ffffff');
    const escuro = contraste(cor, '#120c2b');
    const texto = branco >= escuro ? '#ffffff' : '#120c2b';

    // Escurece em passos de 4% até o destaque passar de 4,5:1 sobre o branco.
    let destaque = cor;

    for (let fator = 1; contraste(destaque, '#ffffff') < 4.5 && fator > 0; fator -= 0.04) {
      destaque = hexDe(rgb(cor).map((v) => v * fator));
    }

    mini.style.setProperty('--marca', cor);
    mini.style.setProperty('--marca-texto', texto);
    mini.style.setProperty('--marca-destaque', destaque);

    const botao = `Botão com texto ${texto === '#ffffff' ? 'branco' : 'escuro'}, contraste de ${razao(Math.max(branco, escuro))}.`;
    const tom = destaque === cor
      ? ` Destaque na própria cor, ${razao(contraste(cor, '#ffffff'))} sobre o branco.`
      : ` Destaque escurecido para ${destaque}, ${razao(contraste(destaque, '#ffffff'))} sobre o branco.`;

    leitura.textContent = botao + tom;
  };

  cores.forEach((entrada) => entrada.addEventListener('change', () => aplicar(entrada.value)));

  livre.addEventListener('input', () => {
    cores.forEach((entrada) => { entrada.checked = false; });
    aplicar(livre.value);
  });

  aplicar(cores.find((entrada) => entrada.checked)?.value ?? '#bb00bf');
}

/* ---------------------------------------------------------------------------
   A produção ao vivo: o /api/health do próprio domínio a cada 30 s, com o
   tempo de resposta e a hora da leitura. Aba escondida não consulta.
   --------------------------------------------------------------------------- */
function producao(linha) {
  const ponto = linha.querySelector('[data-estado]');
  const texto = linha.querySelector('[data-producao-texto]');
  const hora = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });

  const ler = async () => {
    const controle = new AbortController();
    const prazo = setTimeout(() => controle.abort(), 8000);
    const inicio = performance.now();
    let ok = false;
    try {
      const resposta = await fetch('/api/health', { cache: 'no-store', headers: { Accept: 'text/plain' }, signal: controle.signal });
      ok = resposta.ok && (await resposta.text()).trim() === 'Healthy';
    } catch {
      ok = false;
    } finally {
      clearTimeout(prazo);
    }
    const ms = Math.round(performance.now() - inicio);
    ponto.dataset.estado = ok ? 'ok' : 'fora';
    texto.textContent = ok
      ? `No ar agora: respondeu em ${ms} ms às ${hora.format(new Date())}.`
      : `Sem resposta às ${hora.format(new Date())}. Nova leitura em 30 s.`;
  };

  // O ler trata a própria falha (marca "fora"): a promessa não rejeita, e o void diz isso.
  void ler();
  setInterval(() => { if (! document.hidden) void ler(); }, 30000);
  document.addEventListener('visibilitychange', () => { if (! document.hidden) void ler(); });
}

document.querySelectorAll('[data-producao]').forEach(producao);
document.querySelectorAll('[data-qr]').forEach(desenharQr);
document.querySelectorAll('[data-venda]').forEach(venda);
document.querySelectorAll('[data-marca]').forEach(marca);
