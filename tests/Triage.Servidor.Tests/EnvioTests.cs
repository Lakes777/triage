using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Triage.Contratos.Grpc;

namespace Triage.Servidor.Tests;

public sealed class EnvioTests(ServidorDeTeste servidor)
{
    private static CancellationToken Cancelar => TestContext.Current.CancellationToken;

    private static EnviarRevisaoPedido Revisao(Guid? id = null, string versao = "0.1.0+teste") => new()
    {
        RevisaoId = (id ?? Guid.CreateVersion7()).ToString(),
        FeitaEm = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddMinutes(-5)),
        VersaoDoAgente = versao,
        Administrador = false,
        FotoJson = """{ "lidaEm": "2026-11-01T12:00:00-03:00", "administrador": false }""",
        Resultados =
        {
            new ResultadoDoItem { Item = "espaco-livre", Gravidade = Gravidade.Ok },
            new ResultadoDoItem { Item = "bateria", Gravidade = Gravidade.Atencao },
            new ResultadoDoItem { Item = "atualizacoes-windows", Gravidade = Gravidade.Pulado, Motivo = MotivoDoPulo.MotivoSemConexao },
        },
    };

    private static async Task<StatusCode> Status(Func<Task> chamada)
    {
        try
        {
            await chamada();
            return StatusCode.OK;
        }
        catch (RpcException e)
        {
            return e.StatusCode;
        }
    }

    private Task<EnviarRevisaoResposta> Enviar((string Maquina, string Chave) m, EnviarRevisaoPedido pedido,
        Triagem.TriagemClient? cliente = null) =>
        (cliente ?? servidor.Cliente()).EnviarRevisaoAsync(pedido, ServidorDeTeste.Credenciais(m.Maquina, m.Chave),
            cancellationToken: Cancelar).ResponseAsync;

    [Fact]
    public async Task Revisao_valida_e_gravada_com_os_resultados()
    {
        var m = await servidor.NovaMaquina();
        var pedido = Revisao();

        var resposta = await Enviar(m, pedido);

        Assert.True(resposta.GravadaAgora);
        var id = Guid.Parse(pedido.RevisaoId);
        var resultados = await servidor.NoBanco((_, b) => b.Resultados.Where(r => r.RevisaoId == id).OrderBy(r => r.Item).ToListAsync(Cancelar));
        Assert.Equal(["atualizacoes-windows", "bateria", "espaco-livre"], resultados.Select(r => r.Item));
        Assert.Equal(["pulado", "atencao", "ok"], resultados.Select(r => r.Gravidade));
        Assert.Equal(["sem_conexao", null, null], resultados.Select(r => r.Motivo));
    }

    [Fact]
    public async Task Mesma_revisao_enviada_duas_vezes_grava_uma()
    {
        // P17 (3): a fila offline (fase 7) reenvia quando não teve a confirmação
        var m = await servidor.NovaMaquina();
        var pedido = Revisao();

        var primeira = await Enviar(m, pedido);
        var segunda = await Enviar(m, pedido.Clone());

        Assert.True(primeira.GravadaAgora);
        Assert.False(segunda.GravadaAgora);
        var id = Guid.Parse(pedido.RevisaoId);
        Assert.Equal(1, await servidor.NoBanco((_, b) => b.Revisoes.CountAsync(r => r.Id == id, Cancelar)));
        Assert.Equal(3, await servidor.NoBanco((_, b) => b.Resultados.CountAsync(r => r.RevisaoId == id, Cancelar)));
    }

    [Fact]
    public async Task Mesma_revisao_ao_mesmo_tempo_grava_uma()
    {
        var m = await servidor.NovaMaquina();
        var pedido = Revisao();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Enviar(m, pedido.Clone())));

        Assert.Single(respostas, r => r.GravadaAgora);
        var id = Guid.Parse(pedido.RevisaoId);
        Assert.Equal(1, await servidor.NoBanco((_, b) => b.Revisoes.CountAsync(r => r.Id == id, Cancelar)));
    }

    [Fact]
    public async Task Mesmo_id_com_outro_conteudo_e_recusado()
    {
        var m = await servidor.NovaMaquina();
        var pedido = Revisao();
        await Enviar(m, pedido);
        var mudado = pedido.Clone();
        mudado.Resultados[0].Gravidade = Gravidade.Critico;

        Assert.Equal(StatusCode.AlreadyExists, await Status(() => Enviar(m, mudado)));
    }

    [Fact]
    public async Task O_mesmo_id_em_duas_maquinas_nao_se_mistura()
    {
        var a = await servidor.NovaMaquina("a");
        var b = await servidor.NovaMaquina("b");
        var id = Guid.CreateVersion7();

        Assert.True((await Enviar(a, Revisao(id))).GravadaAgora);
        Assert.True((await Enviar(b, Revisao(id, versao: "outra"))).GravadaAgora);
    }

    [Fact]
    public async Task Chave_errada_maquina_que_nao_existe_e_sem_credencial_sao_recusadas()
    {
        var m = await servidor.NovaMaquina();
        var chaveErrada = m.Chave[..^1] + (m.Chave[^1] == 'A' ? 'B' : 'A');

        Assert.Equal(StatusCode.Unauthenticated, await Status(() => Enviar((m.Maquina, chaveErrada), Revisao())));
        Assert.Equal(StatusCode.Unauthenticated, await Status(() => Enviar((Guid.NewGuid().ToString(), m.Chave), Revisao())));
        Assert.Equal(StatusCode.Unauthenticated, await Status(() => Enviar(("nao-e-guid", m.Chave), Revisao())));
        Assert.Equal(StatusCode.Unauthenticated, await Status(() =>
            servidor.Cliente().EnviarRevisaoAsync(Revisao(), cancellationToken: Cancelar).ResponseAsync));
        // A chave de uma máquina não serve para outra
        var outra = await servidor.NovaMaquina("outra");
        Assert.Equal(StatusCode.Unauthenticated, await Status(() => Enviar((outra.Maquina, m.Chave), Revisao())));
    }

    [Fact]
    public async Task Maquina_revogada_e_recusada()
    {
        var m = await servidor.NovaMaquina();
        Assert.True(await servidor.NoBanco((i, _) => i.Revogar(Guid.Parse(m.Maquina), Cancelar)));

        Assert.Equal(StatusCode.PermissionDenied, await Status(() => Enviar(m, Revisao())));
        Assert.False(await servidor.NoBanco((i, _) => i.Revogar(Guid.Parse(m.Maquina), Cancelar))); // já estava
    }

    /// <summary>Segura o envio logo depois de conferir a máquina, até o teste soltar.</summary>
    private sealed class EnvioSegurado : PontosDeTeste
    {
        public TaskCompletionSource Chegou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Solta { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task DepoisDeConferirAMaquina()
        {
            Chegou.TrySetResult();
            await Solta.Task;
        }
    }

    [Fact]
    public async Task Revogacao_no_meio_do_envio_espera_e_a_revisao_grava_antes()
    {
        // P17 (2): o envio já conferiu que a máquina está ativa e ainda não gravou; a revogação chega nesse meio.
        // Com a trava (FOR SHARE), a revogação espera o envio terminar; sem ela, revogaria antes da gravação e a
        // revisão de uma máquina revogada entraria depois (contraprova: tirar o FOR SHARE quebra este teste)
        var m = await servidor.NovaMaquina();
        var segurado = new EnvioSegurado();
        await using var fabrica = servidor.Com(s => s.AddSingleton<PontosDeTeste>(segurado));
        var pedido = Revisao();

        var envio = Enviar(m, pedido, ServidorDeTeste.Cliente(fabrica));
        await segurado.Chegou.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancelar);
        var revogacao = Task.Run(() => servidor.NoBanco((i, _) => i.Revogar(Guid.Parse(m.Maquina), Cancelar)), Cancelar);

        var terminouAntes = await Task.WhenAny(revogacao, Task.Delay(1500, Cancelar)) == revogacao;
        segurado.Solta.SetResult();

        Assert.False(terminouAntes, "a revogação passou na frente de um envio que já tinha conferido a máquina");
        Assert.True((await envio).GravadaAgora);
        Assert.True(await revogacao);
        Assert.Equal(StatusCode.PermissionDenied, await Status(() => Enviar(m, Revisao())));
    }

    public static TheoryData<string, Action<EnviarRevisaoPedido>> Invalidos => new()
    {
        { "id que não é UUID", p => p.RevisaoId = "123" },
        { "sem feita_em", p => p.FeitaEm = null },
        { "feita_em no futuro", p => p.FeitaEm = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(3)) },
        { "sem versão", p => p.VersaoDoAgente = "" },
        { "sem itens", p => p.Resultados.Clear() },
        { "item repetido", p => p.Resultados.Add(new ResultadoDoItem { Item = "bateria", Gravidade = Gravidade.Ok }) },
        { "item com cara de caminho", p => p.Resultados[0].Item = @"C:\Users\ana" },
        { "gravidade faltando", p => p.Resultados[0].Gravidade = Gravidade.Desconhecida },
        { "pulado sem motivo", p => p.Resultados[2].Motivo = MotivoDoPulo.MotivoNenhum },
        { "ok com motivo", p => p.Resultados[0].Motivo = MotivoDoPulo.MotivoErro },
        { "foto que não é JSON", p => p.FotoJson = "{ isto não" },
        { "foto que não é objeto", p => p.FotoJson = "[1, 2]" },
        // Achados do revisor: passavam pela validação e o Postgres recusava (o agente veria erro interno e
        // tentaria de novo para sempre)
        { "item com 65 caracteres", p => p.Resultados[0].Item = new string('a', 65) },
        { "feita_em zerado (1970)", p => p.FeitaEm = new Timestamp() },
        { "feita_em com segundos fora da faixa", p => p.FeitaEm = new Timestamp { Seconds = long.MaxValue } },
        { "feita_em com nanos negativos", p => p.FeitaEm = new Timestamp { Seconds = 1_800_000_000, Nanos = -1 } },
        { "foto com \\u0000", p => p.FotoJson = """{ "disco": "SSD\u0000" }""" },
        { "foto com surrogate solto", p => p.FotoJson = """{ "disco": "\ud800" }""" },
        { "foto com número gigante", p => p.FotoJson = """{ "bytes": 1e999999 }""" },
        { "gravidade fora do enum", p => p.Resultados[0].Gravidade = (Gravidade)99 },
        { "motivo fora do enum", p => p.Resultados[2].Motivo = (MotivoDoPulo)99 },
        { "versão com caractere de controle", p => p.VersaoDoAgente = "1.0\u001b[31m" },
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public async Task Revisao_invalida_e_recusada_sem_gravar(string caso, Action<EnviarRevisaoPedido> estragar)
    {
        var m = await servidor.NovaMaquina();
        var pedido = Revisao();
        estragar(pedido);

        Assert.True(StatusCode.InvalidArgument == await Status(() => Enviar(m, pedido)), caso);
        var maquina = Guid.Parse(m.Maquina);
        Assert.Equal(0, await servidor.NoBanco((_, b) => b.Revisoes.CountAsync(r => r.MaquinaId == maquina, Cancelar)));
    }

    [Fact]
    public async Task Teto_diario_por_maquina_conta_so_revisoes_novas()
    {
        await using var fabrica = servidor.Com(configuracao: new() { ["Limites:RevisoesPorDia"] = "2" });
        var cliente = ServidorDeTeste.Cliente(fabrica);
        var m = await servidor.NovaMaquina();
        var primeira = Revisao();

        await Enviar(m, primeira, cliente);
        await Enviar(m, Revisao(), cliente);
        var terceira = await Status(() => Enviar(m, Revisao(), cliente));
        var reenvio = await Enviar(m, primeira.Clone(), cliente);

        Assert.Equal(StatusCode.ResourceExhausted, terceira);
        Assert.False(reenvio.GravadaAgora); // o reenvio da fila nunca é barrado pelo teto
    }

    [Fact]
    public async Task Envio_tem_limite_por_IP_mesmo_sem_chave()
    {
        await using var fabrica = servidor.Com(configuracao: new() { ["Limites:EnviosPorMinuto"] = "2" });
        var cliente = ServidorDeTeste.Cliente(fabrica);
        var semChave = (Guid.NewGuid().ToString(), "chave-qualquer");

        var codigos = new List<StatusCode>();
        for (var i = 0; i < 3; i++)
            codigos.Add(await Status(() => Enviar(semChave, Revisao(), cliente)));

        Assert.Equal([StatusCode.Unauthenticated, StatusCode.Unauthenticated, StatusCode.Unavailable], codigos);
    }

    [Fact]
    public async Task Revisao_gigante_e_barrada_antes_do_banco()
    {
        var m = await servidor.NovaMaquina();
        var pedido = Revisao();
        pedido.FotoJson = $$"""{ "lixo": "{{new string('x', 300 * 1024)}}" }""";

        Assert.Equal(StatusCode.ResourceExhausted, await Status(() => Enviar(m, pedido)));
    }
}
