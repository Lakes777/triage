using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Triage.Agente;

public enum Decisao
{
    Aplicada,
    Recusada,
    NadaAFazer,
    Erro,
}

/// <summary>Uma linha do correcoes.jsonl: o que foi feito, quando e quanto liberou (P4). Sem caminho de arquivo (P15).</summary>
public sealed record EntradaDoRegistro(
    DateTimeOffset Quando, string Item, Decisao Decisao, long BytesLiberados, int Apagados, int Pulados,
    string Resumo, string Versao);

/// <summary>
/// Um JSON por linha, só acrescentando: dá para ler com qualquer editor, e uma linha cortada (PC desligou no meio)
/// não estraga as anteriores.
/// </summary>
public sealed partial class RegistroDeCorrecoes(string arquivo)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Arquivo => arquivo;

    public void Anotar(EntradaDoRegistro entrada)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arquivo))!);
        var semCaminho = entrada with { Resumo = SemCaminho(entrada.Resumo) };
        File.AppendAllText(arquivo, JsonSerializer.Serialize(semCaminho, Json) + "\n");
    }

    /// <summary>
    /// Rede de segurança da P15: o resumo vem de textos do plano, que podem citar uma pasta (TEMP fora do padrão).
    /// Qualquer coisa com cara de caminho vira "(caminho)".
    /// </summary>
    public static string SemCaminho(string texto) => Caminho().Replace(texto, "(caminho)");

    // C:\..., \\servidor\... ou /..., até o próximo espaço seguido de "(" ou ")" ou o fim; caminho pode ter espaço
    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|\\\\|(?<![\w.])/)[^()\r\n]*?(?=\)|$|\s\()", RegexOptions.Multiline)]
    private static partial Regex Caminho();

    public IReadOnlyList<EntradaDoRegistro> Ler() => !File.Exists(arquivo)
        ? []
        : File.ReadLines(arquivo)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<EntradaDoRegistro>(l, Json)!)
            .ToList();
}
