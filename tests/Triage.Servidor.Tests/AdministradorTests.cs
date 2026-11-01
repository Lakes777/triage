using Microsoft.Extensions.DependencyInjection;

namespace Triage.Servidor.Tests;

public sealed class AdministradorTests(ServidorDeTeste servidor)
{
    private async Task<(int Codigo, string Saida, string Erros)> Rodar(params string[] args)
    {
        var comando = ComandosDoAdministrador.Interpretar(args);
        Assert.NotNull(comando);
        var saida = new StringWriter();
        var erros = new StringWriter();
        await using var escopo = servidor.Services.CreateAsyncScope();
        var codigo = await comando(escopo.ServiceProvider.GetRequiredService<Inscricoes>(), saida, erros);
        return (codigo, saida.ToString(), erros.ToString());
    }

    [Fact]
    public async Task Gerar_codigo_mostra_um_codigo_que_inscreve()
    {
        var (codigo, saida, _) = await Rodar("gerar-codigo");

        Assert.Equal(0, codigo);
        var gerado = System.Text.RegularExpressions.Regex.Match(saida, @"[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}").Value;
        var resposta = await servidor.Cliente().InscreverAsync(
            new Contratos.Grpc.InscreverPedido { Codigo = gerado, Apelido = "do comando" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(resposta.Chave);
    }

    [Fact]
    public async Task Maquinas_lista_e_revogar_corta()
    {
        var (maquina, _) = await servidor.NovaMaquina("para revogar");

        var antes = await Rodar("maquinas");
        var revogar = await Rodar("revogar", maquina);
        var depois = await Rodar("maquinas");
        var deNovo = await Rodar("revogar", maquina);

        Assert.Contains($"{maquina}  para revogar", antes.Saida);
        Assert.Equal(0, revogar.Codigo);
        Assert.Contains("revogada em", depois.Saida.Split('\n').Single(l => l.StartsWith(maquina, StringComparison.Ordinal)));
        Assert.Equal(1, deNovo.Codigo);
    }

    [Theory]
    [InlineData("revogar", "nao-e-guid")]
    public async Task Revogar_id_invalido_e_erro_de_uso(params string[] args)
    {
        var (codigo, _, erros) = await Rodar(args);

        Assert.Equal(2, codigo);
        Assert.Contains("Id de máquina inválido", erros);
    }

    [Theory]
    [InlineData("revogar")]
    [InlineData("gerar-codigo", "a mais")]
    public async Task Comando_com_argumento_errado_mostra_o_uso(params string[] args)
    {
        var (codigo, _, erros) = await Rodar(args);

        Assert.Equal(2, codigo);
        Assert.Contains("gerar-codigo", erros);
    }

    [Fact]
    public void Sem_comando_o_servidor_sobe()
    {
        Assert.Null(ComandosDoAdministrador.Interpretar([]));
        Assert.Null(ComandosDoAdministrador.Interpretar(["--urls", "http://x"]));
    }
}
