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
   Esteiras: um pulso acende os passos em ordem. "Rodar de novo" repete.
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
   A venda simulada. A etapa vive no data-etapa do telefone; o CSS mostra a
   tela dela, e as animações reiniciam sozinhas quando o atributo muda.
   --------------------------------------------------------------------------- */
function venda(figura) {
  const telefone = figura.querySelector('.telefone');
  const avancar = figura.querySelector('.venda-controles [data-avancar]');
  const recomecar = figura.querySelector('[data-recomecar]');
  const status = figura.querySelector('[data-venda-status]');
  const passos = [...figura.querySelectorAll('[data-passo]')];
  const total = 6;

  const rotulos = ['Vender', 'Ler a placa', 'Cobrar no Pix', 'Simular o pagamento', 'Ver a divisão', 'Pôr a placa no ar', 'Vender de novo'];
  const avisos = [
    'Início do vendedor.',
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

    ir(atual >= total ? 1 : atual + 1);
  });

  recomecar.addEventListener('click', () => {
    ir(0);
    avancar.focus();
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
document.querySelectorAll('[data-venda]').forEach(venda);
document.querySelectorAll('[data-isola]').forEach(isolamento);
document.querySelectorAll('[data-marca]').forEach(marca);
document.querySelectorAll('[data-stack]').forEach(stack);
