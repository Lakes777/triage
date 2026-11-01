using System.Globalization;
using System.Xml.Linq;

namespace Triage.Agente;

/// <summary>
/// A revisão semanal no Agendador de Tarefas (P11: nada de serviço rodando o tempo todo). O XML dá o que a linha
/// de comando do schtasks não dá: rodar assim que o PC ligar se ele estava desligado na hora marcada, e rodar
/// também na bateria.
/// </summary>
public static class TarefaSemanal
{
    // Na raiz do Agendador: o schtasks /Delete tira a tarefa mas deixaria uma pasta \Triage vazia (visto no notebook)
    public const string Nome = @"\Triage - Revisao semanal";
    public const DayOfWeek Dia = DayOfWeek.Monday;
    public static readonly TimeOnly Hora = new(12, 0);

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>A próxima segunda às 12h a partir de agora (hoje, se ainda não passou do horário).</summary>
    public static DateTime ProximaVez(DateTime agoraLocal)
    {
        var hoje = agoraLocal.Date;
        var dias = ((int)Dia - (int)hoje.DayOfWeek + 7) % 7;
        var vez = hoje.AddDays(dias).Add(Hora.ToTimeSpan());
        return vez > agoraLocal ? vez : vez.AddDays(7);
    }

    /// <summary>
    /// Roda como o próprio usuário, sem admin e só com ele logado (InteractiveToken): não pede senha nem guarda
    /// credencial. Só revisa e grava o relatório: correção nunca roda agendada (P4).
    /// </summary>
    public static string Xml(string executavel, DateTime inicioLocal)
    {
        var tarefa = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "Triage: revisão semanal deste PC. Para tirar: triage desagendar.")),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "CalendarTrigger",
                    new XElement(Ns + "StartBoundary", inicioLocal.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "ScheduleByWeek",
                        new XElement(Ns + "DaysOfWeek", new XElement(Ns + Dia.ToString())),
                        new XElement(Ns + "WeeksInterval", "1")))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "Usuario"),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "LeastPrivilege"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "StartWhenAvailable", "true"),
                new XElement(Ns + "ExecutionTimeLimit", "PT15M"),
                new XElement(Ns + "Priority", "7")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Usuario"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", executavel),
                    new XElement(Ns + "Arguments", Argumentos))));
        // O schtasks lê o arquivo em UTF-16 (quem grava é o AgendadorDoWindows); o XDocument.ToString() não
        // inclui a declaração, então ela vai à mão
        return """<?xml version="1.0" encoding="UTF-16"?>""" + "\n" + tarefa;
    }

    public const string Argumentos = "revisar --sem-abrir";
}
