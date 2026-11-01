namespace Triage.Agente;

/// <summary>Os relatórios guardados no PC: os mais antigos são apagados (o Triage não pode virar lixo no PC).</summary>
public static class PastaDeRelatorios
{
    public const int Guardados = 20;

    /// <summary>
    /// Apaga os relatórios além dos <see cref="Guardados"/> mais recentes e devolve quantos não saíram. O nome vem
    /// da hora da leitura: uma leitura gravada antiga (--leitura) ou um relógio atrasado dão ao relatório novo um
    /// nome "mais velho" que os outros, então o <paramref name="atual"/> nunca entra na conta. Só toca em
    /// revisao-*.html. Arquivo que não apaga (antivírus, somente leitura) é contado, não derruba a revisão.
    /// </summary>
    public static int ApagarAntigos(string pasta, string atual, Action<string>? apagar = null)
    {
        apagar ??= File.Delete;
        var antigos = Directory.GetFiles(pasta, "revisao-*.html")
            .Where(a => !string.Equals(Path.GetFullPath(a), Path.GetFullPath(atual), StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .SkipLast(Guardados - 1);
        var falhas = 0;
        foreach (var antigo in antigos)
        {
            try
            {
                apagar(antigo);
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                falhas++;
            }
        }
        return falhas;
    }
}
