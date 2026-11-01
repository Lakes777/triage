using System.Security.Cryptography;
using System.Text.Json;

namespace Triage.Agente;

/// <summary>Onde e como este PC está inscrito. A chave fica à parte, no arquivo "chave", protegida pelo cofre.</summary>
public sealed record Inscricao(string Servidor, string MaquinaId, string Apelido);

/// <summary>
/// A inscrição existe mas não abre: senha da conta redefinida, perfil levado para outro PC (a DPAPI não abre mais) ou
/// inscricao.json estragado. A mensagem já diz a saída, que é a mesma para todos os casos.
/// </summary>
public sealed class InscricaoEstragada(string causa) : Exception(
    $"A inscrição deste PC não abre mais ({causa}). Rode triage desinscrever e peça um código novo ao administrador.");

/// <summary>
/// Os arquivos da inscrição em %LOCALAPPDATA%\Triage: inscricao.json (servidor, id, apelido; sem segredo) e chave
/// (a chave da máquina, protegida com DPAPI).
/// </summary>
public sealed class ArquivosDaInscricao(string pastaDoTriage, ICofre cofre)
{
    private string Dados => Path.Combine(pastaDoTriage, "inscricao.json");
    private string Chave => Path.Combine(pastaDoTriage, "chave");

    public bool Inscrito => File.Exists(Dados) && File.Exists(Chave);

    /// <summary>Só os dados, sem abrir a chave (para dizer "já inscrito como ..." mesmo com a chave estragada).</summary>
    public Inscricao? LerDados()
    {
        if (!File.Exists(Dados))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Inscricao>(File.ReadAllText(Dados), JsonSerializerOptions.Web) is
            { Servidor: not null, MaquinaId: not null, Apelido: not null } dados
                ? dados
                : throw new InscricaoEstragada("inscricao.json incompleto");
        }
        catch (JsonException)
        {
            throw new InscricaoEstragada("inscricao.json estragado");
        }
    }

    public (Inscricao Inscricao, string Chave)? Ler()
    {
        if (!Inscrito)
            return null;
        var dados = LerDados()!;
        try
        {
            return (dados, cofre.Abrir(File.ReadAllBytes(Chave)));
        }
        catch (CryptographicException)
        {
            throw new InscricaoEstragada("o Windows não abre mais a chave guardada");
        }
    }

    public void Gravar(Inscricao inscricao, string chave)
    {
        Directory.CreateDirectory(pastaDoTriage);
        File.WriteAllBytes(Chave, cofre.Proteger(chave));
        File.WriteAllText(Dados, JsonSerializer.Serialize(inscricao, JsonSerializerOptions.Web));
    }

    public void Apagar()
    {
        File.Delete(Chave);
        File.Delete(Dados);
    }
}
