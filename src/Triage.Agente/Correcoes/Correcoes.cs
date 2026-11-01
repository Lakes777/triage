using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// O que a correção faria agora. Sem <see cref="Acao"/>, não há o que fazer (e o motivo vai no texto);
/// <see cref="Bloqueado"/> = não dá para fazer daqui (sem admin, pasta fora do padrão), e o comando sai com erro.
/// </summary>
public sealed record Plano(string Descricao, string? Acao, bool Bloqueado = false)
{
    public bool TemOQueFazer => Acao is not null;

    public static Plano Nada(string motivo) => new(motivo, null);

    public static Plano Impedido(string motivo) => new(motivo, null, Bloqueado: true);
}

public sealed record ResultadoDaCorrecao(long BytesLiberados, int Apagados, int Pulados, string Resumo);

/// <summary>
/// Uma correção, com o mesmo id da verificação que ela resolve. Nunca roda sozinha: quem chama mostra o plano e
/// pede confirmação (P4, P20, P26).
/// </summary>
public interface ICorrecao
{
    string Item { get; }
    string Titulo { get; }

    /// <summary>Entra no <c>corrigir --seguras</c>: baixo risco e só dentro de pastas de lixo do sistema (P26).</summary>
    bool Segura { get; }

    /// <summary><paramref name="agora"/> é o mesmo no plano e no aplicar: o que aparece na pergunta é o que sai.</summary>
    Plano Planejar(IAmbienteDeCorrecao ambiente, DateTimeOffset agora);
    ResultadoDaCorrecao Aplicar(IAmbienteDeCorrecao ambiente, DateTimeOffset agora);
}

public static class Correcoes
{
    public static readonly IReadOnlyList<ICorrecao> Todas =
        [new LimparTemporarios(), new EsvaziarLixeira(), new LimparCacheDoWindowsUpdate()];

    public static ICorrecao? Achar(string item) => Todas.FirstOrDefault(c => c.Item == item);

    /// <summary>
    /// A pasta é a que o nome diz? O Temp vem da variável TEMP, que qualquer programa pode trocar: com
    /// TEMP=C:\Users\fulano, "limpar temporários" apagaria os arquivos antigos da pasta do usuário.
    /// </summary>
    public static bool PastaConfere(string caminho, params string[] finalEsperado)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !Path.IsPathFullyQualified(caminho))
            return false;
        var partes = Path.TrimEndingDirectorySeparator(Path.GetFullPath(caminho))
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return partes.Length > finalEsperado.Length
            && partes[^finalEsperado.Length..].SequenceEqual(finalEsperado, StringComparer.OrdinalIgnoreCase);
    }

    internal static string Arquivos(Limpeza l) =>
        $"{Textos.Tamanho(l.Bytes)} em {Textos.Numero(l.Arquivos)} arquivo(s)";

    internal static string Resumo(Limpeza l)
    {
        var texto = $"liberou {Arquivos(l)}";
        if (l.Pulados > 0)
            texto += $"; {Textos.Numero(l.Pulados)} em uso ficaram";
        return texto;
    }
}

public sealed class LimparTemporarios : ICorrecao
{
    public string Item => "temporarios";
    public string Titulo => "Arquivos temporários";
    public bool Segura => true;

    private static IEnumerable<(string Nome, string Caminho)> Pastas(IAmbienteDeCorrecao a)
    {
        var pastas = new List<(string Nome, string Caminho)>();
        if (Correcoes.PastaConfere(a.TempDoUsuario, "Temp"))
            pastas.Add(("temporários do usuário", a.TempDoUsuario));
        if (a.Administrador && Correcoes.PastaConfere(a.TempDoWindows, "Windows", "Temp"))
            pastas.Add(("temporários do Windows", a.TempDoWindows));
        // Com TEMP=C:\Windows\Temp e o terminal como admin, a mesma pasta viria duas vezes (e contaria em dobro)
        return pastas.DistinctBy(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Caminho)),
            StringComparer.OrdinalIgnoreCase);
    }

    public Plano Planejar(IAmbienteDeCorrecao ambiente, DateTimeOffset agora)
    {
        var observacoes = new List<string>();
        if (!Correcoes.PastaConfere(ambiente.TempDoUsuario, "Temp"))
            observacoes.Add($"a pasta temporária do usuário está fora do padrão ({ambiente.TempDoUsuario}) e não será tocada");
        if (!ambiente.Administrador)
            observacoes.Add("a pasta do Windows só é limpa rodando como administrador");
        var medidas = Pastas(ambiente).Select(p => (p.Nome, Medida: LimpadorDePasta.Medir(p.Caminho, agora))).ToList();
        var total = medidas.Aggregate(Limpeza.Nada, (soma, m) => soma + m.Medida);
        var extra = observacoes.Count > 0 ? $" ({string.Join("; ", observacoes)})" : "";
        if (total.Arquivos == 0)
            return Plano.Nada($"Nada para apagar nos temporários com mais de 24 h{extra}.");
        var onde = string.Join(" e ", medidas.Where(m => m.Medida.Arquivos > 0).Select(m => m.Nome));
        return new Plano($"{Correcoes.Arquivos(total)} com mais de 24 h, nos {onde}{extra}",
            $"apagar {Correcoes.Arquivos(total)} dos temporários");
    }

    public ResultadoDaCorrecao Aplicar(IAmbienteDeCorrecao ambiente, DateTimeOffset agora)
    {
        var total = Pastas(ambiente).Aggregate(Limpeza.Nada,
            (soma, p) => soma + LimpadorDePasta.Limpar(p.Caminho, agora));
        return new ResultadoDaCorrecao(total.Bytes, total.Arquivos, total.Pulados, Correcoes.Resumo(total));
    }
}

public sealed class EsvaziarLixeira : ICorrecao
{
    public string Item => "lixeira";
    public string Titulo => "Lixeira";
    public bool Segura => true;

    public Plano Planejar(IAmbienteDeCorrecao ambiente, DateTimeOffset agora)
    {
        var lixeira = ambiente.LerLixeira();
        if (lixeira.Itens == 0)
            return Plano.Nada("A lixeira já está vazia.");
        var conteudo = $"{Textos.Tamanho(lixeira.Bytes)} em {Textos.Numero(lixeira.Itens)} item(ns)";
        // A lixeira é a última chance de recuperar um arquivo: a pergunta diz isso com todas as letras
        return new Plano($"{conteudo}, apagados para sempre (confira antes se não quer nada de volta)",
            $"esvaziar a lixeira ({conteudo})");
    }

    public ResultadoDaCorrecao Aplicar(IAmbienteDeCorrecao ambiente, DateTimeOffset agora)
    {
        var antes = ambiente.LerLixeira();
        ambiente.EsvaziarLixeira();
        var depois = ambiente.LerLixeira();
        var bytes = Math.Max(0, antes.Bytes - depois.Bytes);
        var itens = (int)Math.Max(0, antes.Itens - depois.Itens);
        var resumo = $"liberou {Textos.Tamanho(bytes)} em {Textos.Numero(itens)} item(ns)";
        if (depois.Itens > 0)
            resumo += $"; {Textos.Numero(depois.Itens)} ficaram (em uso)";
        return new ResultadoDaCorrecao(bytes, itens, (int)depois.Itens, resumo);
    }
}

public sealed class LimparCacheDoWindowsUpdate : ICorrecao
{
    public string Item => "cache-windows-update";
    public string Titulo => "Cache do Windows Update";

    // Para um serviço do sistema e precisa de admin: só item por item, nunca no --seguras
    public bool Segura => false;

    public Plano Planejar(IAmbienteDeCorrecao ambiente, DateTimeOffset agora)
    {
        if (!ambiente.Administrador)
            return Plano.Impedido("Precisa de administrador: abra o Terminal como administrador e rode de novo.");
        if (!Correcoes.PastaConfere(ambiente.CacheDoWindowsUpdate, "SoftwareDistribution", "Download"))
            return Plano.Impedido($"A pasta do cache está fora do padrão ({ambiente.CacheDoWindowsUpdate}): nada será tocado.");
        var medida = LimpadorDePasta.Medir(ambiente.CacheDoWindowsUpdate, agora);
        if (medida.Arquivos == 0)
            return Plano.Nada("Nada para apagar no cache com mais de 24 h.");
        return new Plano(
            $"{Correcoes.Arquivos(medida)} com mais de 24 h; o serviço do Windows Update fica parado durante a limpeza",
            $"apagar {Correcoes.Arquivos(medida)} do cache do Windows Update");
    }

    public ResultadoDaCorrecao Aplicar(IAmbienteDeCorrecao ambiente, DateTimeOffset agora)
    {
        if (!ambiente.Administrador || !Correcoes.PastaConfere(ambiente.CacheDoWindowsUpdate, "SoftwareDistribution", "Download"))
            throw new InvalidOperationException("O cache do Windows Update só é limpo como administrador e na pasta prevista.");
        var estavaRodando = ambiente.WindowsUpdateRodando();
        try
        {
            // Dentro do try: se o serviço demorar a parar (TimeoutException), o finally ainda religa
            ambiente.PararWindowsUpdate();
            var limpeza = LimpadorDePasta.Limpar(ambiente.CacheDoWindowsUpdate, agora);
            return new ResultadoDaCorrecao(limpeza.Bytes, limpeza.Arquivos, limpeza.Pulados, Correcoes.Resumo(limpeza));
        }
        finally
        {
            // Religa mesmo se a limpeza falhar: deixar o Windows Update parado seria pior que o cache cheio
            if (estavaRodando)
                ambiente.IniciarWindowsUpdate();
        }
    }
}
