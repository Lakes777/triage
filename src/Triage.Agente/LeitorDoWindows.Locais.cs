using System.Runtime.Versioning;
using Triage.Verificacoes;

namespace Triage.Agente;

[SupportedOSPlatform("windows")]
public sealed partial class LeitorDoWindows
{
    // ESQUELETO da fase 3 (grupo "locais")
    private static Lido<IReadOnlyList<ProgramaNaInicializacao>> LerInicializacao(bool admin) => Lido.Pulado<IReadOnlyList<ProgramaNaInicializacao>>(MotivoDoPulo.NaoSuportado);
    private static Lido<IReadOnlyList<DispositivoComErro>> LerDriversComErro(bool admin) => Lido.Pulado<IReadOnlyList<DispositivoComErro>>(MotivoDoPulo.NaoSuportado);
    private static Lido<EventosDoSistema> LerEventos(bool admin, TimeProvider relogio) => Lido.Pulado<EventosDoSistema>(MotivoDoPulo.NaoSuportado);
    private static Lido<Antivirus> LerAntivirus(bool admin) => Lido.Pulado<Antivirus>(MotivoDoPulo.NaoSuportado);
    private static Lido<Firewall> LerFirewall(bool admin) => Lido.Pulado<Firewall>(MotivoDoPulo.NaoSuportado);
}
