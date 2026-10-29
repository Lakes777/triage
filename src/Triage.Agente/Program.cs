using System.Text;

// O console do Windows ainda nasce em página de código antiga (850/1252): sem isto, "versão" sai quebrado
// quando a saída passa por outro programa ou vai para arquivo
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

return Triage.Agente.Comandos.Executar(args, Console.Out, Console.Error);
