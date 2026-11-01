using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;

namespace Triage.Servidor;

/// <summary>Os limites do servidor (SPEC, seção 8), lidos da configuração "Limites:*".</summary>
public sealed record LimitesDoServidor(int InscricoesPorMinuto, int EnviosPorMinuto, int RevisoesPorDia)
{
    public const string PoliticaDeInscricao = "inscricao";
    public const string PoliticaDeEnvio = "envio";

    public static LimitesDoServidor Ler(IConfiguration configuracao) => new(
        configuracao.GetValue("Limites:InscricoesPorMinuto", 10),
        configuracao.GetValue("Limites:EnviosPorMinuto", 60),
        configuracao.GetValue("Limites:RevisoesPorDia", 50));

    /// <summary>
    /// Políticas presas aos métodos do gRPC por [EnableRateLimiting] (não pelo caminho: o roteamento aceita
    /// "/Inscrever/" com barra no fim, e uma comparação de texto deixava passar sem limite).
    /// </summary>
    public void Registrar(IServiceCollection servicos) => servicos.AddRateLimiter(opcoes =>
    {
        opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests; // o cliente gRPC vê UNAVAILABLE
        opcoes.AddPolicy(PoliticaDeInscricao, http => PorIp(http, InscricoesPorMinuto));
        opcoes.AddPolicy(PoliticaDeEnvio, http => PorIp(http, EnviosPorMinuto));
    });

    private static RateLimitPartition<string> PorIp(HttpContext http, int porMinuto) =>
        RateLimitPartition.GetFixedWindowLimiter(ChaveDoLimite(http.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = porMinuto, Window = TimeSpan.FromMinutes(1) });

    /// <summary>
    /// IPv4 inteiro; IPv6 pelo /64 (cada casa costuma receber um /64 inteiro e trocar o fim do endereço à vontade).
    /// Mesmo critério do Beacon.
    /// </summary>
    public static string ChaveDoLimite(IPAddress? ip)
    {
        if (ip is null)
            return "desconhecido";
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
            return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
