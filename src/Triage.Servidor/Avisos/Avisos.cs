using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Triage.Servidor.Banco;
using Triage.Verificacoes;

namespace Triage.Servidor.Avisos;

/// <summary>Para onde vão os avisos. O de verdade é o Telegram; nos testes, um falso que guarda as mensagens.</summary>
public interface IMensageiro
{
    Task Enviar(string texto, CancellationToken cancelar);
}

/// <summary>
/// O bot do Sidekick, como o Vigil (P19): Telegram:Token e Telegram:Chat. O token nunca vai para o log: a mensagem
/// de erro não leva a URL, e o HttpClient "telegram" é registrado sem os loggers (que escrevem a URL de cada pedido).
/// </summary>
public sealed partial class MensageiroDoTelegram(HttpClient http, string token, string chat) : IMensageiro
{
    /// <summary>O formato do BotFather: número, dois pontos, 35 caracteres. Outra coisa nem é usada.</summary>
    public static bool TokenValido(string? token) => token is not null && FormatoDoToken().IsMatch(token);

    [GeneratedRegex(@"^\d{5,}:[A-Za-z0-9_-]{30,}$")]
    private static partial Regex FormatoDoToken();

    public async Task Enviar(string texto, CancellationToken cancelar)
    {
        using var resposta = await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage",
            new { chat_id = chat, text = texto, disable_web_page_preview = true }, cancelar);
        if (!resposta.IsSuccessStatusCode)
        {
            // O corpo explica ("chat not found") e não tem o token
            var corpo = await resposta.Content.ReadAsStringAsync(CancellationToken.None);
            throw new HttpRequestException(
                $"O Telegram respondeu {(int)resposta.StatusCode}: {corpo[..Math.Min(corpo.Length, 200)]}");
        }
    }
}

/// <summary>
/// Manda os avisos pendentes. Cada um é "pego" por um UPDATE condicional (enviado_em IS NULL): duas rodadas ao
/// mesmo tempo não mandam o mesmo aviso duas vezes. Se o Telegram falhar, a marca é desfeita e a próxima rodada
/// tenta de novo (como no Vigil).
/// Limites aceitos (como no Vigil): se o processo morrer entre marcar e mandar (OOM, kill -9), o aviso se perde; se o
/// Telegram recebeu mas a resposta estourou o prazo, ele sai duas vezes.
/// </summary>
public sealed class EnviadorDeAvisos(TriageContexto banco, IMensageiro mensageiro, TimeProvider relogio,
    IConfiguration configuracao, ILogger<EnviadorDeAvisos> log, PontosDeTeste pontos)
{
    /// <summary>Aviso mais velho que isso não vai mais: ligar o Telegram depois não despeja semanas de mensagens.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromDays(7);

    private static readonly Dictionary<string, string> Titulos =
        Motor.Padrao().Verificacoes.ToDictionary(v => v.Id, v => v.Titulo);

    public async Task<int> EnviarPendentes(CancellationToken cancelar = default)
    {
        var agora = relogio.GetUtcNow();
        var pendentes = await (
                from a in banco.Avisos
                join m in banco.Maquinas on a.MaquinaId equals m.Id
                join r in banco.Revisoes on new { a.MaquinaId, Id = a.RevisaoId } equals new { r.MaquinaId, r.Id }
                where a.EnviadoEm == null && a.CriadoEm > agora - Validade && !m.Demo
                orderby a.CriadoEm
                select new { Aviso = a, m.Apelido, r.FeitaEm })
            .AsNoTracking().Take(50).ToListAsync(cancelar);
        await pontos.DepoisDeListarAvisos();

        var enviados = 0;
        foreach (var p in pendentes)
        {
            // Desligando: para entre um aviso e outro, nunca no meio de um (o meio deixaria a marca sem a mensagem)
            if (cancelar.IsCancellationRequested)
                break;
            var peguei = await banco.Avisos.Where(a => a.Id == p.Aviso.Id && a.EnviadoEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EnviadoEm, agora), cancelar) == 1;
            if (!peguei)
                continue; // outra rodada pegou primeiro
            try
            {
                // Sem o cancelar: o prazo do HttpClient (10 s) é menor que a espera do desligamento (30 s)
                await mensageiro.Enviar(Texto(p.Apelido, p.Aviso, p.FeitaEm, configuracao["Painel:Endereco"]),
                    CancellationToken.None);
                enviados++;
            }
            catch (Exception erro)
            {
                // Qualquer falha desfaz a marca: um aviso marcado e não mandado se perderia em silêncio
                log.LogWarning("Aviso {Aviso} não foi pelo Telegram ({Erro}); tenta na próxima rodada", p.Aviso.Id, erro.Message);
                await banco.Avisos.Where(a => a.Id == p.Aviso.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.EnviadoEm, (DateTimeOffset?)null)
                    .SetProperty(a => a.Tentativas, a => a.Tentativas + 1), CancellationToken.None);
                // Telegram fora: os outros dariam o mesmo (e cada um esperaria até 10 s)
                break;
            }
        }
        return enviados;
    }

    /// <summary>"Triage: notebook da sala piorou em Espaço livre: OK -> Atenção (revisão de 01/11 14:30)."</summary>
    public static string Texto(string apelido, Aviso aviso, DateTimeOffset feitaEm, string? painel)
    {
        var quando = TimeZoneInfo.ConvertTime(feitaEm, Fuso).ToString("dd/MM HH:mm", Textos.Cultura);
        var texto = aviso.Item == Historico.ItemDoDesgaste
            ? $"Triage: {apelido}: o desgaste do SSD {aviso.Detalhe} subiu de {aviso.De} para {aviso.Para} (revisão de {quando})."
            : $"Triage: {apelido} piorou em {Titulos.GetValueOrDefault(aviso.Item, aviso.Item)}: " +
              $"{Rotulo(aviso.De)} -> {Rotulo(aviso.Para)} (revisão de {quando}).";
        return string.IsNullOrWhiteSpace(painel) ? texto : $"{texto} {painel.TrimEnd('/')}/maquinas/{aviso.MaquinaId}";
    }

    private static string Rotulo(string gravidade) => gravidade switch
    {
        "ok" => "OK",
        "atencao" => "Atenção",
        "critico" => "Crítico",
        _ => gravidade,
    };

    // A VM roda em UTC; quem lê o aviso está em Brasília. Imagem sem tzdata: -03:00 fixo (sem horário de verão desde 2019)
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.TryFindSystemTimeZoneById("America/Sao_Paulo", out var fuso)
        ? fuso
        : TimeZoneInfo.CreateCustomTimeZone("Brasília", TimeSpan.FromHours(-3), "Brasília", "Brasília");
}

/// <summary>Roda o enviador de tempos em tempos. Sem Telegram configurado, nem liga: os avisos ficam no banco.</summary>
public sealed class RodadaDeAvisos(IServiceScopeFactory escopos, IConfiguration configuracao, ILogger<RodadaDeAvisos> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        var intervalo = TimeSpan.FromSeconds(configuracao.GetValue("Avisos:IntervaloEmSegundos", 30));
        using var relogio = new PeriodicTimer(intervalo);
        do
        {
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                await escopo.ServiceProvider.GetRequiredService<EnviadorDeAvisos>().EnviarPendentes(parar);
            }
            catch (Exception erro) when (!parar.IsCancellationRequested)
            {
                // Um problema nos avisos nunca derruba o servidor: vai para o log e tenta na próxima
                log.LogError(erro, "Falha ao mandar os avisos");
            }
        }
        while (await relogio.WaitForNextTickAsync(parar));
    }
}
