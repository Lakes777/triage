using System.Text.Json.Serialization;

namespace Triage.Verificacoes;

/// <summary>
/// Tudo que as verificações precisam do PC, lido de uma vez. Vira JSON no `triage gravar`, então não pode ter
/// nome de usuário, de arquivo ou de pasta (P15): só medidas.
/// </summary>
public sealed record FotoDoSistema(
    DateTimeOffset LidaEm,
    bool Administrador,
    Lido<IReadOnlyList<Volume>> Volumes,
    Lido<IReadOnlyList<Disco>> Discos,
    Lido<Pasta> TempDoUsuario,
    Lido<Pasta> TempDoWindows,
    Lido<Pasta> CacheDoWindowsUpdate,
    Lido<ConteudoDaLixeira> Lixeira,
    Lido<DateTimeOffset> UltimaInicializacao,
    // Lido com valor nulo = o PC não tem bateria (desktop)
    Lido<Bateria?> Bateria);

/// <summary>Uma leitura que pode ter falhado: ou tem o valor, ou diz por que não tem.</summary>
public sealed record Lido<T>(T? Valor, MotivoDoPulo? Pulo = null)
{
    [JsonIgnore]
    public bool FoiLido => Pulo is null;
}

public static class Lido
{
    public static Lido<T> Ok<T>(T valor) => new(valor);
    public static Lido<T> Pulado<T>(MotivoDoPulo motivo) => new(default, motivo);
}

/// <summary>Uma unidade fixa, como "C:".</summary>
public sealed record Volume(string Letra, long Total, long Livre, bool DoSistema);

public enum TipoDeDisco { Desconhecido, Hd, Ssd, Nvme }

public enum SaudeDoDisco { Desconhecida, Saudavel, Alerta, Ruim }

/// <summary>Disco físico. O desgaste (0-100% da vida útil prevista) só sai com admin.</summary>
public sealed record Disco(string Modelo, TipoDeDisco Tipo, long Tamanho, SaudeDoDisco Saude,
    int? Desgaste, MotivoDoPulo? DesgasteNaoLido = null);

public sealed record Pasta(long Bytes, int Arquivos);

public sealed record ConteudoDaLixeira(long Bytes, long Itens);

/// <summary>Capacidades em mWh, somando todas as baterias.</summary>
public sealed record Bateria(int CapacidadeDeProjeto, int CapacidadeAtual, int? Ciclos);
