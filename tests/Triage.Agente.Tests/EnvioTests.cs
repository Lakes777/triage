using Google.Protobuf;
using Grpc.Core;
using Triage.Agente;
using Triage.Contratos.Grpc;
using Triage.Verificacoes;
using Gravidade = Triage.Contratos.Grpc.Gravidade;
using MotivoDoPulo = Triage.Contratos.Grpc.MotivoDoPulo;

namespace Triage.Agente.Tests;

public sealed class EnvioTests : IDisposable
{
    private static readonly string Notebook = Path.Combine(AppContext.BaseDirectory, "dados", "notebook-sem-admin.json");
    private readonly string pasta = Directory.CreateTempSubdirectory("triage-envio-").FullName;
    private readonly ServidorFalso servidor = new();
    private static readonly Inscricao Inscricao = new("https://triage.exemplo/", "0193f0c4-0000-7000-8000-000000000001", "pc");

    public void Dispose() => Directory.Delete(pasta, recursive: true);

    private FilaDeEnvios Fila => new(pasta);

    private static EnviarRevisaoPedido Pedido(DateTimeOffset? quando = null)
    {
        var foto = new LeitorGravado(Notebook).Ler();
        var revisao = Motor.Padrao().Revisar(foto, Limites.Padrao) with { FeitaEm = quando ?? DateTimeOffset.Now };
        return PedidoDeEnvio.Montar(foto, revisao);
    }

    private ResultadoDoEnvio Esvaziar() =>
        new Enviador(Fila, servidor, Inscricao, "a-chave").EsvaziarFila(TestContext.Current.CancellationToken).Result;

    [Fact]
    public void Pedido_leva_a_foto_sem_nomes_e_a_gravidade_de_cada_item()
    {
        var foto = new LeitorGravado(Notebook).Ler();
        var revisao = Motor.Padrao().Revisar(foto, Limites.Padrao);

        var pedido = PedidoDeEnvio.Montar(foto, revisao);

        Assert.Equal(Gravacao.ParaJson(foto), pedido.FotoJson);
        Assert.Equal(revisao.Resultados.Count, pedido.Resultados.Count);
        var bateria = pedido.Resultados.Single(r => r.Item == "bateria");
        Assert.Equal((Gravidade.Ok, MotivoDoPulo.MotivoNenhum), (bateria.Gravidade, bateria.Motivo));
        Assert.All(pedido.Resultados.Where(r => r.Gravidade == Gravidade.Pulado),
            r => Assert.NotEqual(MotivoDoPulo.MotivoNenhum, r.Motivo));
        Assert.Equal(7, Guid.Parse(pedido.RevisaoId).Version);
        Assert.Equal(revisao.FeitaEm, pedido.FeitaEm.ToDateTimeOffset());
    }

    [Fact]
    public void Fila_guarda_os_bytes_exatos_do_pedido()
    {
        // O servidor compara o SHA-256 do pedido: o que sai da fila tem de ser idêntico ao que entrou
        var pedido = Pedido();

        Fila.Guardar(pedido);

        var arquivo = Assert.Single(Fila.Pendentes());
        Assert.Equal(pedido.ToByteArray(), File.ReadAllBytes(arquivo));
        Assert.Equal(pedido, FilaDeEnvios.Ler(arquivo));
    }

    [Fact]
    public void Fila_sai_na_ordem_em_que_as_revisoes_foram_feitas_e_tem_teto()
    {
        var inicio = DateTimeOffset.Now.AddDays(-100);
        var pedidos = Enumerable.Range(0, FilaDeEnvios.Maximo + 3).Select(i => Pedido(inicio.AddDays(i))).ToList();
        foreach (var p in pedidos.AsEnumerable().Reverse())
            Fila.Guardar(p);

        var ids = Fila.Pendentes().Select(a => FilaDeEnvios.Ler(a).RevisaoId).ToList();

        Assert.Equal(FilaDeEnvios.Maximo, ids.Count);
        Assert.Equal(pedidos.Skip(3).Select(p => p.RevisaoId), ids); // as 3 mais antigas saíram
    }

    [Fact]
    public void Entregue_sai_da_fila_e_vai_com_a_chave_da_maquina()
    {
        Fila.Guardar(Pedido());
        Fila.Guardar(Pedido());

        var r = Esvaziar();

        Assert.Equal(2, r.Entregues);
        Assert.Empty(Fila.Pendentes());
        Assert.All(servidor.Credenciais, c => Assert.Equal((Inscricao.MaquinaId, "a-chave"), c));
    }

    [Fact]
    public void Contraprova_sem_rede_nada_sai_da_fila()
    {
        Fila.Guardar(Pedido());
        Fila.Guardar(Pedido());
        servidor.Padrao = new Envio(RespostaDoEnvio.TentarDepois, "sem rede");

        var r = Esvaziar();

        Assert.Equal((0, 2, RespostaDoEnvio.TentarDepois), (r.Entregues, r.Restantes, r.Parou));
        Assert.Equal(2, Fila.Pendentes().Count);
        Assert.Single(servidor.Credenciais); // parou na primeira: as outras dariam o mesmo
    }

    [Fact]
    public void Rede_que_cai_no_meio_deixa_so_o_que_nao_subiu()
    {
        var primeira = Pedido(DateTimeOffset.Now.AddHours(-2));
        var segunda = Pedido(DateTimeOffset.Now.AddHours(-1));
        Fila.Guardar(primeira);
        Fila.Guardar(segunda);
        servidor.Respostas.Enqueue(new Envio(RespostaDoEnvio.Entregue));
        servidor.Respostas.Enqueue(new Envio(RespostaDoEnvio.TentarDepois));

        Esvaziar();

        Assert.Equal(segunda.RevisaoId, FilaDeEnvios.Ler(Assert.Single(Fila.Pendentes())).RevisaoId);
    }

    [Fact]
    public void Chave_recusada_para_e_guarda_a_fila()
    {
        Fila.Guardar(Pedido());
        servidor.Padrao = new Envio(RespostaDoEnvio.SemPermissao, "revogada");

        var r = Esvaziar();

        Assert.Equal(RespostaDoEnvio.SemPermissao, r.Parou);
        Assert.Single(Fila.Pendentes());
    }

    [Fact]
    public void Recusada_pelo_servidor_sai_da_fila_e_as_outras_seguem()
    {
        Fila.Guardar(Pedido(DateTimeOffset.Now.AddHours(-1)));
        Fila.Guardar(Pedido());
        servidor.Respostas.Enqueue(new Envio(RespostaDoEnvio.Recusada, "Já existe uma revisão com este id e outro conteúdo."));

        var r = Esvaziar();

        Assert.Equal(1, r.Entregues);
        Assert.Equal(["Já existe uma revisão com este id e outro conteúdo."], r.Recusadas);
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public void Arquivo_estragado_na_fila_nao_trava_as_outras()
    {
        Fila.Guardar(Pedido());
        File.WriteAllBytes(Path.Combine(Fila.Pasta, "00000000000000000000000000000000.revisao"), [0xFF, 0xFF, 0xFF]);

        var r = Esvaziar();

        Assert.Equal(1, r.Entregues);
        Assert.Equal(["arquivo da fila estragado"], r.Recusadas);
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public void Outra_execucao_enviando_ao_mesmo_tempo_nao_mexe_na_fila()
    {
        // A agendada ao ligar o PC e um duplo clique: só uma envia
        Fila.Guardar(Pedido());
        using var outra = Fila.Travar();
        Assert.NotNull(outra);

        var r = Esvaziar();

        Assert.Equal((RespostaDoEnvio.TentarDepois, "outra execução do Triage está enviando agora"), (r.Parou, r.Detalhe));
        Assert.Empty(servidor.Credenciais);
        Assert.Single(Fila.Pendentes());
    }

    [Fact]
    public void Revisao_que_some_da_fila_no_meio_nao_interrompe_as_outras()
    {
        // Achado do revisor: o teto da fila de outra execução apagava um arquivo já listado
        var pedidos = Enumerable.Range(0, 3).Select(i => Pedido(DateTimeOffset.Now.AddHours(-3 + i))).ToList();
        pedidos.ForEach(Fila.Guardar);
        var ultima = Fila.Pendentes()[^1];
        servidor.AoEnviar = () => File.Delete(ultima);

        var r = Esvaziar();

        Assert.Equal(2, r.Entregues);
        Assert.Null(r.Parou);
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public void Tirar_o_que_ja_saiu_nao_da_erro()
    {
        Fila.Tirar(Path.Combine(pasta, "fila", "nao-existe.revisao"));
    }

    [Theory]
    [InlineData(StatusCode.OK, RespostaDoEnvio.Entregue)]
    [InlineData(StatusCode.InvalidArgument, RespostaDoEnvio.Recusada)]
    [InlineData(StatusCode.AlreadyExists, RespostaDoEnvio.Recusada)]
    [InlineData(StatusCode.Unauthenticated, RespostaDoEnvio.SemPermissao)]
    [InlineData(StatusCode.PermissionDenied, RespostaDoEnvio.SemPermissao)]
    [InlineData(StatusCode.Unavailable, RespostaDoEnvio.TentarDepois)]
    [InlineData(StatusCode.DeadlineExceeded, RespostaDoEnvio.TentarDepois)]
    [InlineData(StatusCode.ResourceExhausted, RespostaDoEnvio.TentarDepois)]
    [InlineData(StatusCode.Internal, RespostaDoEnvio.TentarDepois)]
    [InlineData(StatusCode.Unknown, RespostaDoEnvio.TentarDepois)]
    public void Cada_codigo_do_gRPC_tem_um_destino(StatusCode codigo, RespostaDoEnvio esperado)
    {
        Assert.Equal(esperado, ServidorGrpc.Classificar(codigo));
    }

    [Fact]
    public void Arquivo_da_chave_nunca_tem_a_chave_em_claro()
    {
        var arquivos = new ArquivosDaInscricao(pasta, new CofreFalso());

        arquivos.Gravar(Inscricao, "chave-secreta-da-maquina");

        Assert.DoesNotContain("chave-secreta", File.ReadAllText(Path.Combine(pasta, "chave")));
        Assert.DoesNotContain("chave-secreta", File.ReadAllText(Path.Combine(pasta, "inscricao.json")));
        Assert.Equal((Inscricao, "chave-secreta-da-maquina"), arquivos.Ler());
    }
}
