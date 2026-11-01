using System.Diagnostics;
using System.Text;
using Triage.Agente;
using Triage.Verificacoes;

// O console do Windows ainda nasce em página de código antiga (850/1252): sem isto, "versão" sai quebrado
// quando a saída passa por outro programa ou vai para arquivo
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var pastaDoTriage = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Triage");
ILeitorDoSistema? LeitorDoSistema() =>
    OperatingSystem.IsWindows() ? new LeitorDoWindows(TimeProvider.System) : null;

// O navegador padrão abre o .html; fora do Windows (revisar --leitura no WSL) só o caminho é mostrado
static bool AbrirNoNavegador(string arquivo)
{
    if (!OperatingSystem.IsWindows())
        return false;
    try
    {
        using var _ = Process.Start(new ProcessStartInfo(arquivo) { UseShellExecute = true });
        return true;
    }
    catch (Exception erro) when (erro is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
        return false;
    }
}

var windows = new AcessoAoWindows(
    LeitorDoSistema,
    () => OperatingSystem.IsWindows() ? new AmbienteDoWindows(TimeProvider.System) : null,
    () => OperatingSystem.IsWindows() ? new AgendadorDoWindows() : null,
    AbrirNoNavegador,
    Environment.ProcessPath,
    TimeProvider.System,
    () => OperatingSystem.IsWindows() ? new CofreDoWindows() : null,
    endereco => ServidorGrpc.Conectar(endereco));
return new Comandos(Console.In, Console.Out, Console.Error, pastaDoTriage, windows).Executar(args);
