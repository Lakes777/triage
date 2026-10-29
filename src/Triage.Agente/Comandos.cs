using Triage.Contratos;

namespace Triage.Agente;

/// <summary>Entrada do terminal. Cada fase acrescenta o seu comando aqui (revisar, corrigir, inscrever...).</summary>
public static class Comandos
{
    public const int Sucesso = 0;
    public const int UsoErrado = 2;

    private const string Ajuda = """
        Triage: revisa este PC e diz o que fazer.

        Uso:
          triage --versao    mostra a versão
          triage --ajuda     mostra esta ajuda
        """;

    public static int Executar(string[] args, TextWriter saida, TextWriter erros)
    {
        switch (args)
        {
            case ["--versao"]:
                saida.WriteLine($"triage {VersaoDoTriage.Atual}");
                return Sucesso;
            case [] or ["--ajuda"]:
                saida.WriteLine(Ajuda);
                return Sucesso;
            default:
                erros.WriteLine($"Comando desconhecido: {string.Join(' ', args)}");
                erros.WriteLine(Ajuda);
                return UsoErrado;
        }
    }
}
