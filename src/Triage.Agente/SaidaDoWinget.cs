using System.Text.RegularExpressions;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// Lê a saída do `winget upgrade`. Só texto, sem tocar no Windows: é testado no Linux com a saída real gravada
/// (`tests/dados/winget-upgrade-ptbr.txt`).
/// </summary>
public static partial class SaidaDoWinget
{
    // APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE ("nenhuma atualização aplicável"): não é falta de rede
    public const int NenhumaAtualizacao = unchecked((int)0x8A15002B);

    /// <summary>
    /// Com tabela, a lista dela (mesmo com código de saída de erro: o que apareceu foi lido). Sem tabela, código 0 =
    /// nada desatualizado; qualquer outro = a busca não chegou ao fim (sem conexão), nunca "nada desatualizado".
    /// </summary>
    public static Lido<IReadOnlyList<ProgramaDesatualizado>> Interpretar(string saida, int codigoDeSaida)
    {
        if (LerTabelas(saida) is { } programas)
            // Cabeçalho sem nenhuma linha legível (ex.: nome com caracteres largos desalinhando as colunas) é leitura
            // que falhou, não "nada desatualizado"
            return programas.Count > 0 ? Lido.Ok(programas) : Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(MotivoDoPulo.Erro);
        return codigoDeSaida is 0 or NenhumaAtualizacao
            ? Lido.Ok<IReadOnlyList<ProgramaDesatualizado>>([])
            : Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(MotivoDoPulo.SemConexao);
    }

    /// <summary>
    /// Junta as linhas de todas as tabelas (a segunda, quando existe, é a dos que "exigem segmentação explícita").
    /// Nulo = nenhuma tabela na saída.
    /// </summary>
    public static IReadOnlyList<ProgramaDesatualizado>? LerTabelas(string saida)
    {
        var linhas = saida.Split('\n').Select(Limpar).ToList();
        List<ProgramaDesatualizado>? programas = null;
        for (var i = 1; i < linhas.Count; i++)
        {
            if (!EhSeparador(linhas[i]))
                continue;
            // O cabeçalho é traduzido ("Nome ID Versão Disponível Origem"): só a posição de cada coluna importa
            var colunas = InicioDasColunas(linhas[i - 1]);
            if (colunas.Count < 4)
                continue;
            programas ??= [];
            for (i++; i < linhas.Count && LerLinha(linhas[i], colunas) is { } programa; i++)
                programas.Add(programa);
            i--;
        }
        return programas;
    }

    // O winget anima o progresso reescrevendo a mesma linha com \r ("-", "\", "|", "/" e barras): vale o que
    // ficou por último, e um resto solto da animação conta como linha em branco
    private static string Limpar(string linha)
    {
        linha = Escapes().Replace(linha.TrimEnd('\r'), "");
        linha = linha[(linha.LastIndexOf('\r') + 1)..];
        return linha.Trim() is "-" or "\\" or "|" or "/" ? "" : linha.TrimEnd();
    }

    private static bool EhSeparador(string linha) => linha.Length >= 3 && linha.All(c => c == '-');

    // Cada coluna começa onde um nome do cabeçalho começa (os nomes são uma palavra só)
    private static List<int> InicioDasColunas(string cabecalho) =>
        Enumerable.Range(0, cabecalho.Length)
            .Where(i => cabecalho[i] != ' ' && (i == 0 || cabecalho[i - 1] == ' '))
            .ToList();

    // A tabela acaba na linha em branco ou na que não cabe nas colunas ("12 atualizações disponíveis."): numa linha
    // de verdade cada célula começa no início da coluna, logo depois do espaço que separa da anterior
    private static ProgramaDesatualizado? LerLinha(string linha, List<int> colunas)
    {
        if (linha.Length <= colunas[3])
            return null;
        if (colunas.Skip(1).Take(3).Any(c => linha[c - 1] != ' ' || linha[c] == ' '))
            return null;
        string Celula(int c) => (c + 1 < colunas.Count
            ? linha[colunas[c]..Math.Min(colunas[c + 1], linha.Length)]
            : linha[colunas[c]..]).Trim();
        var programa = new ProgramaDesatualizado(Celula(0), Celula(1), Celula(2), Celula(3));
        // O id do winget nunca tem espaço
        return programa is { Nome: "" } or { Versao: "" } or { Disponivel: "" } || programa.Id.Contains(' ') ? null : programa;
    }

    // Códigos de cor/cursor do terminal, se algum escapar com a saída redirecionada
    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex Escapes();
}
