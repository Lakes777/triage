using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Triage.Contratos.Grpc;
using Triage.Servidor.Banco;

// Um Postgres e um servidor para todos os testes do projeto (subir o contêiner leva alguns segundos)
[assembly: AssemblyFixture(typeof(Triage.Servidor.Tests.ServidorDeTeste))]

namespace Triage.Servidor.Tests;

/// <summary>O servidor inteiro, em memória, ligado a um Postgres de verdade num contêiner (Testcontainers).</summary>
public sealed class ServidorDeTeste : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer banco = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await banco.StartAsync();
        // Sobe o servidor (e roda as migrações) antes do primeiro teste
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Ambiente próprio: sem ele, o servidor leria o appsettings.Development.json (o banco do dia a dia)
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:Banco", banco.GetConnectionString());
        // Os testes fazem muitas inscrições seguidas; o limite em si é testado com outro valor
        builder.UseSetting("Limites:InscricoesPorMinuto", "100000");
    }

    /// <summary>Outro servidor no mesmo banco, com um ajuste (limite menor, gancho de teste...).</summary>
    public WebApplicationFactory<Program> Com(Action<IServiceCollection>? servicos = null,
        Dictionary<string, string>? configuracao = null) => WithWebHostBuilder(b =>
    {
        foreach (var (chave, valor) in configuracao ?? [])
            b.UseSetting(chave, valor);
        if (servicos is not null)
            b.ConfigureTestServices(servicos);
    });

    public static Triagem.TriagemClient Cliente(WebApplicationFactory<Program> fabrica) =>
        new(GrpcChannel.ForAddress(fabrica.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = fabrica.Server.CreateHandler(), MaxSendMessageSize = null }));

    public Triagem.TriagemClient Cliente() => Cliente(this);

    public static Metadata Credenciais(string maquina, string chave) =>
        new() { { TriagemServico.CabecalhoDaMaquina, maquina }, { TriagemServico.CabecalhoDaChave, chave } };

    /// <summary>Roda algo com as regras e o banco direto, como os comandos do administrador fazem.</summary>
    public async Task<T> NoBanco<T>(Func<Inscricoes, TriageContexto, Task<T>> acao)
    {
        await using var escopo = Services.CreateAsyncScope();
        return await acao(escopo.ServiceProvider.GetRequiredService<Inscricoes>(),
            escopo.ServiceProvider.GetRequiredService<TriageContexto>());
    }

    /// <summary>Gera um código e inscreve uma máquina nova: (id, chave).</summary>
    public async Task<(string Maquina, string Chave)> NovaMaquina(string apelido = "PC de teste")
    {
        var codigo = await NoBanco((i, _) => i.GerarCodigo());
        var resposta = await Cliente().InscreverAsync(new InscreverPedido { Codigo = codigo, Apelido = apelido });
        return (resposta.MaquinaId, resposta.Chave);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await banco.DisposeAsync();
    }
}
