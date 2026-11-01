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
    TimeProvider Relogio,
    Func<ICofre?> Cofre,
    Func<Uri, IServidorDoTriage> Conectar);

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

    /// <summary>O servidor de verdade (fase 10). Outro só com --servidor na inscrição.</summary>
    public const string ServidorPadrao = "https://triage.andrelagos.dev";

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
          triage inscrever <código> <apelido>   liga este PC ao servidor (opcional; código do administrador)
          triage enviar                     manda ao servidor as revisões que ficaram na fila
          triage desinscrever               desliga este PC do servidor (apaga a chave e a fila daqui)
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
            case ["inscrever", .. var resto]:
                return Inscrever(resto);
            case ["enviar"]:
                return Enviar();
            case ["desinscrever"]:
                return Desinscrever();
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
        // Uma leitura gravada não é este PC agora: nunca vai para o servidor
        return Revisar(leitura is null ? windows.Leitor() : new LeitorGravado(leitura), abrir, enviar: leitura is null);
    }

    private int Revisar(ILeitorDoSistema? leitor, bool abrir, bool enviar = true)
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
        var foto = leitor.Ler();
        var revisao = Motor.Padrao().Revisar(foto, limites);
        SaidaEmTexto.Escrever(revisao, saida);

        var relatorio = GuardarRelatorio(revisao);
        saida.WriteLine();
        saida.WriteLine($"Relatório completo: {relatorio}");
        if (abrir && !windows.AbrirNoNavegador(relatorio))
            saida.WriteLine("Não deu para abrir o navegador: abra o arquivo acima.");
        if (enviar)
            EnviarAoFimDaRevisao(foto, revisao);
        return Sucesso;
    }

    /// <summary>
    /// Inscrito: põe a revisão na fila e tenta esvaziar. Qualquer problema de envio vira aviso: a revisão local
    /// já está pronta, e o envio é opcional (P25).
    /// </summary>
    private void EnviarAoFimDaRevisao(FotoDoSistema foto, Revisao revisao)
    {
        if (windows.Cofre() is not { } cofre || !new ArquivosDaInscricao(pastaDoTriage, cofre).Inscrito)
            return;
        saida.WriteLine();
        try
        {
            // A chave primeiro: se ela não abre, nem adianta encher a fila
            var (inscricao, chave) = new ArquivosDaInscricao(pastaDoTriage, cofre).Ler()!.Value;
            new FilaDeEnvios(pastaDoTriage).Guardar(PedidoDeEnvio.Montar(foto, revisao));
            // O duplo clique fica parado esperando a rede (até 15 s): melhor dizer o que está acontecendo
            saida.WriteLine("Enviando ao servidor...");
            EsvaziarFila(inscricao, chave);
        }
        catch (InscricaoEstragada erro)
        {
            erros.WriteLine(erro.Message);
        }
        catch (Exception erro)
        {
            saida.WriteLine($"Não deu para enviar agora ({erro.Message}): a revisão ficou na fila (triage enviar).");
        }
    }

    private int Inscrever(string[] resto)
    {
        var servidor = ServidorPadrao;
        var partes = new List<string>();
        for (var i = 0; i < resto.Length; i++)
        {
            if (resto[i] != "--servidor")
                partes.Add(resto[i]);
            else if (i + 1 < resto.Length && !resto[i + 1].StartsWith("--", StringComparison.Ordinal))
                servidor = resto[++i];
            else
            {
                // Sem isto, "triage inscrever CODIGO pc --servidor" virava o apelido "pc --servidor"
                erros.WriteLine("Falta o endereço depois de --servidor.");
                return UsoErrado;
            }
        }
        // O apelido pode ter espaços sem aspas: triage inscrever 7K2M-9QXA-4TRW notebook da sala
        if (partes.Count < 2)
        {
            erros.WriteLine("Uso: triage inscrever <código> <apelido> (o código vem do administrador do servidor)");
            return UsoErrado;
        }
        if (!EnderecoAceito(servidor, out var endereco))
        {
            erros.WriteLine($"Endereço do servidor inválido: {servidor}. Use https:// (http:// só para localhost).");
            return UsoErrado;
        }
        if (windows.Cofre() is not { } cofre)
            return SoNoWindows();
        var arquivos = new ArquivosDaInscricao(pastaDoTriage, cofre);
        if (arquivos.Inscrito)
        {
            // Inscrever de novo cria outra máquina no servidor e deixa a antiga órfã: só de propósito. Lê só os
            // dados (sem abrir a chave): com a chave estragada, a mensagem ainda aponta a saída
            var atual = arquivos.LerDados()!;
            erros.WriteLine($"Este PC já está inscrito como \"{atual.Apelido}\" em {atual.Servidor}. " +
                "Para inscrever de novo, rode triage desinscrever antes.");
            return Falha;
        }

        var apelido = string.Join(' ', partes.Skip(1));
        string maquina, chave;
        try
        {
            (maquina, chave) = windows.Conectar(endereco).Inscrever(partes[0], apelido, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Grpc.Core.RpcException erro)
        {
            erros.WriteLine(erro.StatusCode is Grpc.Core.StatusCode.InvalidArgument
                ? erro.Status.Detail
                : $"Não deu para falar com o servidor em {endereco} ({erro.StatusCode}: {erro.Status.Detail}). Confira a " +
                  "internet e tente de novo. Se a resposta se perdeu no caminho, o código pode já ter sido usado: aí peça outro.");
            return Falha;
        }
        try
        {
            arquivos.Gravar(new Inscricao(endereco.ToString(), maquina, apelido), chave);
        }
        catch (Exception erro)
        {
            // O servidor já criou a máquina e o código foi gasto: sem isto, ficaria só um "Erro:" sem saída
            erros.WriteLine($"O servidor aceitou o código, mas não deu para guardar a chave neste PC ({erro.Message}). " +
                $"O código já foi usado: peça outro, e peça ao administrador para revogar a máquina {maquina}.");
            return Falha;
        }
        saida.WriteLine($"Inscrito como \"{apelido}\". As próximas revisões vão também para {endereco}.");
        saida.WriteLine("Vão só medidas e nomes de programas desatualizados: nunca arquivo, pasta ou nome de usuário.");
        return Sucesso;
    }

    private int Enviar()
    {
        if (windows.Cofre() is not { } cofre)
            return SoNoWindows();
        if (!new ArquivosDaInscricao(pastaDoTriage, cofre).Inscrito)
        {
            erros.WriteLine("Este PC não está inscrito em nenhum servidor (triage inscrever <código> <apelido>).");
            return Falha;
        }
        // A chave antes da fila: com ela estragada, o "enviar" é o lugar de descobrir (e a mensagem aponta a saída)
        var (inscricao, chave) = new ArquivosDaInscricao(pastaDoTriage, cofre).Ler()!.Value;
        if (new FilaDeEnvios(pastaDoTriage).Pendentes().Count == 0)
        {
            saida.WriteLine("Nada na fila para enviar.");
            return Sucesso;
        }
        return EsvaziarFila(inscricao, chave) is null ? Sucesso : Falha;
    }

    /// <summary>Esvazia a fila e conta no terminal. Devolve por que parou (nulo = esvaziou).</summary>
    private RespostaDoEnvio? EsvaziarFila(Inscricao inscricao, string chave)
    {
        var enviador = new Enviador(new FilaDeEnvios(pastaDoTriage), windows.Conectar(new Uri(inscricao.Servidor)),
            inscricao, chave);
        var r = enviador.EsvaziarFila().GetAwaiter().GetResult();
        if (r.Entregues > 0)
            saida.WriteLine($"Enviada(s) ao servidor: {r.Entregues} revisão(ões).");
        foreach (var motivo in r.Recusadas)
            erros.WriteLine($"Revisão recusada pelo servidor e tirada da fila: {motivo}");
        switch (r.Parou)
        {
            case RespostaDoEnvio.SemPermissao:
                erros.WriteLine($"O servidor recusou a chave deste PC ({r.Detalhe}). Se ele foi revogado, peça um código " +
                    $"novo e rode triage desinscrever e triage inscrever. {r.Restantes} revisão(ões) na fila.");
                break;
            case RespostaDoEnvio.TentarDepois:
                saida.WriteLine($"Sem conexão com o servidor agora ({r.Detalhe}): {r.Restantes} revisão(ões) na fila, " +
                    "que sobem na próxima revisão (ou com triage enviar).");
                break;
        }
        return r.Parou;
    }

    private int Desinscrever()
    {
        if (windows.Cofre() is not { } cofre)
            return SoNoWindows();
        var arquivos = new ArquivosDaInscricao(pastaDoTriage, cofre);
        if (!arquivos.Inscrito)
        {
            saida.WriteLine("Este PC não estava inscrito.");
            return Sucesso;
        }
        var fila = new FilaDeEnvios(pastaDoTriage);
        var pendentes = fila.Pendentes().Count;
        arquivos.Apagar();
        fila.Esvaziar();
        saida.WriteLine("Inscrição apagada deste PC (chave e fila). No servidor, o administrador pode revogar a máquina antiga." +
            (pendentes > 0 ? $" {pendentes} revisão(ões) que não tinham subido foram descartadas." : ""));
        return Sucesso;
    }

    /// <summary>https sempre; http só para um servidor na própria máquina (a chave viajaria aberta pela rede).</summary>
    public static bool EnderecoAceito(string texto, out Uri endereco)
    {
        endereco = null!;
        if (!Uri.TryCreate(texto, UriKind.Absolute, out var uri))
            return false;
        var local = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local))
            return false;
        endereco = uri;
        return true;
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
        saida.WriteLine($"Ela revisa e guarda o relatório em {Path.Combine(pastaDoTriage, "relatorios")} (e, se este PC " +
            "estiver inscrito num servidor, manda a revisão para lá): nunca corrige nada.");
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
        erros.WriteLine("Isto só funciona no Windows. Fora dele, dá para revisar uma leitura gravada: " +
            "triage revisar --leitura <arquivo.json>");
        return Falha;
    }
}
