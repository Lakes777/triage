using System.Net;
using System.Text.RegularExpressions;
using Triage.Contratos;
using Triage.Contratos.Grpc;
using Triage.Verificacoes;

namespace Triage.Servidor.Tests;

public sealed class ServidorTests(ServidorDeTeste servidor)
{
    private static CancellationToken Cancelar => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Saude_responde_200_com_o_banco()
    {
        var resposta = await servidor.CreateClient().GetAsync("/saude", Cancelar);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", await resposta.Content.ReadAsStringAsync(Cancelar));
    }

    [Fact]
    public async Task Versao_por_grpc_devolve_a_versao_do_servidor()
    {
        var resposta = await servidor.Cliente().VersaoAsync(new VersaoPedido { VersaoDoAgente = "teste" },
            cancellationToken: Cancelar);

        Assert.Equal(VersaoDoTriage.Atual, resposta.VersaoDoServidor);
    }

    [Fact]
    public void Contrato_nao_tem_campo_para_caminho_usuario_ou_arquivo()
    {
        // P15: se alguém acrescentar um campo assim no .proto, este teste quebra
        var proibidos = new Regex("caminho|pasta|usuario|arquivo|path|user|file|folder|dir|nome|name|serial",
            RegexOptions.IgnoreCase);
        var campos = TriageReflection.Descriptor.MessageTypes.SelectMany(m => m.Fields.InDeclarationOrder())
            .Select(f => f.Name).ToList();

        Assert.NotEmpty(campos);
        Assert.DoesNotContain(campos, proibidos.IsMatch);
    }

    [Fact]
    public void Foto_de_um_PC_de_verdade_nao_tem_caminho_nem_usuario()
    {
        // A foto_json vem do "triage gravar": a leitura gravada do notebook é o exemplo real (P15)
        var foto = Gravacao.ParaJson(Gravacao.DeJson(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "dados", "notebook-sem-admin.json"))));

        Assert.DoesNotMatch(@"[A-Za-z]:\\\\|\\\\Users\\\\|/home/|Lakes|andre", foto);
    }
}
