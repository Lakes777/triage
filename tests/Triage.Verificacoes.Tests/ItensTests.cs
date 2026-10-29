using Triage.Verificacoes;
using Triage.Verificacoes.Itens;
using static Triage.Verificacoes.Tests.Fotos;

namespace Triage.Verificacoes.Tests;

public class ItensTests
{
    [Theory]
    [InlineData(4.9, Gravidade.Critico)]
    [InlineData(5, Gravidade.Atencao)]
    [InlineData(14.9, Gravidade.Atencao)]
    [InlineData(15, Gravidade.Ok)]
    public void Espaco_livre_segue_os_limites(double porcentoLivre, Gravidade esperada)
    {
        var foto = Saudavel() with { Volumes = Volumes(porcentoLivre) };

        Assert.Equal(esperada, new EspacoLivre().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Fact]
    public void Mudar_um_limite_muda_a_gravidade()
    {
        var foto = Saudavel() with { Volumes = Volumes(10) };

        Assert.Equal(Gravidade.Atencao, new EspacoLivre().Avaliar(foto, Limites.Padrao).Gravidade);
        Assert.Equal(Gravidade.Ok, new EspacoLivre().Avaliar(foto, Limites.Padrao with { EspacoLivreAtencaoPorcento = 8 }).Gravidade);
    }

    [Fact]
    public void Espaco_livre_vale_a_pior_unidade()
    {
        var foto = Saudavel() with
        {
            Volumes = Lido.Ok<IReadOnlyList<Volume>>([new Volume("C:", 100 * Gb, 50 * Gb, true), new Volume("D:", 100 * Gb, 3 * Gb, false)]),
        };

        var avaliacao = new EspacoLivre().Avaliar(foto, Limites.Padrao);

        Assert.Equal(Gravidade.Critico, avaliacao.Gravidade);
        Assert.Contains("D: 3 GB livres de 100 GB (3%)", avaliacao.Resumo);
    }

    [Theory]
    [InlineData(SaudeDoDisco.Ruim, 5, Gravidade.Critico)]
    [InlineData(SaudeDoDisco.Alerta, 5, Gravidade.Atencao)]
    [InlineData(SaudeDoDisco.Saudavel, 90, Gravidade.Critico)]
    [InlineData(SaudeDoDisco.Saudavel, 70, Gravidade.Atencao)]
    [InlineData(SaudeDoDisco.Saudavel, 69, Gravidade.Ok)]
    public void Disco_pela_saude_e_pelo_desgaste(SaudeDoDisco saude, int desgaste, Gravidade esperada)
    {
        var foto = Saudavel() with { Discos = Discos(new Disco("SSD", TipoDeDisco.Nvme, 256 * Gb, saude, desgaste)) };

        Assert.Equal(esperada, new SaudeDosDiscos().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Fact]
    public void Disco_com_saude_desconhecida_e_pulado_e_nao_ok()
    {
        var foto = Saudavel() with { Discos = Discos(new Disco("Velho", TipoDeDisco.Hd, 500 * Gb, SaudeDoDisco.Desconhecida, null)) };

        var avaliacao = new SaudeDosDiscos().Avaliar(foto, Limites.Padrao);

        Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
        Assert.Equal(MotivoDoPulo.NaoSuportado, avaliacao.Motivo);
    }

    [Fact]
    public void Disco_com_saude_desconhecida_mas_muito_gasto_e_critico()
    {
        var foto = Saudavel() with
        {
            Discos = Discos(
                new Disco("Saudável", TipoDeDisco.Ssd, 120 * Gb, SaudeDoDisco.Saudavel, 3),
                new Disco("Gasto", TipoDeDisco.Nvme, 256 * Gb, SaudeDoDisco.Desconhecida, 95)),
        };

        Assert.Equal(Gravidade.Critico, new SaudeDosDiscos().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Fact]
    public void Bateria_que_nao_informa_a_capacidade_atual_e_pulada_e_nao_critica()
    {
        var foto = Saudavel() with { Bateria = Lido.Ok<Bateria?>(new Bateria(50_000, 0, null)) };

        var avaliacao = new SaudeDaBateria().Avaliar(foto, Limites.Padrao);

        Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
        Assert.Equal(MotivoDoPulo.NaoSuportado, avaliacao.Motivo);
    }

    [Fact]
    public void Temporarios_avisam_quando_a_pasta_do_usuario_fica_de_fora()
    {
        var foto = Saudavel() with { TempDoUsuario = Lido.Pulado<Pasta>(MotivoDoPulo.Erro) };

        Assert.Contains("a pasta do usuário ficou de fora: erro ao ler", new ArquivosTemporarios().Avaliar(foto, Limites.Padrao).Resumo);
    }

    [Theory]
    [InlineData(29_999, Gravidade.Critico)]   // 59,99%
    [InlineData(30_000, Gravidade.Atencao)]   // 60%
    [InlineData(39_999, Gravidade.Atencao)]
    [InlineData(40_000, Gravidade.Ok)]        // 80%
    public void Bateria_pela_capacidade_original(int atual, Gravidade esperada)
    {
        var foto = Saudavel() with { Bateria = Lido.Ok<Bateria?>(new Bateria(50_000, atual, null)) };

        Assert.Equal(esperada, new SaudeDaBateria().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Fact]
    public void Desktop_sem_bateria_nao_se_aplica()
    {
        var desktop = NotebookSemAdmin() with { Bateria = Lido.Ok<Bateria?>(null) };

        var avaliacao = new SaudeDaBateria().Avaliar(desktop, Limites.Padrao);

        Assert.Equal(Gravidade.NaoSeAplica, avaliacao.Gravidade);
    }

    [Theory]
    [InlineData(30, Gravidade.Critico)]
    [InlineData(7, Gravidade.Atencao)]
    [InlineData(6.9, Gravidade.Ok)]
    public void Tempo_sem_reiniciar(double dias, Gravidade esperada)
    {
        var foto = Saudavel() with { UltimaInicializacao = Lido.Ok(Agora.AddDays(-dias)) };

        Assert.Equal(esperada, new TempoSemReiniciar().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Fact]
    public void Tempo_sem_reiniciar_escreve_dia_no_singular_e_no_plural()
    {
        Assert.Contains("há 1 dia e 2 h", Reinicio(TimeSpan.FromHours(26)));
        Assert.Contains("há 3 dias e 0 h", Reinicio(TimeSpan.FromDays(3)));
        Assert.Contains("há 5 h", Reinicio(TimeSpan.FromHours(5)));
    }

    [Fact]
    public void Temporarios_somam_as_pastas_lidas_e_avisam_a_que_ficou_de_fora()
    {
        var foto = Saudavel() with
        {
            TempDoUsuario = Lido.Ok(new Pasta(3 * Gb, 1000)),
            TempDoWindows = Lido.Pulado<Pasta>(MotivoDoPulo.SemAdmin),
        };

        var avaliacao = new ArquivosTemporarios().Avaliar(foto, Limites.Padrao);

        Assert.Equal(Gravidade.Atencao, avaliacao.Gravidade);
        Assert.Equal("3 GB em 1.000 arquivos (a pasta do Windows ficou de fora: precisa de admin)", avaliacao.Resumo);
    }

    [Theory]
    [InlineData(2 * Gb, Gravidade.Atencao)]
    [InlineData(2 * Gb - 1, Gravidade.Ok)]
    public void Lixeira_e_cache_do_update_pelo_tamanho(long bytes, Gravidade esperada)
    {
        var foto = Saudavel() with
        {
            Lixeira = Lido.Ok(new ConteudoDaLixeira(bytes, 3)),
            CacheDoWindowsUpdate = Lido.Ok(new Pasta(bytes, 3)),
        };

        Assert.Equal(esperada, new TamanhoDaLixeira().Avaliar(foto, Limites.Padrao).Gravidade);
        Assert.Equal(esperada, new CacheDoWindowsUpdate().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    private static Lido<IReadOnlyList<Volume>> Volumes(double porcentoLivre) =>
        Lido.Ok<IReadOnlyList<Volume>>([new Volume("C:", 1000 * Gb, (long)(1000 * Gb * porcentoLivre / 100), true)]);

    private static Lido<IReadOnlyList<Disco>> Discos(params Disco[] discos) => Lido.Ok<IReadOnlyList<Disco>>(discos);

    private static string Reinicio(TimeSpan ligado) =>
        new TempoSemReiniciar().Avaliar(Saudavel() with { UltimaInicializacao = Lido.Ok(Agora - ligado) }, Limites.Padrao).Resumo;
}
