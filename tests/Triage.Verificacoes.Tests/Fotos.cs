using Triage.Verificacoes;

namespace Triage.Verificacoes.Tests;

/// <summary>Fotos prontas para os testes: a do notebook de verdade e uma "saudável" para variar um campo por vez.</summary>
public static class Fotos
{
    public static readonly DateTimeOffset Agora = new(2026, 10, 29, 12, 0, 0, TimeSpan.FromHours(-3));

    public static string Arquivo(string nome) => Path.Combine(AppContext.BaseDirectory, "dados", nome);

    public static FotoDoSistema NotebookSemAdmin() =>
        Gravacao.DeJson(File.ReadAllText(Arquivo("notebook-sem-admin.json")));

    public static FotoDoSistema Saudavel() => new(
        Agora,
        Administrador: true,
        Lido.Ok<IReadOnlyList<Volume>>([new Volume("C:", 200 * Gb, 100 * Gb, DoSistema: true)]),
        Lido.Ok<IReadOnlyList<Disco>>([new Disco("SSD Teste", TipoDeDisco.Nvme, 256 * Gb, SaudeDoDisco.Saudavel, Desgaste: 5)]),
        Lido.Ok(new Pasta(100 * Mb, 50)),
        Lido.Ok(new Pasta(100 * Mb, 50)),
        Lido.Ok(new Pasta(10 * Mb, 2)),
        Lido.Ok(new ConteudoDaLixeira(0, 0)),
        Lido.Ok(Agora.AddDays(-1)),
        Lido.Ok<Bateria?>(new Bateria(50_000, 45_000, 100)));

    /// <summary>Tudo pulado com o mesmo motivo, como num PC que não deixa ler nada.</summary>
    public static FotoDoSistema TudoPulado(MotivoDoPulo motivo) => new(
        Agora,
        Administrador: false,
        Lido.Pulado<IReadOnlyList<Volume>>(motivo),
        Lido.Pulado<IReadOnlyList<Disco>>(motivo),
        Lido.Pulado<Pasta>(motivo),
        Lido.Pulado<Pasta>(motivo),
        Lido.Pulado<Pasta>(motivo),
        Lido.Pulado<ConteudoDaLixeira>(motivo),
        Lido.Pulado<DateTimeOffset>(motivo),
        Lido.Pulado<Bateria?>(motivo));

    public const long Mb = 1L << 20;
    public const long Gb = 1L << 30;
}
