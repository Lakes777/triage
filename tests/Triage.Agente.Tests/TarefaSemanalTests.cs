using System.Xml.Linq;
using Triage.Agente;

namespace Triage.Agente.Tests;

public sealed class TarefaSemanalTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Theory]
    [InlineData("2026-11-01 15:00", "2026-11-02 12:00")] // domingo -> segunda
    [InlineData("2026-11-02 09:00", "2026-11-02 12:00")] // segunda de manhã -> hoje
    [InlineData("2026-11-02 12:00", "2026-11-09 12:00")] // segunda no horário -> a outra
    [InlineData("2026-11-02 18:00", "2026-11-09 12:00")]
    [InlineData("2026-11-07 23:59", "2026-11-09 12:00")] // sábado
    public void Proxima_vez_e_a_proxima_segunda_ao_meio_dia(string agora, string esperada)
    {
        Assert.Equal(DateTime.Parse(esperada), TarefaSemanal.ProximaVez(DateTime.Parse(agora)));
    }

    [Fact]
    public void Xml_e_valido_e_tem_o_que_o_schtasks_de_linha_de_comando_nao_da()
    {
        var xml = TarefaSemanal.Xml(@"C:\Users\Ana Maria\Downloads\triage.exe", new DateTime(2026, 11, 2, 12, 0, 0));

        Assert.StartsWith("""<?xml version="1.0" encoding="UTF-16"?>""", xml);
        var tarefa = XDocument.Parse(xml).Root!;
        string Valor(string caminho) => caminho.Split('/').Aggregate(tarefa, (e, nome) => e.Element(Ns + nome)!).Value;

        Assert.Equal("true", Valor("Settings/StartWhenAvailable")); // PC desligado na hora: roda ao ligar
        Assert.Equal("false", Valor("Settings/DisallowStartIfOnBatteries"));
        Assert.Equal("InteractiveToken", Valor("Principals/Principal/LogonType")); // sem senha guardada
        Assert.Equal("LeastPrivilege", Valor("Principals/Principal/RunLevel")); // nunca como admin
        Assert.Equal(@"C:\Users\Ana Maria\Downloads\triage.exe", Valor("Actions/Exec/Command")); // espaço no caminho
        Assert.Equal("revisar --sem-abrir", Valor("Actions/Exec/Arguments"));
        Assert.NotNull(tarefa.Element(Ns + "Triggers")!.Descendants(Ns + "Monday").SingleOrDefault());
    }

    [Fact]
    public void Contraprova_a_tarefa_nunca_chama_corrigir()
    {
        Assert.DoesNotContain("corrigir", TarefaSemanal.Argumentos);
        Assert.StartsWith("revisar", TarefaSemanal.Argumentos);
    }
}
