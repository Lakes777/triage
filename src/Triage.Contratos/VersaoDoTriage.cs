using System.Reflection;

namespace Triage.Contratos;

public static class VersaoDoTriage
{
    /// <summary>Versão com o commit curto, ex.: "0.1.0+9c3e94b". Sem git no build, só "0.1.0".</summary>
    public static string Atual { get; } = Encurtar(typeof(VersaoDoTriage).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    /// <summary>O SDK põe o hash inteiro do commit depois do "+"; 7 caracteres bastam para achar no GitHub.</summary>
    public static string Encurtar(string completa)
    {
        var mais = completa.IndexOf('+');
        return mais < 0 || completa.Length <= mais + 8 ? completa : completa[..(mais + 8)];
    }
}
