using System.Globalization;
using System.Text.Json;
using Triage.Contratos;
using Triage.Relatorio;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// Entrada do terminal. Cada fase acrescenta o seu comando aqui (corrigir, inscrever...).
/// <paramref name="leitorDoSistema"/> devolve nulo fora do Windows; <paramref name="pastaDoTriage"/> é onde mora
/// o limites.json e os relatórios (%LOCALAPPDATA%\Triage no Windows). <paramref name="abrirNoNavegador"/> devolve
/// falso quando não deu para abrir (fora do Windows, sem navegador padrão).
/// </summary>
public sealed class Comandos(
    TextReader entrada, TextWriter saida, TextWriter erros,
    Func<ILeitorDoSistema?> leitorDoSistema, string pastaDoTriage, Func<string, bool> abrirNoNavegador)
{
    public const int Sucesso = 0;
    public const int Falha = 1;
    public const int UsoErrado = 2;

    private const string Ajuda = """
        Triage: revisa este PC e diz o que fazer.

        Uso:
          triage                            revisa, abre o relatório e espera Enter (para o duplo clique)
          triage revisar                    revisa este PC e abre o relatório no navegador
          triage revisar --leitura <arq>    revisa uma leitura gravada, em vez deste PC
          triage revisar --sem-abrir        gera o relatório sem abrir o navegador
          triage gravar <arq>               grava a leitura deste PC em JSON (só medidas, sem nomes)
          triage --versao                   mostra a versão
          triage --ajuda                    mostra esta ajuda
        """;

    public int Executar(string[] args)
    {
        if (args is [])
        {
            // Duplo clique: sem esperar, a janela fecharia antes de dar para ler (P11), até se der erro
            var codigo = Protegido(() => Revisar(leitorDoSistema(), abrir: true));
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
            case ["revisar", .. var opcoes]:
                return Revisar(opcoes);
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

    private int Revisar(string[] opcoes)
    {
        string? leitura = null;
        var abrir = true;
        string? problema = null;
        for (var i = 0; i < opcoes.Length && problema is null; i++)
        {
            switch (opcoes[i])
            {
                case "--leitura" or "--sem-abrir" when opcoes[..i].Contains(opcoes[i]):
                    problema = $"Opção repetida: {opcoes[i]}";
                    break;
                case "--leitura" when i + 1 < opcoes.Length && !opcoes[i + 1].StartsWith("--", StringComparison.Ordinal):
                    leitura = opcoes[++i];
                    break;
                case "--leitura":
                    problema = "Falta o arquivo depois de --leitura";
                    break;
                case "--sem-abrir":
                    abrir = false;
                    break;
                default:
                    problema = $"Opção desconhecida: {opcoes[i]}";
                    break;
            }
        }
        if (problema is not null)
        {
            erros.WriteLine(problema);
            erros.WriteLine(Ajuda);
            return UsoErrado;
        }
        return Revisar(leitura is null ? leitorDoSistema() : new LeitorGravado(leitura), abrir);
    }

    private int Revisar(ILeitorDoSistema? leitor, bool abrir)
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

        var relatorio = GuardarRelatorio(revisao);
        saida.WriteLine();
        saida.WriteLine($"Relatório completo: {relatorio}");
        if (abrir && !abrirNoNavegador(relatorio))
            saida.WriteLine("Não deu para abrir o navegador: abra o arquivo acima.");
        return Sucesso;
    }

    private string GuardarRelatorio(Revisao revisao)
    {
        var pasta = Directory.CreateDirectory(Path.Combine(pastaDoTriage, "relatorios")).FullName;
        // A data no nome deixa a ordem alfabética igual à ordem no tempo
        var arquivo = Path.Combine(pasta, $"revisao-{revisao.FeitaEm.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.html");
        File.WriteAllText(arquivo, GeradorDeRelatorio.Gerar(revisao, VersaoDoTriage.Atual));
        ApagarRelatoriosAntigos(pasta, arquivo);
        return arquivo;
    }

    private void ApagarRelatoriosAntigos(string pasta, string atual)
    {
        var falhas = PastaDeRelatorios.ApagarAntigos(pasta, atual);
        if (falhas > 0)
            erros.WriteLine($"Aviso: {falhas} relatório(s) antigo(s) não puderam ser apagados em {pasta}.");
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
