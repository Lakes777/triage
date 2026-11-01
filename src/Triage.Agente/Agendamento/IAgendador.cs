using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace Triage.Agente;

/// <summary>O Agendador de Tarefas atrás de uma interface: os testes conferem o XML sem Windows.</summary>
public interface IAgendador
{
    void Criar(string nome, string xml);

    /// <summary>Devolve falso se a tarefa não existia.</summary>
    bool Apagar(string nome);
}

/// <summary>Pelo schtasks.exe, que vem em todo Windows e não pede admin para tarefas do próprio usuário.</summary>
[SupportedOSPlatform("windows")]
public sealed class AgendadorDoWindows : IAgendador
{
    public void Criar(string nome, string xml)
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"triage-tarefa-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(arquivo, xml, Encoding.Unicode);
            var (codigo, texto) = Schtasks("/Create", "/TN", nome, "/XML", arquivo, "/F");
            if (codigo != 0)
                throw new InvalidOperationException($"o Agendador de Tarefas recusou ({codigo}): {texto}");
        }
        finally
        {
            File.Delete(arquivo);
        }
    }

    public bool Apagar(string nome)
    {
        if (Schtasks("/Query", "/TN", nome).Codigo != 0)
            return false;
        var (codigo, texto) = Schtasks("/Delete", "/TN", nome, "/F");
        if (codigo != 0)
            throw new InvalidOperationException($"o Agendador de Tarefas recusou ({codigo}): {texto}");
        return true;
    }

    private static (int Codigo, string Texto) Schtasks(params string[] argumentos)
    {
        var inicio = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in argumentos)
            inicio.ArgumentList.Add(a);
        using var processo = Process.Start(inicio)!;
        var saida = processo.StandardOutput.ReadToEndAsync();
        var erro = processo.StandardError.ReadToEnd();
        processo.WaitForExit();
        return (processo.ExitCode, (erro + saida.Result).Trim());
    }
}
