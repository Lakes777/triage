using System.Runtime.Versioning;
using Triage.Verificacoes;

namespace Triage.Agente;

[SupportedOSPlatform("windows")]
public sealed partial class LeitorDoWindows
{
    // ESQUELETO da fase 3 (grupo "rede")
    private static Lido<AtualizacoesPendentes> LerAtualizacoesPendentes(bool admin) => Lido.Pulado<AtualizacoesPendentes>(MotivoDoPulo.NaoSuportado);
    private static Lido<bool> LerReinicioPendente(bool admin) => Lido.Pulado<bool>(MotivoDoPulo.NaoSuportado);
    private static Lido<DateTimeOffset?> LerUltimaAtualizacaoInstalada(bool admin) => Lido.Pulado<DateTimeOffset?>(MotivoDoPulo.NaoSuportado);
    private static Lido<IReadOnlyList<ProgramaDesatualizado>> LerProgramasDesatualizados(bool admin) => Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(MotivoDoPulo.NaoSuportado);
}
