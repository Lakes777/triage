namespace Triage.Verificacoes;

/// <summary>O veredito de uma verificação. Pulado sempre vem com o motivo.</summary>
public sealed record Avaliacao
{
    public Gravidade Gravidade { get; }
    public string Resumo { get; }
    public MotivoDoPulo? Motivo { get; }

    public Avaliacao(Gravidade gravidade, string resumo, MotivoDoPulo? motivo = null)
    {
        if ((gravidade == Gravidade.Pulado) != (motivo is not null))
            throw new ArgumentException("Pulado precisa de motivo, e só pulado tem motivo.", nameof(motivo));
        Gravidade = gravidade;
        Resumo = resumo;
        Motivo = motivo;
    }

    public static Avaliacao Pulada(MotivoDoPulo motivo, string? detalhe = null) =>
        new(Gravidade.Pulado, detalhe ?? Textos.Motivo(motivo), motivo);
}

public sealed record Resultado(string Item, string Titulo, Avaliacao Avaliacao,
    string OQueE, string PorQueImporta, string ComoResolver);

public sealed record Revisao(DateTimeOffset FeitaEm, bool Administrador, IReadOnlyList<Resultado> Resultados);
