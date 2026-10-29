using System.Globalization;

namespace Triage.Verificacoes;

/// <summary>Formatação para gente ler, sempre em português do Brasil (independe do idioma do Windows).</summary>
public static class Textos
{
    public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    public static string Tamanho(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Format(Cultura, "{0:0.#} GB", bytes / (double)(1L << 30)),
        >= 1L << 20 => string.Format(Cultura, "{0:0} MB", bytes / (double)(1L << 20)),
        >= 1L << 10 => string.Format(Cultura, "{0:0} KB", bytes / (double)(1L << 10)),
        _ => string.Format(Cultura, "{0} bytes", bytes),
    };

    public static string Numero(long n) => n.ToString("N0", Cultura);

    public static string Porcento(double p) => string.Format(Cultura, "{0:0}%", Math.Floor(p));

    public static string Motivo(MotivoDoPulo motivo) => motivo switch
    {
        MotivoDoPulo.SemAdmin => "precisa de admin",
        MotivoDoPulo.SemConexao => "sem conexão",
        MotivoDoPulo.SemPermissao => "sem permissão",
        MotivoDoPulo.NaoSuportado => "este PC não informa",
        _ => "erro ao ler",
    };

    public static Gravidade Pior(IEnumerable<Gravidade> gravidades) =>
        gravidades.DefaultIfEmpty(Gravidade.Ok).MaxBy(Peso);

    // Só para Ok/Atenção/Crítico: quem chama trata pulado e "não se aplica" antes
    private static int Peso(Gravidade g) => g switch
    {
        Gravidade.Critico => 2,
        Gravidade.Atencao => 1,
        _ => 0,
    };
}
