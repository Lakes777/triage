using System.Text.Json;
using Triage.Contratos;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// Entrada do terminal. Cada fase acrescenta o seu comando aqui (corrigir, inscrever...).
/// <paramref name="leitorDoSistema"/> devolve nulo fora do Windows; <paramref name="pastaDoTriage"/> é onde mora
/// o limites.json (%LOCALAPPDATA%\Triage no Windows).
/// </summary>
public sealed class Comandos(
    TextReader entrada, TextWriter saida, TextWriter erros,
    Func<ILeitorDoSistema?> leitorDoSistema, string pastaDoTriage)
{
    public const int Sucesso = 0;
    public const int Falha = 1;
    public const int UsoErrado = 2;

    private const string Ajuda = """
        Triage: revisa este PC e diz o que fazer.

        Uso:
          triage                            revisa e espera Enter (para o duplo clique)
          triage revisar                    revisa este PC
          triage revisar --leitura <arq>    revisa uma leitura gravada, em vez deste PC
          triage gravar <arq>               grava a leitura deste PC em JSON (só medidas, sem nomes)
          triage --versao                   mostra a versão
          triage --ajuda                    mostra esta ajuda
        """;

    public int Executar(string[] args)
    {
        if (args is [])
        {
            // Duplo clique: sem esperar, a janela fecharia antes de dar para ler (P11), até se der erro
            var codigo = Protegido(() => Revisar(leitorDoSistema()));
            saida.WriteLine();
            saida.WriteLine("Aperte Enter para fechar.");
            entrada.ReadLine();
            return codigo;
        }
        return Protegido(() => ExecutarComando(args));
    }

    private int Protegido(Func<int> comando)
    {
        try
        {
            return comando();
        }
        catch (Exception erro)
        {
            erros.WriteLine($"Erro: {erro.Message}");
            return Falha;
        }
    }

    private int ExecutarComando(string[] args)
    {
        switch (args)
        {
            case ["revisar"]:
                return Revisar(leitorDoSistema());
            case ["revisar", "--leitura", var arquivo]:
                return Revisar(new LeitorGravado(arquivo));
            case ["gravar", var arquivo]:
                return Gravar(leitorDoSistema(), arquivo);
            case ["--versao"]:
                saida.WriteLine($"triage {VersaoDoTriage.Atual}");
                return Sucesso;
            case ["--ajuda"]:
                saida.WriteLine(Ajuda);
                return Sucesso;
            default:
                erros.WriteLine($"Comando desconhecido: {string.Join(' ', args)}");
                erros.WriteLine(Ajuda);
                return UsoErrado;
        }
    }

    private int Revisar(ILeitorDoSistema? leitor)
    {
        if (leitor is null)
            return SoNoWindows();
        Limites limites;
        try
        {
            limites = CarregarLimites();
        }
        catch (JsonException erro)
        {
            erros.WriteLine($"O arquivo {ArquivoDeLimites} tem um erro: {erro.Message}");
            return Falha;
        }
        var revisao = Motor.Padrao().Revisar(leitor.Ler(), limites);
        SaidaEmTexto.Escrever(revisao, saida);
        return Sucesso;
    }

    private int Gravar(ILeitorDoSistema? leitor, string arquivo)
    {
        if (leitor is null)
            return SoNoWindows();
        File.WriteAllText(arquivo, Gravacao.ParaJson(leitor.Ler()));
        saida.WriteLine($"Leitura gravada em {arquivo} (só medidas: sem nome de usuário, de arquivo ou de pasta).");
        return Sucesso;
    }

    private string ArquivoDeLimites => Path.Combine(pastaDoTriage, "limites.json");

    private Limites CarregarLimites() =>
        File.Exists(ArquivoDeLimites) ? Limites.DeJson(File.ReadAllText(ArquivoDeLimites)) : Limites.Padrao;

    private int SoNoWindows()
    {
        erros.WriteLine("Ler este PC só funciona no Windows. Fora dele: triage revisar --leitura <arquivo.json>");
        return Falha;
    }
}
