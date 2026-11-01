namespace Triage.Agente;

/// <summary>O que uma limpeza encontrou ou fez. <see cref="Pulados"/> = em uso ou sem permissão.</summary>
public sealed record Limpeza(long Bytes, int Arquivos, int Pulados = 0, int Recentes = 0)
{
    public static readonly Limpeza Nada = new(0, 0);

    public static Limpeza operator +(Limpeza a, Limpeza b) =>
        new(a.Bytes + b.Bytes, a.Arquivos + b.Arquivos, a.Pulados + b.Pulados, a.Recentes + b.Recentes);
}

/// <summary>
/// Apaga o conteúdo de uma pasta prevista, nunca a pasta em si. Regras (SPEC, seção 6):
/// - só arquivos criados E modificados há mais de <see cref="IdadeMinima"/>: um instalador rodando agora usa o
///   temporário que acabou de criar, e muitos programas abrem arquivos deixando apagar mesmo em uso. A data de
///   criação conta porque quem extrai um .zip mantém a data de modificação original (de anos atrás);
/// - atalho de pasta (junção, link simbólico) nunca é seguido: um link no Temp apontando para Documentos
///   apagaria Documentos. A enumeração não entra neles, e logo antes de apagar cada arquivo as pastas entre ele e a
///   raiz são conferidas de novo (alguém pode trocar uma pasta por junção no meio). Limite conhecido: entre essa
///   conferência e o apagar ainda há uma janela pequena (ver SPEC, fase 5);
/// - arquivo em uso ou sem permissão é pulado e contado, sem parar a limpeza.
/// </summary>
public static class LimpadorDePasta
{
    public static readonly TimeSpan IdadeMinima = TimeSpan.FromHours(24);

    private static readonly EnumerationOptions Opcoes = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // O padrão pularia ocultos e de sistema, que também ocupam espaço; links não são seguidos nem apagados
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Quanto sairia agora, sem apagar nada (para a pergunta antes da correção).</summary>
    public static Limpeza Medir(string raiz, DateTimeOffset agora)
    {
        if (!Directory.Exists(raiz))
            return Limpeza.Nada;
        var limite = agora - IdadeMinima;
        long bytes = 0;
        int arquivos = 0, recentes = 0;
        foreach (var arquivo in new DirectoryInfo(raiz).EnumerateFiles("*", Opcoes))
        {
            if (Recente(arquivo, limite))
            {
                recentes++;
                continue;
            }
            bytes += arquivo.Length;
            arquivos++;
        }
        return new Limpeza(bytes, arquivos, Recentes: recentes);
    }

    public static Limpeza Limpar(string raiz, DateTimeOffset agora)
    {
        if (!Directory.Exists(raiz))
            return Limpeza.Nada;
        var limite = agora - IdadeMinima;
        var pastaRaiz = new DirectoryInfo(raiz);

        // As subpastas antigas são anotadas antes: apagar um arquivo muda a data da pasta para agora
        var pastasAntigas = pastaRaiz.EnumerateDirectories("*", Opcoes)
            .Where(p => p.LastWriteTimeUtc <= limite.UtcDateTime && p.CreationTimeUtc <= limite.UtcDateTime)
            .Select(p => p.FullName)
            .ToList();

        long bytes = 0;
        int apagados = 0, pulados = 0, recentes = 0;
        foreach (var arquivo in pastaRaiz.EnumerateFiles("*", Opcoes))
        {
            if (Recente(arquivo, limite))
            {
                recentes++;
                continue;
            }
            if (!SemLinkAteARaiz(raiz, arquivo.FullName))
            {
                pulados++;
                continue;
            }
            try
            {
                var tamanho = arquivo.Length;
                arquivo.Delete();
                bytes += tamanho;
                apagados++;
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                pulados++;
            }
        }

        // Das mais fundas para as de cima, e só as vazias: pasta com arquivo recente ou com link fica
        foreach (var pasta in pastasAntigas.OrderByDescending(p => p.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(pasta).Any())
                    Directory.Delete(pasta, recursive: false);
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                // Pasta vazia em uso: fica para a próxima, não ocupa espaço
            }
        }
        return new Limpeza(bytes, apagados, pulados, recentes);
    }

    private static bool Recente(FileInfo arquivo, DateTimeOffset limite) =>
        arquivo.LastWriteTimeUtc > limite.UtcDateTime || arquivo.CreationTimeUtc > limite.UtcDateTime;

    /// <summary>
    /// Nenhuma pasta entre o arquivo e a raiz (nem a raiz) virou link ou junção desde a enumeração. Também falha
    /// se o caminho não for de dentro da raiz.
    /// </summary>
    public static bool SemLinkAteARaiz(string raiz, string arquivo)
    {
        var topo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raiz));
        var pasta = Path.GetDirectoryName(Path.GetFullPath(arquivo));
        if (pasta is null || !(pasta + Path.DirectorySeparatorChar).StartsWith(topo + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            for (; pasta is not null && pasta.Length >= topo.Length; pasta = Path.GetDirectoryName(pasta))
            {
                if (File.GetAttributes(pasta).HasFlag(FileAttributes.ReparsePoint))
                    return false;
            }
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return true;
    }
}
