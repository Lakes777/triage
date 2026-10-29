using Triage.Servidor;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var app = builder.Build();

// Para o Vigil e para o /publicar-vm: só diz se está de pé (o banco entra na fase 6)
app.MapGet("/saude", () => Results.Ok(new { estado = "ok" }));
app.MapGrpcService<TriagemServico>();

app.Run();

// Para o WebApplicationFactory dos testes enxergar a classe gerada pelo top-level
public partial class Program;
