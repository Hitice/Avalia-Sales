/*
 * A fumaça da abertura, em WebGL, portada da /proposta do site da Avalia
 * (resources/js/fumaca.js) sem mudança no shader: ruído fractal com o domínio
 * dobrado sobre ele mesmo, uma mancha roxa à esquerda e uma branca à direita,
 * azul-petróleo no meio. Resolução até 2x, descendo de degrau quando a máquina
 * não segura 50 quadros; fora da tela, ou com a aba escondida, o desenho para.
 * Quem decide se ela liga (economia de dados, aparelho fraco, movimento
 * reduzido) é o now.js.
 */
const VERTICE = `
attribute vec2 posicao;
void main() { gl_Position = vec4(posicao, 0.0, 1.0); }
`;

const FRAGMENTO = `
precision mediump float;

uniform vec2 u_tela;
uniform float u_tempo;
uniform float u_entrada;
uniform vec2 u_mouse;

uniform vec3 ROXO;
uniform vec3 BRANCO;
uniform vec3 PETROLEO;

float acaso(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float ruido(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    return mix(mix(acaso(i), acaso(i + vec2(1.0, 0.0)), f.x),
               mix(acaso(i + vec2(0.0, 1.0)), acaso(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fractal(vec2 p) {
    float v = 0.0;
    float a = 0.5;

    for (int i = 0; i < 5; i++) {
        v += a * ruido(p);
        p = mat2(0.8, -0.6, 0.6, 0.8) * p * 2.03;
        a *= 0.5;
    }

    return v;
}

void main() {
    vec2 uv = gl_FragCoord.xy / u_tela;
    vec2 p = vec2(uv.x * u_tela.x / u_tela.y, uv.y) + u_mouse * 0.07;
    float t = u_tempo * 0.095;

    vec2 q = vec2(fractal(p * 2.2 + vec2(t, -t)), fractal(p * 2.2 + vec2(4.5, -t * 0.7)));
    vec2 r = vec2(fractal(p * 3.0 + q * 2.8 + vec2(t, 6.0)), fractal(p * 2.8 + q * 2.4 - vec2(t, 0.0)));
    float n = fractal(p * 3.8 + r * 3.8);

    // Na entrada, a mancha roxa vem da esquerda e a branca da direita.
    float afasta = (1.0 - u_entrada) * 0.6;
    float roxo = exp(-pow((uv.x - 0.28 + afasta) * 2.4, 2.0) - pow((uv.y - 0.47 + (q.x - 0.5) * 0.42) * 2.6, 2.0));
    float branco = exp(-pow((uv.x - 0.72 - afasta) * 2.4, 2.0) - pow((uv.y - 0.5 + (q.y - 0.5) * 0.42) * 2.6, 2.0));
    float volutas = pow(n, 2.0) * 2.7 + pow(n, 6.0) * 6.0;

    vec3 cor = ROXO * roxo * volutas + BRANCO * branco * volutas;
    cor = mix(cor, PETROLEO * volutas, exp(-pow((uv.x - 0.5) * 6.0, 2.0)) * 0.35);
    cor *= smoothstep(0.0, 0.18, uv.y) * smoothstep(1.0, 0.68, uv.y);

    gl_FragColor = vec4(cor, 1.0);
}
`;

// O roxo da Avalia, um branco lilás e um índigo profundo entre os dois (sem o verde da proposta):
// eles. O site da casa passa as suas em `data-cores` (tres hex: esquerda,
// direita e meio), e a proposta, que nao passa nada, fica como estava.
const CORES = [[0.529, 0.021, 0.644], [0.5, 0.49, 0.62], [0.07, 0.05, 0.24]];

/** Hex sRGB para a cor linear que o shader soma, com o mesmo teto de brilho da proposta. */
function linear(hex) {
    return [1, 3, 5].map((i) => Math.pow(parseInt(hex.slice(i, i + 2), 16) / 255, 2.2) * 0.7);
}

function compilar(gl, tipo, fonte) {
    const sombreador = gl.createShader(tipo);

    gl.shaderSource(sombreador, fonte);
    gl.compileShader(sombreador);

    return gl.getShaderParameter(sombreador, gl.COMPILE_STATUS) ? sombreador : null;
}

/** Liga a fumaca no canvas. Devolve false quando o navegador nao tem WebGL. */
export function iniciar(canvas) {
    const gl = canvas.getContext('webgl', { antialias: false, alpha: false, powerPreference: 'low-power' });

    if (! gl) {
        return false;
    }

    const vertice = compilar(gl, gl.VERTEX_SHADER, VERTICE);
    const fragmento = compilar(gl, gl.FRAGMENT_SHADER, FRAGMENTO);

    if (! vertice || ! fragmento) {
        return false;
    }

    const programa = gl.createProgram();

    gl.attachShader(programa, vertice);
    gl.attachShader(programa, fragmento);
    gl.linkProgram(programa);

    if (! gl.getProgramParameter(programa, gl.LINK_STATUS)) {
        return false;
    }

    gl.useProgram(programa);

    // Dois triangulos cobrindo a tela inteira.
    gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]), gl.STATIC_DRAW);

    const posicao = gl.getAttribLocation(programa, 'posicao');

    gl.enableVertexAttribArray(posicao);
    gl.vertexAttribPointer(posicao, 2, gl.FLOAT, false, 0, 0);

    const local = {
        tela: gl.getUniformLocation(programa, 'u_tela'),
        tempo: gl.getUniformLocation(programa, 'u_tempo'),
        entrada: gl.getUniformLocation(programa, 'u_entrada'),
        mouse: gl.getUniformLocation(programa, 'u_mouse'),
    };

    const cores = canvas.dataset.cores ? canvas.dataset.cores.split(',').map(linear) : CORES;

    ['ROXO', 'BRANCO', 'PETROLEO'].forEach((nome, i) => gl.uniform3fv(gl.getUniformLocation(programa, nome), cores[i]));

    const parado = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const mouse = { x: 0, y: 0, alvoX: 0, alvoY: 0 };
    let visivel = true;
    let quadro = null;
    const inicio = performance.now();

    // Degraus de resolucao, do mais nitido ao mais leve. Sem o pedido do site
    // fica o 1,25x da proposta, e nada desce.
    const degraus = canvas.dataset.resolucao === 'tela' ? [2, 1.5, 1.25, 1] : [1.25];
    let degrau = 0;
    const amostra = { quadros: 0, desde: 0 };

    const medir = () => {
        const escala = Math.min(devicePixelRatio || 1, degraus[degrau]);

        canvas.width = Math.max(1, Math.round(canvas.clientWidth * escala));
        canvas.height = Math.max(1, Math.round(canvas.clientHeight * escala));
        gl.viewport(0, 0, canvas.width, canvas.height);
    };

    const desenhar = (agora) => {
        const segundos = (agora - inicio) / 1000;
        const entrada = parado ? 1 : Math.min(segundos / 3, 1);

        mouse.x += (mouse.alvoX - mouse.x) * 0.04;
        mouse.y += (mouse.alvoY - mouse.y) * 0.04;

        gl.uniform2f(local.tela, canvas.width, canvas.height);
        gl.uniform1f(local.tempo, parado ? 9 : segundos);
        gl.uniform1f(local.entrada, entrada);
        gl.uniform2f(local.mouse, mouse.x, mouse.y);
        gl.drawArrays(gl.TRIANGLES, 0, 6);
    };

    // A cada 40 quadros, a media: abaixo de 50 por segundo, um degrau abaixo.
    // So desce, para nao ficar oscilando entre dois degraus.
    const conferir = (agora) => {
        if (amostra.quadros === 0) {
            amostra.desde = agora;
        }

        if (++amostra.quadros < 40) {
            return;
        }

        const media = (agora - amostra.desde) / (amostra.quadros - 1);

        amostra.quadros = 0;

        if (media > 20 && degrau < degraus.length - 1) {
            degrau++;
            medir();
        }
    };

    const rodar = (agora) => {
        desenhar(agora);
        conferir(agora);
        quadro = visivel && ! document.hidden ? requestAnimationFrame(rodar) : null;
    };

    const retomar = () => {
        if (! parado && quadro === null && visivel && ! document.hidden) {
            // A pausa nao conta como quadro lento.
            amostra.quadros = 0;
            quadro = requestAnimationFrame(rodar);
        }
    };

    new ResizeObserver(() => {
        medir();
        desenhar(performance.now());
    }).observe(canvas);

    new IntersectionObserver((entradas) => {
        visivel = entradas[0].isIntersecting;
        retomar();
    }).observe(canvas);

    document.addEventListener('visibilitychange', retomar);

    window.addEventListener('pointermove', (evento) => {
        mouse.alvoX = evento.clientX / innerWidth - 0.5;
        mouse.alvoY = 0.5 - evento.clientY / innerHeight;
    }, { passive: true });

    medir();
    desenhar(performance.now());
    canvas.closest('[data-hero]')?.classList.add('fumaca-pronta');
    retomar();

    return true;
}
