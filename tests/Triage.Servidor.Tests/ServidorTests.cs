using System.Net;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Triage.Contratos;
using Triage.Contratos.Grpc;

namespace Triage.Servidor.Tests;

public class ServidorTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Saude_responde_200()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/saude", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Versao_por_grpc_devolve_a_versao_do_servidor()
    {
        using var canal = GrpcChannel.ForAddress(fabrica.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = fabrica.Server.CreateHandler() });
        var cliente = new Triagem.TriagemClient(canal);

        var resposta = await cliente.VersaoAsync(new VersaoPedido { VersaoDoAgente = "teste" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(VersaoDoTriage.Atual, resposta.VersaoDoServidor);
    }
}
