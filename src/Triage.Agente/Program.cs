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

return new Comandos(Console.In, Console.Out, Console.Error, LeitorDoSistema, pastaDoTriage).Executar(args);
