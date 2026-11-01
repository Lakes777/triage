using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Triage.Servidor;

/// <summary>Códigos de inscrição e chaves de máquina (P10). Nada disso é guardado em claro: só o SHA-256.</summary>
public static class Regras
{
    public const int TamanhoMaximoDoApelido = 40;
    public static readonly TimeSpan ValidadeDoCodigo = TimeSpan.FromHours(24);

    // Base32 de Crockford: sem I, L, O e U, que se confundem com 1, 0 e V quando alguém dita ou copia à mão
    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int TamanhoDoCodigo = 12; // 60 bits: com o limite de tentativas por IP, impossível de adivinhar

    /// <summary>Um código novo, já no formato de mostrar: "7K2M-9QXA-4TRW".</summary>
    public static string NovoCodigo()
    {
        var c = new string(RandomNumberGenerator.GetItems<char>(Alfabeto, TamanhoDoCodigo));
        return $"{c[..4]}-{c[4..8]}-{c[8..]}";
    }

    /// <summary>Aceita minúsculas, sem traços e com espaços; nulo se não tem cara de código (nem vai ao banco).</summary>
    public static string? NormalizarCodigo(string? digitado)
    {
        var limpo = new string((digitado ?? "").Where(ch => ch is not ('-' or ' ')).ToArray()).ToUpperInvariant();
        return limpo.Length == TamanhoDoCodigo && limpo.All(Alfabeto.Contains) ? limpo : null;
    }

    /// <summary>A chave da máquina: 32 bytes aleatórios em base64url (43 caracteres).</summary>
    public static string NovaChave() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string texto) => SHA256.HashData(Encoding.UTF8.GetBytes(texto));

    /// <summary>Compara em tempo constante: um "==" para no primeiro byte diferente e o tempo revelaria a chave.</summary>
    public static bool MesmaChave(string recebida, byte[] hashGuardado) =>
        CryptographicOperations.FixedTimeEquals(Hash(recebida), hashGuardado);

    /// <summary>
    /// O apelido sem espaços nas pontas; nulo se vazio, longo demais, com caractere de controle ou invisível de
    /// formatação (inversão de direção U+202E, largura zero), que bagunçariam o terminal e o painel.
    /// </summary>
    public static string? NormalizarApelido(string? apelido)
    {
        var limpo = (apelido ?? "").Trim();
        return limpo.Length is > 0 and <= TamanhoMaximoDoApelido
            && !limpo.Any(ch => char.IsControl(ch) || char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.Format)
            ? limpo : null;
    }
}
