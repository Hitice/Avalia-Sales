// /now - a abertura com fumaça e frase digitada, a barra de leitura, os
// números que contam e as peças interativas (a venda, a parede entre revendas,
// a marca, as esteiras e a faixa da stack). Sem dependência e sem build.
// O HTML já traz o estado final de tudo: este arquivo só anima e responde.

const raiz = document.documentElement;
const parado = matchMedia('(prefers-reduced-motion: reduce)').matches;

raiz.classList.add('js');

if (! parado) {
  raiz.classList.add('movimento');
}

/* ---------------------------------------------------------------------------
   Fumaça em WebGL, só onde vale a pena. Economia de dados ou aparelho fraco:
   fica a fumaça de CSS parada, sem baixar o módulo nem ligar a GPU.
   --------------------------------------------------------------------------- */
function ligarFumaca() {
  const canvas = document.querySelector('[data-fumaca]');
  const hero = canvas?.closest('[data-hero]');

  if (! canvas || ! hero) {
    return;
  }

  const economia = navigator.connection?.saveData === true
    || (navigator.deviceMemory !== undefined && navigator.deviceMemory <= 2)
    || (navigator.hardwareConcurrency !== undefined && navigator.hardwareConcurrency <= 2);

  if (economia) {
    hero.classList.add('fumaca-parada');
    canvas.remove();

    return;
  }

  import('/now/fumaca.js')
    .then(({ iniciar }) => {
      if (iniciar(canvas) === false) {
        hero.classList.add('fumaca-parada');
        canvas.remove();
      }
    })
    .catch(() => hero.classList.add('fumaca-parada'));
}

/* ---------------------------------------------------------------------------
   Rolagem: a abertura sobe e apaga, e a barra de leitura avança.
   --------------------------------------------------------------------------- */
function ligarRolagem() {
  const hero = document.querySelector('[data-hero]');
  const barra = document.querySelector('[data-barra]');
  let pendente = false;

  const medir = () => {
    pendente = false;

    const altura = Math.max(1, raiz.scrollHeight - innerHeight);
    const lido = Math.min(1, Math.max(0, scrollY / altura));

    if (barra) {
      barra.style.transform = `scaleX(${lido})`;
    }

    if (hero && ! parado) {
      hero.style.setProperty('--rolagem', Math.min(1, scrollY / innerHeight).toFixed(3));
    }
  };

  addEventListener('scroll', () => {
    if (! pendente) {
      pendente = true;
      requestAnimationFrame(medir);
    }
  }, { passive: true });

  addEventListener('resize', medir, { passive: true });
  medir();
}

/* Tempo de leitura a 200 palavras por minuto, contado no texto do documento. */
function tempoDeLeitura() {
  const documento = document.querySelector('.documento');
  const selo = document.querySelector('[data-leitura]');

  if (! documento || ! selo) {
    return;
  }

  // Conta só o que está à vista: o detalhe técnico fechado não pesa no tempo de leitura.
  const visivel = documento.cloneNode(true);
  visivel.querySelectorAll('details').forEach((d) => d.remove());
  const palavras = visivel.textContent.trim().split(/\s+/).length;

  selo.querySelector('span').textContent = `${Math.max(1, Math.round(palavras / 200))} min de leitura`;
  selo.hidden = false;
}

/* ---------------------------------------------------------------------------
   Frase que se escreve sozinha. A frase já vem inteira no HTML e cada letra
   vira um span invisível que aparece na sua vez: o texto ocupa desde o início
   o espaço final, e a página não pula. Com movimento reduzido, nada roda.
   --------------------------------------------------------------------------- */
function digitar(frase) {
  if (parado) {
    return;
  }

  const letras = [];

  const quebrar = (no) => {
    [...no.childNodes].forEach((filho) => {
      if (filho.nodeType !== Node.TEXT_NODE) {
        if (filho.namespaceURI !== 'http://www.w3.org/2000/svg') {
          quebrar(filho);
        }

        return;
      }

      const pedaco = document.createDocumentFragment();

      [...filho.textContent].forEach((caractere) => {
        const letra = document.createElement('span');

        letra.className = 'letra';
        letra.textContent = caractere;
        pedaco.appendChild(letra);
        letras.push(letra);
      });

      filho.replaceWith(pedaco);
    });
  };

  frase.setAttribute('aria-label', frase.textContent.replace(/\s+/g, ' ').trim());
  quebrar(frase);
  frase.classList.add('digitando');

  let indice = 0;

  const passo = () => {
    letras[indice - 1]?.classList.remove('atual');

    const letra = letras[indice];

    if (! letra) {
      letras[letras.length - 1].classList.add('atual');
      frase.dispatchEvent(new CustomEvent('digitada'));
      setTimeout(() => frase.classList.add('digitada'), 1800);

      return;
    }

    letra.classList.add('vista', 'atual');

    const palavra = letra.closest('b');

    if (palavra && letra === palavra.lastElementChild) {
      palavra.classList.add('acesa');
    }

    indice++;

    // Ritmo de quem digita: espaço é rápido, letra varia um pouco.
    setTimeout(passo, letra.textContent === ' ' ? 40 : 55 + Math.random() * 45);
  };

  setTimeout(passo, Number(frase.dataset.digitar) || 0);
}

/* ---------------------------------------------------------------------------
   As duas palavras que trocam de lugar pelos arcos das setas. A distância e o
   desenho saem da medida das palavras na tela (offsetLeft ignora o transform,
   então a medida no meio da troca dá o lugar de repouso).
   --------------------------------------------------------------------------- */
function trocar(caixa) {
  const primeira = caixa.querySelector('.viaja-a');
  const segunda = caixa.querySelector('.viaja-b');
  const cima = caixa.querySelector('.seta-cima');
  const baixo = caixa.querySelector('.seta-baixo');
  const desenho = caixa.querySelector('svg');

  if (! primeira || ! segunda || ! cima || ! baixo) {
    return;
  }

  const medir = () => {
    const em = parseFloat(getComputedStyle(caixa).fontSize);
    const largura = caixa.offsetWidth;
    const altura = caixa.offsetHeight;
    const x1 = primeira.offsetLeft + primeira.offsetWidth / 2;
    const x2 = segunda.offsetLeft + segunda.offsetWidth / 2;
    const folga = em * 0.12;
    const inicio = primeira.offsetLeft;
    const fim = segunda.offsetLeft + segunda.offsetWidth;

    // Troca pelas bordas, e não pelo centro: as palavras têm larguras
    // diferentes; o "e" anda a diferença para os espaços ficarem iguais.
    caixa.style.setProperty('--ida-a', `${fim - primeira.offsetWidth - inicio}px`);
    caixa.style.setProperty('--ida-b', `${inicio - segunda.offsetLeft}px`);
    caixa.style.setProperty('--ida-e', `${segunda.offsetWidth - primeira.offsetWidth}px`);
    desenho.setAttribute('viewBox', `0 0 ${largura} ${altura}`);
    desenho.style.setProperty('--traco', `${Math.max(2, em * 0.055)}px`);

    // Arco de círculo pela corda e pela flecha: r = (c²/4 + f²) / 2f.
    const corda = x2 - x1;
    const flecha = em * 0.7;
    const raio = (corda * corda / 4 + flecha * flecha) / (2 * flecha);

    cima.setAttribute('d', `M ${x1} ${folga} A ${raio} ${raio} 0 0 1 ${x2 - em * 0.08} ${folga}`);
    baixo.setAttribute('d', `M ${x2} ${altura - folga} A ${raio} ${raio} 0 0 1 ${x1 + em * 0.08} ${altura - folga}`);

    desenho.querySelectorAll('linearGradient').forEach((degrade) => {
      degrade.setAttribute('x1', x1);
      degrade.setAttribute('x2', x2);
    });
  };

  medir();
  new ResizeObserver(medir).observe(caixa);
  document.fonts?.ready.then(medir);

  if (parado) {
    caixa.classList.add('setas-paradas');

    return;
  }

  const frase = caixa.closest('[data-digitar]');
  const comecar = () => caixa.classList.add('trocando');

  if (frase) {
    frase.addEventListener('digitada', () => setTimeout(comecar, 600), { once: true });
  } else {
    comecar();
  }
}

/* ---------------------------------------------------------------------------
   Quando entra na tela. `quase` pede o elemento quase inteiro visível: com a
   abertura ocupando a tela, qualquer pixel disparava antes da hora.
   --------------------------------------------------------------------------- */
function quandoVisivel(elemento, fazer, limiar = 0.3) {
  if (parado || ! ('IntersectionObserver' in window)) {
    fazer();

    return;
  }

  const observador = new IntersectionObserver((entradas) => {
    if (entradas.some((entrada) => entrada.isIntersecting)) {
      observador.disconnect();
      fazer();
    }
  }, { threshold: limiar });

  observador.observe(elemento);
}

/* Número que conta de zero até o valor. O valor final já está no HTML. */
function contar(alvo) {
  const valor = Number(alvo.dataset.contar);

  if (parado || ! ('IntersectionObserver' in window) || ! Number.isFinite(valor)) {
    return;
  }

  alvo.textContent = '0';

  quandoVisivel(alvo.closest('li') ?? alvo, () => {
    const inicio = performance.now();

    const passo = (agora) => {
      const p = Math.min((agora - inicio) / 1400, 1);

      alvo.textContent = Math.round(valor * (1 - (1 - p) ** 3)).toLocaleString('pt-BR');

      if (p < 1) {
        requestAnimationFrame(passo);
      }
    };

    requestAnimationFrame(passo);
  }, 0.9);
}

/* ---------------------------------------------------------------------------
   Esteiras: um pulso acende os passos em ordem. "Play" repete.
   --------------------------------------------------------------------------- */
function esteira(figura) {
  const passos = [...figura.querySelectorAll('.esteira-linha > li')];
  const botao = figura.querySelector('[data-rodar]');
  let relogios = [];

  const rodar = () => {
    relogios.forEach(clearTimeout);
    relogios = [];

    if (parado) {
      passos.forEach((passo) => passo.classList.add('acesa'));

      return;
    }

    passos.forEach((passo) => passo.classList.remove('acesa'));
    passos.forEach((passo, i) => {
      relogios.push(setTimeout(() => passo.classList.add('acesa'), 250 + i * 480));
    });
  };

  botao?.addEventListener('click', rodar);
  quandoVisivel(figura, rodar, 0.45);
}

/* ---------------------------------------------------------------------------
   QR de enfeite: os três quadros de posição e módulos sorteados por semente.
   Não codifica nada; o rótulo da tela diz que é exemplo.
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

  alvo.innerHTML = `<svg viewBox="-1 -1 ${lado + 2} ${lado + 2}" shape-rendering="crispEdges"><rect x="-1" y="-1" width="${lado + 2}" height="${lado + 2}" fill="#fff"/><path fill="#120c2b" fill-rule="evenodd" d="${quadro(0, 0)}${quadro(lado - 7, 0)}${quadro(0, lado - 7)}${caminho}"/></svg>`;
}

/* ---------------------------------------------------------------------------
   A trilha das seções: um ponto por h2 do documento, a linha preenchida até
   onde se leu e a alça que se arrasta para percorrer a página, com encaixe na
   seção mais próxima ao soltar. Os pontos são links: quem usa teclado navega
   por eles, e a alça é só para o ponteiro.
   --------------------------------------------------------------------------- */
function trilha() {
  const secoes = [...document.querySelectorAll('.documento h2[id]')];
  if (secoes.length < 2) return;

  const nav = document.createElement('nav');
  nav.className = 'trilha';
  nav.setAttribute('aria-label', 'Seções');
  nav.innerHTML = '<i class="trilha-linha" aria-hidden="true"></i><i class="trilha-feita" aria-hidden="true"></i><ol></ol><i class="trilha-alca" aria-hidden="true"></i>';
  const lista = nav.querySelector('ol');
  const alca = nav.querySelector('.trilha-alca');
  const ultimo = secoes.length - 1;

  secoes.forEach((secao, n) => {
    const item = document.createElement('li');
    item.style.setProperty('--pos', `${(n / ultimo) * 100}%`);
    item.innerHTML = `<a href="#${secao.id}"><span></span></a>`;
    item.querySelector('span').textContent = secao.textContent.trim();
    lista.append(item);
  });
  document.body.append(nav);
  const itens = [...lista.children];

  const folga = () => window.innerHeight * 0.3;
  const topo = (secao) => secao.getBoundingClientRect().top + window.scrollY;

  // Onde se está, de 0 ao último índice: a seção atual mais a fração até a próxima.
  const posicao = () => {
    const y = window.scrollY + folga();
    if (y <= topo(secoes[0])) return 0;
    for (let n = 0; n < ultimo; n++) {
      const a = topo(secoes[n]);
      const b = topo(secoes[n + 1]);
      if (y < b) return n + (y - a) / (b - a);
    }
    return ultimo;
  };

  const rolarPara = (p, suave) => {
    const n = Math.min(ultimo, Math.floor(p));
    const a = topo(secoes[n]);
    const b = n < ultimo ? topo(secoes[n + 1]) : a;
    window.scrollTo({ top: a + (b - a) * (p - n) - folga() + 1, behavior: suave ? 'smooth' : 'instant' });
  };

  let arrastando = false;
  const pinta = () => {
    const p = posicao();
    const pct = `${(p / ultimo) * 100}%`;
    nav.style.setProperty('--feito', pct);
    if (!arrastando) nav.style.setProperty('--alca', pct);
    itens.forEach((item, n) => {
      item.classList.toggle('lida', n <= p);
      item.classList.toggle('ativa', n === Math.round(p));
    });
    nav.classList.toggle('visivel', window.scrollY > window.innerHeight * 0.6);
  };

  const pelaAltura = (evento) => {
    const caixa = nav.getBoundingClientRect();
    const f = Math.max(0, Math.min(1, (evento.clientY - caixa.top) / caixa.height));
    nav.style.setProperty('--alca', `${f * 100}%`);
    return f * ultimo;
  };

  // Enquanto o botão está apertado, o movimento é ouvido na janela inteira:
  // não depende de o ponteiro continuar preso à alça.
  const move = (evento) => rolarPara(pelaAltura(evento), false);
  const solta = (evento) => {
    window.removeEventListener('pointermove', move);
    window.removeEventListener('pointerup', solta);
    window.removeEventListener('pointercancel', solta);
    arrastando = false;
    nav.classList.remove('arrastando');
    rolarPara(Math.round(pelaAltura(evento)), true);
  };

  alca.addEventListener('pointerdown', (evento) => {
    evento.preventDefault();
    arrastando = true;
    nav.classList.add('arrastando');
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', solta);
    window.addEventListener('pointercancel', solta);
  });

  window.addEventListener('scroll', pinta, { passive: true });
  window.addEventListener('resize', pinta);
  pinta();
}

/* ---------------------------------------------------------------------------
   O título que se escreve: letras em spans com o índice na variável --i, as
   palavras agrupadas para a quebra de linha não cortar no meio. O leitor de
   tela recebe a frase inteira pelo aria-label.
   --------------------------------------------------------------------------- */
function escrever(alvo) {
  const frase = alvo.textContent.trim();
  let i = 0;

  alvo.setAttribute('aria-label', frase);
  alvo.textContent = '';
  frase.split(' ').forEach((palavra, n) => {
    const grupo = document.createElement('span');

    grupo.className = 'palavra';
    grupo.setAttribute('aria-hidden', 'true');
    [...(n > 0 ? ' ' + palavra : palavra)].forEach((letra) => {
      const span = document.createElement('span');

      span.className = 'letra';
      span.style.setProperty('--i', String(i++));
      span.textContent = letra;
      grupo.append(span);
    });
    alvo.append(grupo);
  });

  const cursor = document.createElement('span');

  cursor.className = 'cursor';
  cursor.setAttribute('aria-hidden', 'true');
  alvo.lastChild.append(cursor);
}

/* ---------------------------------------------------------------------------
   A venda simulada. A etapa vive no data-etapa do telefone; o CSS mostra a
   tela dela, e as animações reiniciam sozinhas quando o atributo muda.
   --------------------------------------------------------------------------- */
function venda(figura) {
  const telefone = figura.querySelector('.telefone');
  const avancar = figura.querySelector('.venda-controles [data-avancar]');
  const recomecar = figura.querySelector('[data-recomecar]');
  const status = figura.querySelector('[data-venda-status]');
  const destravar = figura.querySelector('[data-destravar]');
  const alca = figura.querySelector('[data-destravar-botao]');
  const passos = [...figura.querySelectorAll('[data-passo]')];
  const total = 6;

  const rotulos = ['Vender', 'Ler a placa', 'Cobrar no Pix', 'Simular o pagamento', 'Ver a divisão', 'Pôr a placa no ar', 'Vender de novo'];
  const avisos = [
    'Celular bloqueado. Deslize para vender.',
    'Etapa 1 de 6: placa de avaliação e perfil no Google na mesma venda, total de R$ 300,00.',
    'Etapa 2 de 6: QR lido, placa AV7K2Q vinculada ao cliente.',
    'Etapa 3 de 6: Pix de R$ 300,00 na tela, aguardando pagamento.',
    'Etapa 4 de 6: pagamento confirmado, a tela mudou para Pago sozinha.',
    'Etapa 5 de 6: revenda R$ 180,00, comissão do vendedor R$ 30,00, operadora R$ 90,00. Valores de exemplo.',
    'Etapa 6 de 6: placa no ar, apontando para a avaliação no Google do negócio.',
  ];

  const ir = (etapa) => {
    const atual = Math.max(0, Math.min(total, etapa));

    telefone.dataset.etapa = String(atual);
    avancar.textContent = rotulos[atual];
    avancar.hidden = atual === 0;
    recomecar.hidden = atual === 0 || atual === total;
    status.textContent = avisos[atual];

    passos.forEach((passo) => {
      const numero = Number(passo.dataset.passo);

      passo.classList.toggle('feita', numero < atual);

      if (numero === atual) {
        passo.setAttribute('aria-current', 'step');
      } else {
        passo.removeAttribute('aria-current');
      }
    });
  };

  avancar.addEventListener('click', () => {
    const atual = Number(telefone.dataset.etapa);

    ir(atual >= total ? 0 : atual + 1);

    if (atual >= total) {
      alca.focus();
    }
  });

  recomecar.addEventListener('click', () => {
    ir(0);
    alca.focus();
  });

  // Deslizar a alça até o fim destrava; tocar sem arrastar, ou Enter, também.
  let inicio = null;
  let andou = 0;
  const limite = () => destravar.clientWidth - alca.offsetWidth - 8;
  const destrava = () => {
    alca.style.transform = `translateX(${limite()}px)`;
    window.setTimeout(() => {
      ir(1);
      alca.style.transform = '';
      avancar.focus();
    }, 220);
  };

  alca.addEventListener('pointerdown', (evento) => {
    inicio = evento.clientX;
    andou = 0;
    alca.setPointerCapture(evento.pointerId);
    destravar.classList.add('arrastando');
  });

  alca.addEventListener('pointermove', (evento) => {
    if (inicio === null) return;
    andou = Math.max(0, Math.min(limite(), evento.clientX - inicio));
    alca.style.transform = `translateX(${andou}px)`;
  });

  const solta = () => {
    if (inicio === null) return;
    inicio = null;
    destravar.classList.remove('arrastando');

    if (andou < 4 || andou > limite() * 0.6) {
      destrava();
    } else {
      alca.style.transform = '';
    }
  };

  alca.addEventListener('pointerup', solta);
  alca.addEventListener('pointercancel', () => {
    inicio = null;
    destravar.classList.remove('arrastando');
    alca.style.transform = '';
  });

  alca.addEventListener('click', (evento) => {
    // O clique do teclado chega sem ponteiro (detail 0); o do mouse já destravou no pointerup.
    if (evento.detail === 0) destrava();
  });

  figura.querySelectorAll('[data-ir]').forEach((botao) => {
    botao.addEventListener('click', () => ir(Number(botao.dataset.ir)));
  });

  ir(0);
}

/* ---------------------------------------------------------------------------
   A parede entre revendas: o pedido da Aurora bate no filtro do código, ou,
   com o filtro desligado, passa dele e bate na trava do banco.
   --------------------------------------------------------------------------- */
function isolamento(figura) {
  const botao = figura.querySelector('[data-tentar]');
  const chave = figura.querySelector('[data-sem-filtro]');
  const pacote = figura.querySelector('.isola-pacote');
  const resultado = figura.querySelector('[data-isola-resultado]');
  const paredeCodigo = figura.querySelector('.parede-codigo');
  const paredeBanco = figura.querySelector('.parede-banco');

  const textos = {
    codigo: 'Barrado no código: a consulta da Aurora só enxerga a Aurora. <b>Nenhuma linha da Ipê.</b>',
    banco: 'Sem o filtro, a consulta passou do código e o banco barrou: a política de linha devolve só o que é da Aurora. <b>Nenhuma linha da Ipê.</b>',
  };

  chave.addEventListener('change', () => {
    figura.classList.toggle('sem-filtro', chave.checked);
    resultado.textContent = chave.checked
      ? 'Filtro do código desligado. Tente de novo.'
      : 'Filtro do código ligado. A Aurora vê só a própria carteira.';
  });

  botao.addEventListener('click', () => {
    const parede = chave.checked ? paredeBanco : paredeCodigo;
    const texto = chave.checked ? textos.banco : textos.codigo;

    if (parado || ! pacote.animate) {
      resultado.innerHTML = texto;

      return;
    }

    const origem = pacote.getBoundingClientRect();
    const alvo = parede.getBoundingClientRect();
    const distancia = alvo.left - origem.right;

    resultado.textContent = 'Consultando…';
    botao.disabled = true;

    const animacao = pacote.animate([
      { transform: 'translateX(0)', opacity: 0 },
      { transform: 'translateX(0)', opacity: 1, offset: 0.08 },
      { transform: `translateX(${distancia}px)`, opacity: 1, offset: 0.5, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' },
      { transform: `translateX(${distancia - 28}px)`, opacity: 1, offset: 0.72 },
      { transform: 'translateX(-6px)', opacity: 0 },
    ], { duration: 1200, easing: 'cubic-bezier(0.5, 0, 0.75, 0)' });

    setTimeout(() => {
      parede.classList.remove('bateu');
      void parede.offsetWidth;
      parede.classList.add('bateu');
    }, 560);

    animacao.finished.then(() => {
      resultado.innerHTML = texto;
      botao.disabled = false;
    });
  });
}

/* ---------------------------------------------------------------------------
   A marca: luminância relativa e contraste do WCAG. O texto do botão é o que
   mais contrasta com a cor; o destaque sobre o branco escurece até 4,5:1.
   --------------------------------------------------------------------------- */
const canal = (v) => {
  const c = v / 255;

  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
};

const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
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
  const nome = figura.querySelector('[data-mini-nome]');
  const inicial = figura.querySelector('[data-mini-inicial]');
  const livre = figura.querySelector('[data-cor-livre]');
  const cores = [...figura.querySelectorAll('input[name="cor"]')];

  const aplicar = (cor) => {
    if (! /^#[0-9a-f]{6}$/i.test(cor)) {
      return;
    }

    const branco = contraste(cor, '#ffffff');
    const preto = contraste(cor, '#18181b');
    const texto = branco >= preto ? '#ffffff' : '#18181b';

    // Escurece em passos de 4% até o destaque passar de 4,5:1 sobre o branco.
    let destaque = cor;

    for (let fator = 1; contraste(destaque, '#ffffff') < 4.5 && fator > 0; fator -= 0.04) {
      destaque = hexDe(rgb(cor).map((v) => v * fator));
    }

    mini.style.setProperty('--marca', cor);
    mini.style.setProperty('--marca-texto', texto);
    mini.style.setProperty('--marca-destaque', destaque);

    const botao = `Botão com texto ${texto === '#ffffff' ? 'branco' : 'escuro'}, contraste ${razao(Math.max(branco, preto))}.`;
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

  figura.querySelectorAll('input[name="nome"]').forEach((entrada) => {
    entrada.addEventListener('change', () => {
      nome.textContent = entrada.value;
      inicial.textContent = entrada.value.replace(/^(Revenda|Casa)\s+/, '').charAt(0);
    });
  });

  aplicar(cores.find((entrada) => entrada.checked)?.value ?? '#bb00bf');
}

/* A faixa da stack: tocar, focar ou passar o mouse mostra o papel da peça. */
function stack(lista) {
  const saida = document.querySelector('[data-stack-papel]');
  const botoes = [...lista.querySelectorAll('button')];

  const mostrar = (botao) => {
    const papel = botao.parentElement.querySelector('.papel').textContent;
    const nome = document.createElement('b');

    nome.textContent = botao.textContent.trim();
    saida.replaceChildren(nome, ` - ${papel}`);
    saida.hidden = false;
  };

  botoes.forEach((botao) => {
    botao.setAttribute('aria-pressed', 'false');
    botao.addEventListener('click', () => {
      botoes.forEach((outro) => outro.setAttribute('aria-pressed', String(outro === botao)));
      mostrar(botao);
    });
    botao.addEventListener('focus', () => mostrar(botao));
    botao.addEventListener('pointerenter', (evento) => {
      if (evento.pointerType === 'mouse') {
        mostrar(botao);
      }
    });
  });
}

/* ---------------------------------------------------------------------------
   Ligar tudo. O módulo roda depois do HTML lido.
   --------------------------------------------------------------------------- */
ligarFumaca();
ligarRolagem();
tempoDeLeitura();
document.querySelectorAll('[data-troca]').forEach(trocar);
document.querySelectorAll('[data-digitar]').forEach(digitar);
document.querySelectorAll('[data-contar]').forEach(contar);
document.querySelectorAll('[data-revelar]').forEach((figura) => quandoVisivel(figura, () => figura.classList.add('revelado')));
document.querySelectorAll('[data-esteira]').forEach(esteira);
document.querySelectorAll('[data-qr]').forEach(desenharQr);
document.querySelectorAll('[data-escrever]').forEach(escrever);
trilha();
document.querySelectorAll('[data-venda]').forEach(venda);
document.querySelectorAll('[data-isola]').forEach(isolamento);
document.querySelectorAll('[data-marca]').forEach(marca);
document.querySelectorAll('[data-stack]').forEach(stack);
