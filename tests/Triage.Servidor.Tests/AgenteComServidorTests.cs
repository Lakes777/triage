using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Triage.Agente;
using Triage.Contratos.Grpc;
using Triage.Verificacoes;
using Gravidade = Triage.Contratos.Grpc.Gravidade;

namespace Triage.Servidor.Tests;

/// <summary>
/// O agente inteiro (comandos, fila, cliente gRPC) contra o servidor inteiro (gRPC, regras, Postgres): os critérios
/// da fase 7 provados de ponta a ponta, sem nada falso no meio além da rede e do cofre.
/// </summary>
public sealed class AgenteComServidorTests(ServidorDeTeste servidor) : IDisposable
{
    private static readonly string Notebook = Path.Combine(AppContext.BaseDirectory, "dados", "notebook-sem-admin.json");
    private static CancellationToken Cancelar => TestContext.Current.CancellationToken;
    private readonly string pasta = Directory.CreateTempSubdirectory("triage-ponta-a-ponta-").FullName;
    private bool redeFora;

    public void Dispose() => Directory.Delete(pasta, recursive: true);

    private sealed class Cofre : ICofre
    {
        public byte[] Proteger(string segredo) => System.Text.Encoding.UTF8.GetBytes(segredo).Reverse().ToArray();
        public string Abrir(byte[] protegido) => System.Text.Encoding.UTF8.GetString(protegido.Reverse().ToArray());
    }

    /// <summary>A "rede": o TestServer quando está de pé, ou um transporte que falha como cabo desligado.</summary>
    private sealed class RedeQueCai(HttpMessageHandler servidor, Func<bool> fora) : DelegatingHandler(servidor)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelar) =>
            fora() ? throw new HttpRequestException("Nenhuma conexão pôde ser feita (cabo desligado)") : base.SendAsync(pedido, cancelar);
    }

    private (int Codigo, string Saida, string Erros) Agente(params string[] args)
    {
        var saida = new StringWriter();
        var erros = new StringWriter();
        var windows = new AcessoAoWindows(
            () => new LeitorGravado(Notebook), () => null, () => null, _ => true, null, TimeProvider.System,
            () => new Cofre(),
            endereco => ServidorGrpc.Conectar(endereco, new RedeQueCai(servidor.Server.CreateHandler(), () => redeFora)));
        var codigo = new Comandos(new StringReader(""), saida, erros, pasta, windows).Executar(args);
        return (codigo, saida.ToString(), erros.ToString());
    }

    private FilaDeEnvios Fila => new(pasta);

    private async Task<Guid> Inscrever()
    {
        var codigo = await servidor.NoBanco((i, _) => i.GerarCodigo(Cancelar));
        var r = Agente("inscrever", "--servidor", "http://localhost", codigo, "ponta", "a", "ponta");
        Assert.True(r.Codigo == 0, r.Erros);
        return Guid.Parse(new ArquivosDaInscricao(pasta, new Cofre()).Ler()!.Value.Inscricao.MaquinaId);
    }

    private Task<int> RevisoesNoServidor(Guid maquina) =>
        servidor.NoBanco((_, b) => b.Revisoes.CountAsync(r => r.MaquinaId == maquina, Cancelar));

    [Fact]
    public async Task Inscrito_o_revisar_chega_ao_banco_com_os_itens()
    {
        var maquina = await Inscrever();

        var r = Agente("revisar", "--sem-abrir");

        Assert.Contains("Enviada(s) ao servidor: 1", r.Saida);
        Assert.Equal(1, await RevisoesNoServidor(maquina));
        var itens = await servidor.NoBanco((_, b) => b.Resultados.CountAsync(x => x.MaquinaId == maquina, Cancelar));
        Assert.Equal(Motor.Padrao().Verificacoes.Count, itens);
        var apelido = await servidor.NoBanco((_, b) => b.Maquinas.Where(m => m.Id == maquina).Select(m => m.Apelido).SingleAsync(Cancelar));
        Assert.Equal("ponta a ponta", apelido);
    }

    [Fact]
    public async Task Sem_rede_a_revisao_fica_na_fila_e_sobe_depois()
    {
        var maquina = await Inscrever();
        redeFora = true;

        var semRede = Agente("revisar", "--sem-abrir");
        var tentativa = Agente("enviar");
        redeFora = false;
        var voltou = Agente("enviar");

        Assert.Equal(0, semRede.Codigo);
        Assert.Contains("Sem conexão com o servidor agora (", semRede.Saida);
        Assert.Contains("1 revisão(ões) na fila", semRede.Saida);
        Assert.Equal(1, tentativa.Codigo);
        Assert.Equal(0, voltou.Codigo);
        Assert.Contains("Enviada(s) ao servidor: 1", voltou.Saida);
        Assert.Equal(1, await RevisoesNoServidor(maquina));
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public async Task Mesma_revisao_enviada_duas_vezes_grava_uma()
    {
        // Como quando a confirmação se perde na volta: o agente não sabe que chegou e manda de novo
        var maquina = await Inscrever();
        redeFora = true;
        Agente("revisar", "--sem-abrir");
        var arquivo = Assert.Single(Fila.Pendentes());
        var bytes = File.ReadAllBytes(arquivo);
        redeFora = false;

        Agente("enviar");
        File.WriteAllBytes(arquivo, bytes);
        var reenvio = Agente("enviar");

        Assert.Equal(0, reenvio.Codigo);
        Assert.Contains("Enviada(s) ao servidor: 1", reenvio.Saida); // para o agente, entregue
        Assert.Equal(1, await RevisoesNoServidor(maquina));
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public async Task Mesmo_id_com_outro_conteudo_e_recusado_e_a_original_fica()
    {
        var maquina = await Inscrever();
        redeFora = true;
        Agente("revisar", "--sem-abrir");
        var arquivo = Assert.Single(Fila.Pendentes());
        var original = FilaDeEnvios.Ler(arquivo);
        redeFora = false;
        Agente("enviar");

        // Um item "ok" que vira "atenção": conteúdo válido, mas diferente do que já foi gravado com este id
        var indice = original.Resultados.ToList().FindIndex(x => x.Gravidade == Gravidade.Ok);
        var mudado = original.Clone();
        mudado.Resultados[indice].Gravidade = Gravidade.Atencao;
        File.WriteAllBytes(arquivo, mudado.ToByteArray());
        var r = Agente("enviar");

        Assert.Contains("Revisão recusada pelo servidor e tirada da fila: Já existe uma revisão com este id e outro conteúdo.", r.Erros);
        Assert.Empty(Fila.Pendentes());
        Assert.Equal(1, await RevisoesNoServidor(maquina));
        var id = Guid.Parse(original.RevisaoId);
        var gravada = await servidor.NoBanco((_, b) => b.Resultados
            .Where(x => x.RevisaoId == id && x.Item == original.Resultados[indice].Item).Select(x => x.Gravidade).SingleAsync(Cancelar));
        // A gravidade no banco é a da revisão original, não a do conteúdo mudado
        var nomes = new Dictionary<Gravidade, string>
        {
            [Gravidade.Ok] = "ok",
            [Gravidade.Atencao] = "atencao",
            [Gravidade.Critico] = "critico",
            [Gravidade.Pulado] = "pulado",
            [Gravidade.NaoSeAplica] = "nao_se_aplica",
        };
        Assert.Equal(nomes[original.Resultados[indice].Gravidade], gravada);
        Assert.NotEqual(nomes[mudado.Resultados[indice].Gravidade], gravada);
    }

    [Fact]
    public async Task Maquina_revogada_avisa_e_guarda_a_fila()
    {
        var maquina = await Inscrever();
        await servidor.NoBanco((i, _) => i.Revogar(maquina, Cancelar));

        var r = Agente("revisar", "--sem-abrir");

        Assert.Equal(0, r.Codigo);
        Assert.Contains("foi revogada pelo administrador", r.Erros);
        Assert.Single(Fila.Pendentes());
        Assert.Equal(0, await RevisoesNoServidor(maquina));
    }

    [Fact]
    public void Codigo_errado_mostra_a_mensagem_do_servidor()
    {
        var r = Agente("inscrever", "--servidor", "http://localhost", "ZZZZ-ZZZZ-ZZZZ", "pc");

        Assert.Equal(1, r.Codigo);
        Assert.Contains("Código inválido, já usado ou vencido", r.Erros);
        Assert.False(new ArquivosDaInscricao(pasta, new Cofre()).Inscrito);
    }
}
