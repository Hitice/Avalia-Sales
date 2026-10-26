using AvaliaSales.Domain.Equipe;

namespace AvaliaSales.Domain.Revendedores;

/// <summary>Empresa que compra placas da Avalia e vende com a própria equipe.</summary>
public sealed class Revendedor
{
    public const int NomeMaximo = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Nome { get; private set; }

    /// <summary>
    /// Taxa da Avalia em pontos base. Cada cobrança copia o valor do momento.
    /// Zero por padrão: a revenda paga a tarifa do Pagar.me e a Avalia ganha
    /// nas placas e nos serviços.
    /// </summary>
    public int TaxaAvaliaBps { get; private set; }

    /// <summary>Recebedor do revendedor no Pagar.me (rp_... ou re_...). Sem ele, não há cobrança.</summary>
    public string? RecebedorPagarme { get; private set; }

    /// <summary>CNPJ, só dígitos. Nulo no revendedor criado antes do cadastro pela tela.</summary>
    public string? Cnpj { get; private set; }

    /// <summary>Celular de contato da revenda, só dígitos com DDD. Nulo no revendedor criado antes do cadastro pela tela.</summary>
    public string? Celular { get; private set; }

    /// <summary>Comissão de quem não tem a própria, em pontos base. Mudança vale para venda nova.</summary>
    public int ComissaoPadraoBps { get; private set; } = 2000;

    /// <summary>Preço da renovação sobre o valor da venda, em pontos base (6000 = 60%).</summary>
    public int RenovacaoBps { get; private set; } = 6000;

    /// <summary>A marca do revendedor no app (white label). Nula usa a marca da plataforma.</summary>
    public string? NomeExibido { get; private set; }

    public string? CorPrimaria { get; private set; }
    public string? CorDestaque { get; private set; }

    /// <summary>O cabeçalho do app na cor primária, com o texto que o contraste escolheu; falso deixa o cabeçalho branco.</summary>
    public bool CabecalhoNaCor { get; private set; }
    public DateTimeOffset? MarcaAlteradaEm { get; private set; }

    /// <summary>Quando o contrato de revenda foi assinado. Sem ele, não há pedido de placas.</summary>
    public DateTimeOffset? ContratoAssinadoEm { get; private set; }

    /// <summary>Vai no contrato e no distrato. Nulo no revendedor cadastrado antes de a tela pedir.</summary>
    public Endereco? Endereco { get; private set; }

    /// <summary>O dono pediu o encerramento: o distrato está sendo gerado ou espera assinatura.</summary>
    public DateTimeOffset? EncerramentoPedidoEm { get; private set; }

    /// <summary>
    /// Data efetiva do encerramento, a da assinatura do distrato. Daí em diante
    /// não há venda nem pedido, e o dono só consulta e exporta até <see cref="ExportacaoAte"/>.
    /// </summary>
    public DateTimeOffset? EncerradaEm { get; private set; }

    /// <summary>Prazo para o dono exportar os dados depois do encerramento (cláusula 3.3 do distrato).</summary>
    public const int DiasParaExportar = 30;

    public DateTimeOffset? ExportacaoAte => EncerradaEm?.AddDays(DiasParaExportar);

    /// <summary>A revenda encerrada pediu para voltar; espera a aprovação da operadora.</summary>
    public DateTimeOffset? ReaberturaPedidaEm { get; private set; }

    /// <summary>A última reabertura aprovada. Contrato de antes dela não vale: a revenda assina outro.</summary>
    public DateTimeOffset? ReabertaEm { get; private set; }

    public const int MotivoMaximo = 200;

    /// <summary>
    /// A Avalia suspendeu a revenda: ela não vende nem pede placas até a
    /// reativação. As placas vendidas continuam no ar, porque são do cliente.
    /// </summary>
    public DateTimeOffset? SuspensaEm { get; private set; }

    public string? MotivoDaSuspensao { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// A operadora marcou a revenda como pertencente ao mesmo grupo dela (ADR 0025): só para ela, e só
    /// para os agregados do resultado do grupo, a operadora lê o DRE desta revenda. Decisão explícita
    /// da operadora; nenhuma regra deduz o grupo do CNPJ ou do nome.
    /// </summary>
    public bool DoGrupoDaOperadora { get; private set; }

    public DateTimeOffset? GrupoDefinidoEm { get; private set; }
    public Guid? GrupoDefinidoPor { get; private set; }

    private Revendedor() => Nome = string.Empty;

    public void DefinirDoGrupo(bool doGrupo, Guid? usuarioId, DateTimeOffset agora)
    {
        DoGrupoDaOperadora = doGrupo;
        GrupoDefinidoEm = agora;
        GrupoDefinidoPor = usuarioId;
    }

    public Revendedor(string nome, string? cnpj = null)
    {
        if (cnpj is not null)
        {
            Cnpj = Documentos.Normalizar(cnpj, obrigatorio: true) is { Length: 14 } valido
                ? valido
                : throw new RegraDeNegocioException("Informe o CNPJ da empresa, com 14 números.");
        }

        Nome = string.Empty;
        Renomear(nome);
    }

    /// <summary>
    /// Só a operadora corrige o CNPJ, quando a empresa muda de documento (a revenda Avalia passou para outro
    /// CNPJ). O dono não muda o CNPJ: ele é a parte do contrato.
    /// </summary>
    public void CorrigirCadastro(string nome, string cnpj)
    {
        Renomear(nome);
        Cnpj = Documentos.Normalizar(cnpj, obrigatorio: true) is { Length: 14 } valido
            ? valido
            : throw new RegraDeNegocioException("Informe o CNPJ da empresa, com 14 números.");
    }

    /// <summary>O nome da empresa. O CNPJ não muda: é a parte do contrato.</summary>
    public void Renomear(string nome)
    {
        nome = (nome ?? string.Empty).Trim();
        Nome = nome.Length is > 0 and <= NomeMaximo
            ? nome
            : throw new RegraDeNegocioException($"O nome da empresa precisa ter de 1 a {NomeMaximo} caracteres.");
    }

    /// <summary>Vale para as cobranças novas; a venda já feita guarda a taxa dela.</summary>
    public void DefinirTaxaAvalia(int bps) =>
        TaxaAvaliaBps = bps is >= 0 and < Pagamentos.DivisaoDaCobranca.Base
            ? bps
            : throw new RegraDeNegocioException("A taxa da Avalia vai de 0% a 99,99%.");

    public void DefinirRecebedorPagarme(string recebedor)
    {
        recebedor = (recebedor ?? string.Empty).Trim();
        RecebedorPagarme = (recebedor.StartsWith("rp_", StringComparison.Ordinal) || recebedor.StartsWith("re_", StringComparison.Ordinal))
            && recebedor.Length is > 3 and <= 64 && recebedor[3..].All(char.IsAsciiLetterOrDigit)
            ? recebedor
            : throw new RegraDeNegocioException("O recebedor do Pagar.me começa com rp_ ou re_.");
    }

    public void DefinirCelular(string? celular) =>
        Celular = Telefones.Normalizar(celular, obrigatorio: false) ?? throw new RegraDeNegocioException("Informe o celular com DDD.");

    /// <summary>Destinos novos por dia (placa vendida, destino trocado, link criado ou alterado) antes de pedir liberação à operadora.</summary>
    public const int DestinosPorDia = 10;

    public const int LiberacaoMaxima = 500;

    /// <summary>Quantos destinos a operadora liberou além do padrão no dia de <see cref="DestinosLiberadosEm"/>.</summary>
    public int DestinosLiberados { get; private set; }

    /// <summary>O dia (Brasília) da liberação extra; ela vale só nesse dia.</summary>
    public DateOnly? DestinosLiberadosEm { get; private set; }

    public int LimiteDeDestinosEm(DateOnly dia) => DestinosPorDia + (DestinosLiberadosEm == dia ? DestinosLiberados : 0);

    /// <summary>Soma à liberação do dia; liberação de um dia que já passou recomeça do zero.</summary>
    public void LiberarDestinos(int quantidade, DateOnly hoje)
    {
        if (quantidade is < 1 or > LiberacaoMaxima)
        {
            throw new RegraDeNegocioException($"A liberação vai de 1 a {LiberacaoMaxima} cadastros.");
        }

        DestinosLiberados = (DestinosLiberadosEm == hoje ? DestinosLiberados : 0) + quantidade;
        DestinosLiberadosEm = hoje;
    }

    public void DefinirComissaoPadrao(int bps) => ComissaoPadraoBps = Comissao.Conferir(bps);

    public void DefinirRenovacao(int bps) =>
        RenovacaoBps = bps is >= 1_000 and <= 10_000 ? bps : throw new RegraDeNegocioException("A renovação vai de 10% a 100% do valor da venda.");

    public const int NomeExibidoMaximo = 40;

    /// <summary>Contraste mínimo de texto (WCAG 2.2, 1.4.3) e de componente, como o anel de foco (1.4.11).</summary>
    public const double ContrasteDeTexto = 4.5;

    public const double ContrasteDeComponente = 3.0;

    /// <summary>
    /// A primária vira fundo de botão com texto por cima; o destaque vira o anel
    /// de foco sobre o fundo claro. Cor que não dá contraste é recusada aqui,
    /// mesmo que a tela já tenha conferido.
    /// </summary>
    public void DefinirMarca(string? nomeExibido, string corPrimaria, string corDestaque, DateTimeOffset agora, bool cabecalhoNaCor = false)
    {
        var nome = string.IsNullOrWhiteSpace(nomeExibido) ? null : nomeExibido.Trim();
        if (nome is { Length: > NomeExibidoMaximo })
        {
            throw new RegraDeNegocioException($"O nome exibido tem até {NomeExibidoMaximo} caracteres.");
        }

        var primaria = Contraste.Normalizar(corPrimaria);
        var destaque = Contraste.Normalizar(corDestaque);
        if (Contraste.Razao(primaria, Contraste.TextoSobre(primaria)) < ContrasteDeTexto)
        {
            throw new RegraDeNegocioException("A cor primária não dá contraste com texto. Escolha uma mais escura ou mais clara.");
        }

        if (Contraste.Razao(destaque, Contraste.Branco) < ContrasteDeComponente)
        {
            throw new RegraDeNegocioException("A cor de destaque some no fundo branco. Escolha uma com mais contraste.");
        }

        NomeExibido = nome;
        CorPrimaria = primaria;
        CorDestaque = destaque;
        CabecalhoNaCor = cabecalhoNaCor;
        MarcaAlteradaEm = agora;
    }

    public void RemoverMarca(DateTimeOffset agora)
    {
        NomeExibido = null;
        CorPrimaria = null;
        CorDestaque = null;
        CabecalhoNaCor = false;
        MarcaAlteradaEm = agora;
    }

    /// <summary>Muda a versão da marca, para o navegador buscar o logo novo.</summary>
    public void TocarMarca(DateTimeOffset agora) => MarcaAlteradaEm = agora;

    /// <summary>Pela D4Sign, ou pela operadora quando o contrato foi assinado em papel.</summary>
    public void RegistrarContratoAssinado(DateTimeOffset quando) => ContratoAssinadoEm ??= quando;

    public void Suspender(string? motivo, DateTimeOffset agora)
    {
        if (EncerradaEm is not null)
        {
            throw new RegraDeNegocioException("A conta desta revenda está encerrada.");
        }

        var texto = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        MotivoDaSuspensao = texto is null || texto.Length <= MotivoMaximo ? texto : throw new RegraDeNegocioException($"O motivo tem até {MotivoMaximo} caracteres.");
        SuspensaEm ??= agora;
    }

    public void Reativar()
    {
        SuspensaEm = null;
        MotivoDaSuspensao = null;
    }

    /// <summary>A trava da venda e do pedido de placas.</summary>
    public void ExigirAtiva()
    {
        if (SuspensaEm is not null)
        {
            throw new RegraDeNegocioException("A revenda está suspensa pela Avalia. Fale com o suporte.");
        }
    }

    public void DefinirEndereco(Endereco endereco) => Endereco = endereco;

    public void PedirEncerramento(DateTimeOffset agora)
    {
        if (EncerradaEm is not null)
        {
            throw new RegraDeNegocioException("A conta já está encerrada.");
        }

        if (EncerramentoPedidoEm is not null)
        {
            throw new RegraDeNegocioException("O encerramento já foi pedido. Assine o distrato pelo e-mail da D4Sign.");
        }

        if (Endereco is null)
        {
            throw new RegraDeNegocioException("Complete o endereço da empresa em Meus dados antes de encerrar a conta.");
        }

        EncerramentoPedidoEm = agora;
    }

    public void DesistirDoEncerramento()
    {
        if (EncerradaEm is not null)
        {
            throw new RegraDeNegocioException("A conta já está encerrada.");
        }

        EncerramentoPedidoEm = null;
    }

    public void PedirReabertura(DateTimeOffset agora)
    {
        if (EncerradaEm is null)
        {
            throw new RegraDeNegocioException("A conta não está encerrada.");
        }

        if (ReaberturaPedidaEm is not null)
        {
            throw new RegraDeNegocioException("A reabertura já foi pedida. O suporte da Avalia responde pelo chamado.");
        }

        ReaberturaPedidaEm = agora;
    }

    public void RecusarReabertura()
    {
        if (ReaberturaPedidaEm is null)
        {
            throw new RegraDeNegocioException("Não há pedido de reabertura.");
        }

        ReaberturaPedidaEm = null;
    }

    /// <summary>
    /// Aprovada pela operadora: a conta volta a operar e assina um contrato novo
    /// antes do próximo pedido de placas. O que o distrato fez fica: os clientes
    /// transferidos continuam com a Avalia e o estoque baixado não volta.
    /// </summary>
    public void Reabrir(DateTimeOffset agora)
    {
        if (EncerradaEm is null)
        {
            throw new RegraDeNegocioException("A conta não está encerrada.");
        }

        EncerradaEm = null;
        EncerramentoPedidoEm = null;
        ReaberturaPedidaEm = null;
        ContratoAssinadoEm = null;
        ReabertaEm = agora;
    }

    /// <summary>Com o distrato assinado. Encerrar de novo não muda a data.</summary>
    public void Encerrar(DateTimeOffset quando)
    {
        EncerramentoPedidoEm ??= quando;
        EncerradaEm ??= quando;
    }
}
