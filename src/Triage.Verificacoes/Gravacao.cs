using System.Text.Json;
using System.Text.Json.Serialization;

namespace Triage.Verificacoes;

/// <summary>Grava e lê a foto do sistema em JSON (`triage gravar`, testes com PCs de verdade).</summary>
public static class Gravacao
{
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string ParaJson(FotoDoSistema foto) => JsonSerializer.Serialize(foto, Opcoes);

    public static FotoDoSistema DeJson(string json) =>
        JsonSerializer.Deserialize<FotoDoSistema>(json, Opcoes)
        ?? throw new JsonException("A leitura gravada está vazia.");
}

public sealed class LeitorGravado(string arquivo) : ILeitorDoSistema
{
    public FotoDoSistema Ler() => Gravacao.DeJson(File.ReadAllText(arquivo));
}
