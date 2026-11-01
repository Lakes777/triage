using System.Globalization;
using System.Text.Json;
using Triage.Contratos;
using Triage.Relatorio;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// Tudo o que o agente usa do Windows, trocado por falsos nos testes. As fábricas devolvem nulo fora do Windows.
/// <see cref="AbrirNoNavegador"/> devolve falso quando não deu para abrir; <see cref="Executavel"/> é o caminho do
/// triage.exe que a tarefa semanal vai chamar.
/// </summary>
public sealed record AcessoAoWindows(
    Func<ILeitorDoSistema?> Leitor,
    Func<IAmbienteDeCorrecao?> Correcoes,
    Func<IAgendador?> Agendador,
    Func<string, bool> AbrirNoNavegador,
    string? Executavel,
    TimeProvider Relogio);

/// <summary>
/// Entrada do terminal. Cada fase acrescenta o seu comando aqui (inscrever, enviar...).
/// <paramref name="pastaDoTriage"/> é onde moram o limites.json, os relatórios e o registro das correções
/// (%LOCALAPPDATA%\Triage no Windows).
/// </summary>
public sealed class Comandos(
    TextReader entrada, TextWriter saida, TextWriter erros, string pastaDoTriage, AcessoAoWindows windows)
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
          triage corrigir                   lista as correções
          triage corrigir <item>            faz uma correção (mostra o que vai fazer e pergunta antes)
          triage corrigir --seguras         faz as correções seguras de uma vez (uma pergunta só)
          triage agendar                    revisa toda segunda às 12h, pelo Agendador de Tarefas
          triage desagendar                 tira a revisão semanal
          triage --versao                   mostra a versão
          triage --ajuda                    mostra esta ajuda
        """;

    public int Executar(string[] args)
    {
        if (args is [])
        {
            // Duplo clique: sem esperar, a janela fecharia antes de dar para ler (P11), até se der erro
            var codigo = Protegido(() => Revisar(windows.Leitor(), abrir: true));
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
                return Gravar(windows.Leitor(), arquivo);
            case ["corrigir"]:
                return ListarCorrecoes();
            case ["corrigir", var item]:
                return Corrigir(item);
            case ["agendar"]:
                return Agendar();
            case ["desagendar"]:
                return Desagendar();
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
        return Revisar(leitura is null ? windows.Leitor() : new LeitorGravado(leitura), abrir);
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
        if (abrir && !windows.AbrirNoNavegador(relatorio))
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

    private int ListarCorrecoes()
    {
        saida.WriteLine("Correções (cada uma mostra o que vai fazer e pergunta antes):");
        foreach (var c in Correcoes.Todas)
            saida.WriteLine($"  triage corrigir {c.Item,-22}{c.Titulo}{(c.Segura ? " (segura)" : "")}");
        saida.WriteLine("  triage corrigir --seguras         as marcadas como seguras, com uma pergunta só");
        return Sucesso;
    }

    private int Corrigir(string item)
    {
        var correcao = item == "--seguras" ? null : Correcoes.Achar(item);
        if (item != "--seguras" && correcao is null)
        {
            erros.WriteLine($"Correção desconhecida: {item}");
            erros.WriteLine($"Existem: {string.Join(", ", Correcoes.Todas.Select(c => c.Item))} e --seguras");
            return UsoErrado;
        }
        if (windows.Correcoes() is not { } ambiente)
        {
            erros.WriteLine("Corrigir só funciona no Windows.");
            return Falha;
        }
        var executor = new ExecutorDeCorrecoes(entrada, saida, erros, ambiente,
            new RegistroDeCorrecoes(Path.Combine(pastaDoTriage, "correcoes.jsonl")));
        return correcao is null ? executor.CorrigirSeguras() : executor.Corrigir(correcao);
    }

    private int Agendar()
    {
        if (windows.Agendador() is not { } agendador)
            return SoNoWindows();
        // Pelo "dotnet run" o processo é o dotnet.exe: a tarefa chamaria o programa errado. Outro nome vale
        // ("triage (1).exe" de quem baixou duas vezes). Os dois separadores: os testes rodam no Linux
        var nome = windows.Executavel?.Split('\\', '/')[^1];
        if (windows.Executavel is not { } exe || nome is null
            || !nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || nome.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            erros.WriteLine("Para agendar, rode pelo triage.exe baixado (o agendamento chama este mesmo arquivo), " +
                "não pelo dotnet run.");
            return Falha;
        }
        var proxima = TarefaSemanal.ProximaVez(windows.Relogio.GetLocalNow().DateTime);
        agendador.Criar(TarefaSemanal.Nome, TarefaSemanal.Xml(exe, proxima));
        saida.WriteLine($"Agendado: revisão toda segunda às {TarefaSemanal.Hora:HH\\:mm}, a próxima em " +
            $"{proxima.ToString("dd/MM/yyyy", Textos.Cultura)}. Se o PC estiver desligado, roda assim que ligar.");
        saida.WriteLine($"Ela só revisa e guarda o relatório em {Path.Combine(pastaDoTriage, "relatorios")}: nunca corrige nada.");
        saida.WriteLine($"Usa este arquivo: {exe}. Se mover o triage.exe, rode triage agendar de novo.");
        return Sucesso;
    }

    private int Desagendar()
    {
        if (windows.Agendador() is not { } agendador)
            return SoNoWindows();
        saida.WriteLine(agendador.Apagar(TarefaSemanal.Nome)
            ? "Revisão semanal removida do Agendador de Tarefas."
            : "Não havia revisão semanal agendada.");
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
