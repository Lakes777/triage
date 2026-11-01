using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.ServiceProcess;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>As correções no Windows de verdade. Sem teste no Linux: o CI roda as três num runner descartável.</summary>
[SupportedOSPlatform("windows")]
public sealed class AmbienteDoWindows(TimeProvider relogio) : IAmbienteDeCorrecao
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly TimeSpan PrazoDoServico = TimeSpan.FromSeconds(60);

    public bool Administrador { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public DateTimeOffset Agora => relogio.GetLocalNow();
    public string TempDoUsuario => Path.GetTempPath();
    public string TempDoWindows => Path.Combine(Windows, "Temp");
    public string CacheDoWindowsUpdate => Path.Combine(Windows, "SoftwareDistribution", "Download");

    public ConteudoDaLixeira LerLixeira() => LeitorDoWindows.LerLixeira();

    public void EsvaziarLixeira()
    {
        // Sem a janela de confirmação do Explorer: a confirmação já foi feita no terminal
        const uint semConfirmacao = 0x1, semProgresso = 0x2, semSom = 0x4;
        var resultado = SHEmptyRecycleBinW(IntPtr.Zero, null, semConfirmacao | semProgresso | semSom);
        // E_UNEXPECTED (0x8000FFFF) = a lixeira já estava vazia
        if (resultado != 0 && resultado != unchecked((int)0x8000FFFF))
            Marshal.ThrowExceptionForHR(resultado);
    }

    public bool WindowsUpdateRodando()
    {
        using var servico = new ServiceController("wuauserv");
        return servico.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
    }

    public void PararWindowsUpdate()
    {
        using var servico = new ServiceController("wuauserv");
        // O wuauserv para sozinho quando fica ocioso: em StopPending, o Stop() lançaria; basta esperar
        if (servico.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
            servico.Stop();
        servico.WaitForStatus(ServiceControllerStatus.Stopped, PrazoDoServico);
    }

    public void IniciarWindowsUpdate()
    {
        using var servico = new ServiceController("wuauserv");
        if (servico.Status is ServiceControllerStatus.StopPending)
            servico.WaitForStatus(ServiceControllerStatus.Stopped, PrazoDoServico);
        if (servico.Status is ServiceControllerStatus.Stopped)
            servico.Start();
        servico.WaitForStatus(ServiceControllerStatus.Running, PrazoDoServico);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHEmptyRecycleBinW(IntPtr janela, string? raiz, uint opcoes);
}
