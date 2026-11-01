using Triage.Verificacoes;

namespace Triage.Servidor;

public sealed record Piora(string Item, string De, string Para, string? Detalhe = null);

/// <summary>
/// As regras dos avisos (P19), puras: comparam a revisão nova com o que veio antes. Quem busca o "antes" no banco é
/// o Envios, na mesma transação da gravação.
/// </summary>
public static class Historico
{
    public const string ItemDoDesgaste = "desgaste-ssd";

    /// <summary>
    /// Pontos de desgaste (0-100% da vida útil) entre uma revisão e a seguinte que já são "subindo rápido". Um SSD
    /// comum gasta perto de 1 ponto por ano; 3 de uma semana para outra é escrita anormal ou disco morrendo.
    /// </summary>
    public const int SaltoDeDesgaste = 3;

    // Só estas têm ordem; pulado e "não se aplica" não são piora nem melhora (P18)
    private static readonly Dictionary<string, int> Peso = new() { ["ok"] = 0, ["atencao"] = 1, ["critico"] = 2 };

    public static bool Comparavel(string gravidade) => Peso.ContainsKey(gravidade);

    /// <summary>
    /// Itens que pioraram: ok -> atenção, atenção -> crítico, ok -> crítico. "Anterior" é a última gravidade
    /// comparável de cada item antes desta revisão (um item pulado no meio não apaga a história). Melhora não avisa.
    /// </summary>
    public static IReadOnlyList<Piora> Pioras(IReadOnlyDictionary<string, string> anterior,
        IReadOnlyDictionary<string, string> atual) =>
        atual.Where(a => Comparavel(a.Value)
                && anterior.TryGetValue(a.Key, out var antes) && Comparavel(antes)
                && Peso[a.Value] > Peso[antes])
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => new Piora(a.Key, anterior[a.Key], a.Value))
            .ToList();

    /// <summary>
    /// Discos cujo desgaste subiu <see cref="SaltoDeDesgaste"/> pontos ou mais desde a revisão anterior. O mesmo
    /// disco é o mesmo modelo com o mesmo tamanho (a foto não tem número de série, P15). Foto que não lê = nada.
    /// </summary>
    public static IReadOnlyList<Piora> DesgasteSubindo(string? fotoAnterior, string fotoAtual)
    {
        var antes = Discos(fotoAnterior);
        return Discos(fotoAtual)
            .Where(d => d.Desgaste is not null)
            .Select(d => (Disco: d, Antes: antes.FirstOrDefault(a => a.Modelo == d.Modelo && a.Tamanho == d.Tamanho)?.Desgaste))
            .Where(x => x.Antes is { } a && x.Disco.Desgaste!.Value - a >= SaltoDeDesgaste)
            .Select(x => new Piora(ItemDoDesgaste, $"{x.Antes}%", $"{x.Disco.Desgaste}%",
                x.Disco.Modelo.Length > 150 ? x.Disco.Modelo[..150] : x.Disco.Modelo))
            .ToList();
    }

    private static IReadOnlyList<Disco> Discos(string? foto)
    {
        if (foto is null)
            return [];
        try
        {
            return Gravacao.DeJson(foto).Discos?.Valor ?? [];
        }
        catch (Exception erro) when (erro is System.Text.Json.JsonException or NotSupportedException or ArgumentException)
        {
            return [];
        }
    }
}
