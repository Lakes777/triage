using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Triage.Agente;

/// <summary>Guarda a chave da máquina protegida. Atrás de uma interface para os testes rodarem no Linux.</summary>
public interface ICofre
{
    byte[] Proteger(string segredo);
    string Abrir(byte[] protegido);
}

/// <summary>
/// DPAPI do usuário: o arquivo "chave" só abre neste Windows e nesta conta. Copiado para outro PC (ou lido por
/// outro usuário), não serve para nada.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CofreDoWindows : ICofre
{
    // "Entropia" extra: outro programa do mesmo usuário que chame a DPAPI sem ela não abre o arquivo por engano
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("triage/chave-da-maquina/v1");

    public byte[] Proteger(string segredo) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(segredo), Entropia, DataProtectionScope.CurrentUser);

    public string Abrir(byte[] protegido) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(protegido, Entropia, DataProtectionScope.CurrentUser));
}
