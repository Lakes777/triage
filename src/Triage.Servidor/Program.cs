using Microsoft.EntityFrameworkCore;
using Triage.Servidor;
using Triage.Servidor.Avisos;
using Triage.Servidor.Banco;

// "gerar-codigo", "maquinas", "revogar <id>": comandos do administrador em vez de subir o servidor
var comando = ComandosDoAdministrador.Interpretar(args);
var builder = WebApplication.CreateBuilder(args);

// Melhor nem subir do que subir sem banco e falhar no primeiro pedido
var conexao = builder.Configuration.GetConnectionString("Banco")
    ?? throw new InvalidOperationException(
        "Defina a conexão com o banco em ConnectionStrings:Banco (variável ConnectionStrings__Banco).");
builder.Services.AddDbContext<TriageContexto>(opcoes => opcoes.UseNpgsql(conexao).UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PontosDeTeste>();
builder.Services.AddScoped<Inscricoes>();
builder.Services.AddScoped<Envios>();

builder.Services.AddGrpc(opcoes =>
{
    // Uma revisão real tem uns 10 KB; o limite segura um pedido gigante antes de chegar ao banco
    opcoes.MaxReceiveMessageSize = 256 * 1024;
});

// Inscrição e envio por IP; revisões por máquina por dia (dentro do envio). Atrás do Caddy, o IP real vem do
// X-Forwarded-For (configurado na fase 10, com o deploy)
var limites = LimitesDoServidor.Ler(builder.Configuration);
builder.Services.AddSingleton(limites);
limites.Registrar(builder.Services);

// Avisos pelo Telegram (P19): com token e chat válidos, a rodada manda; sem eles, os avisos só ficam no banco
var token = builder.Configuration["Telegram:Token"];
var chat = builder.Configuration["Telegram:Chat"];
var telegramLigado = MensageiroDoTelegram.TokenValido(token) && !string.IsNullOrWhiteSpace(chat);
if (telegramLigado)
{
    builder.Services.AddScoped<EnviadorDeAvisos>();
    // Sem os loggers do HttpClient: eles escrevem a URL de cada pedido, e a URL do Telegram leva o token
    builder.Services.AddHttpClient("telegram", http => http.Timeout = TimeSpan.FromSeconds(10)).RemoveAllLoggers();
    builder.Services.AddScoped<IMensageiro>(s => new MensageiroDoTelegram(
        s.GetRequiredService<IHttpClientFactory>().CreateClient("telegram"), token!, chat!));
    builder.Services.AddHostedService<RodadaDeAvisos>();
}

builder.Services.AddHealthChecks().AddDbContextCheck<TriageContexto>("banco");

var app = builder.Build();

// Configurou pela metade (token com aspas vindas do .env, chat faltando): avisa sem mostrar o valor
if (!telegramLigado && (!string.IsNullOrWhiteSpace(token) || !string.IsNullOrWhiteSpace(chat)))
    app.Logger.LogWarning("Telegram configurado pela metade ou com token fora do formato do BotFather: os avisos " +
        "ficam só no banco");

// As migrações do EF rodam ao subir (e antes de qualquer comando): o banco nunca fica atrás do código
using (var escopo = app.Services.CreateScope())
    escopo.ServiceProvider.GetRequiredService<TriageContexto>().Database.Migrate();

if (comando is not null)
{
    using var escopo = app.Services.CreateScope();
    return await comando(escopo.ServiceProvider, Console.Out, Console.Error);
}

app.UseRateLimiter();
// Para o Vigil e para o /publicar-vm: de pé e com o banco respondendo
app.MapHealthChecks("/saude");
app.MapGrpcService<TriagemServico>();

app.Run();
return 0;

// Para o WebApplicationFactory dos testes enxergar a classe gerada pelo top-level
public partial class Program;
