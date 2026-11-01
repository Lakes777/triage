using Google.Protobuf;
using Triage.Contratos.Grpc;

namespace Triage.Agente;

/// <summary>
/// As revisões esperando envio, em %LOCALAPPDATA%\Triage\fila, uma por arquivo com o nome do id. Guarda o pedido
/// serializado (os bytes do protobuf), não a revisão para remontar: o servidor compara o SHA-256 do pedido, e um
/// pedido remontado poderia sair diferente (nota da fase 6 no SPEC).
/// </summary>
public sealed class FilaDeEnvios(string pastaDoTriage)
{
    /// <summary>Um PC sem rede por meses não enche o disco: além disso, as mais antigas saem.</summary>
    public const int Maximo = 50;

    public string Pasta { get; } = Path.Combine(pastaDoTriage, "fila");

    public void Guardar(EnviarRevisaoPedido pedido)
    {
        Directory.CreateDirectory(Pasta);
        var arquivo = Arquivo(pedido.RevisaoId);
        // Grava num temporário e renomeia: o PC desligando no meio não deixa meio arquivo na fila
        var temporario = arquivo + ".gravando";
        File.WriteAllBytes(temporario, pedido.ToByteArray());
        File.Move(temporario, arquivo, overwrite: true);
        foreach (var antiga in Pendentes().SkipLast(Maximo))
            Tirar(antiga);
        // Um .gravando que sobrou de uma queda de energia nunca vira revisão: some depois de uma hora
        foreach (var sobra in Directory.GetFiles(Pasta, "*.gravando")
                     .Where(a => File.GetLastWriteTimeUtc(a) < DateTime.UtcNow.AddHours(-1)))
            Tirar(sobra);
    }

    /// <summary>
    /// Trava da fila: duas execuções ao mesmo tempo (a agendada ao ligar e um duplo clique) não enviam juntas.
    /// Nulo = outra execução está enviando agora.
    /// </summary>
    public IDisposable? Travar()
    {
        Directory.CreateDirectory(Pasta);
        try
        {
            return new FileStream(Path.Combine(Pasta, ".trava"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Os arquivos da fila, do mais antigo para o mais novo (o id é UUID v7, que começa pelo horário).</summary>
    public IReadOnlyList<string> Pendentes() => !Directory.Exists(Pasta)
        ? []
        : Directory.GetFiles(Pasta, "*.revisao").Order(StringComparer.Ordinal).ToList();

    public static EnviarRevisaoPedido Ler(string arquivo) => EnviarRevisaoPedido.Parser.ParseFrom(File.ReadAllBytes(arquivo));

    /// <summary>Já sumiu (outra execução tirou) ou está em uso: tanto faz, o servidor não grava duas vezes.</summary>
    public void Tirar(string arquivo)
    {
        try
        {
            File.Delete(arquivo);
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Esvaziar()
    {
        if (Directory.Exists(Pasta))
            Directory.Delete(Pasta, recursive: true);
    }

    private string Arquivo(string revisaoId) => Path.Combine(Pasta, $"{Guid.Parse(revisaoId):N}.revisao");
}
