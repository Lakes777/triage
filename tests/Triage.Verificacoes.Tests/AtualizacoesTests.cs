using Triage.Verificacoes;
using Triage.Verificacoes.Itens;
using static Triage.Verificacoes.Tests.Fotos;

namespace Triage.Verificacoes.Tests;

/// <summary>Grupo "rede" da fase 3: Atualizações do Windows e programas desatualizados (winget).</summary>
public class AtualizacoesTests
{
    // Busca online feita sem nada pendente, sem reinício pendente e última instalação há 3 dias
    private static FotoDoSistema EmDia() => Saudavel() with
    {
        Atualizacoes = Lido.Ok(new AtualizacoesPendentes(0, 0)),
        ReinicioPendente = Lido.Ok(false),
        UltimaAtualizacaoInstalada = Lido.Ok<DateTimeOffset?>(Agora.AddDays(-3)),
    };

    private static Avaliacao Atualizacoes(FotoDoSistema foto, Limites? limites = null) =>
        new AtualizacoesDoWindows().Avaliar(foto, limites ?? Limites.Padrao);

    [Fact]
    public void Windows_em_dia_e_ok()
    {
        var avaliacao = Atualizacoes(EmDia());

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.Equal("nenhuma atualização pendente; última atualização instalada há 3 dias (26/10/2026)", avaliacao.Resumo);
    }

    [Theory]
    [InlineData(29.9, Gravidade.Ok)]
    [InlineData(30, Gravidade.Atencao)]
    [InlineData(59.9, Gravidade.Atencao)]
    [InlineData(60, Gravidade.Critico)]
    public void Dias_desde_a_ultima_instalacao_seguem_os_limites(double dias, Gravidade esperada)
    {
        var foto = EmDia() with { UltimaAtualizacaoInstalada = Lido.Ok<DateTimeOffset?>(Agora.AddDays(-dias)) };

        Assert.Equal(esperada, Atualizacoes(foto).Gravidade);
    }

    [Fact]
    public void Mudar_o_limite_de_dias_muda_a_gravidade()
    {
        var foto = EmDia() with { UltimaAtualizacaoInstalada = Lido.Ok<DateTimeOffset?>(Agora.AddDays(-40)) };

        Assert.Equal(Gravidade.Atencao, Atualizacoes(foto).Gravidade);
        Assert.Equal(Gravidade.Ok, Atualizacoes(foto, Limites.Padrao with { AtualizacaoAtencaoDias = 45 }).Gravidade);
    }

    [Theory]
    [InlineData(1, 0, "1 atualização pendente;")]
    [InlineData(1, 1, "1 atualização pendente, de segurança;")]
    [InlineData(4, 0, "4 atualizações pendentes;")]
    [InlineData(4, 2, "4 atualizações pendentes (2 de segurança);")]
    public void Atualizacao_pendente_e_atencao(int pendentes, int deSeguranca, string inicio)
    {
        var foto = EmDia() with { Atualizacoes = Lido.Ok(new AtualizacoesPendentes(pendentes, deSeguranca)) };

        var avaliacao = Atualizacoes(foto);

        Assert.Equal(Gravidade.Atencao, avaliacao.Gravidade);
        Assert.StartsWith(inicio, avaliacao.Resumo);
    }

    [Fact]
    public void Reinicio_pendente_e_atencao()
    {
        var avaliacao = Atualizacoes(EmDia() with { ReinicioPendente = Lido.Ok(true) });

        Assert.Equal(Gravidade.Atencao, avaliacao.Gravidade);
        Assert.Contains("espera reiniciar", avaliacao.Resumo);
    }

    [Fact]
    public void Vale_o_pior_entre_pendentes_e_dias()
    {
        var foto = EmDia() with
        {
            Atualizacoes = Lido.Ok(new AtualizacoesPendentes(3, 1)),
            UltimaAtualizacaoInstalada = Lido.Ok<DateTimeOffset?>(Agora.AddDays(-90)),
        };

        Assert.Equal(Gravidade.Critico, Atualizacoes(foto).Gravidade);
    }

    [Fact]
    public void Historico_vazio_nao_e_problema_por_si_so()
    {
        var avaliacao = Atualizacoes(EmDia() with { UltimaAtualizacaoInstalada = Lido.Ok<DateTimeOffset?>(null) });

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.Contains("nenhuma atualização no histórico", avaliacao.Resumo);
    }

    [Fact]
    public void Partes_offline_nao_lidas_aparecem_no_resumo()
    {
        var foto = EmDia() with
        {
            ReinicioPendente = Lido.Pulado<bool>(MotivoDoPulo.SemAdmin),
            UltimaAtualizacaoInstalada = Lido.Pulado<DateTimeOffset?>(MotivoDoPulo.Erro),
        };

        var avaliacao = Atualizacoes(foto);

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.Equal("nenhuma atualização pendente; reinício pendente não lido: precisa de admin; histórico não lido: erro ao ler", avaliacao.Resumo);
    }

    // P7/P25: a busca online pulada nunca deixa o item "ok". CONTRAPROVA feita em 29/10/2026: com a volta para
    // "ok" quando a parte offline está boa (no lugar do Avaliacao.Pulada), este teste quebrou
    [Theory]
    [InlineData(MotivoDoPulo.SemConexao)]
    [InlineData(MotivoDoPulo.SemAdmin)]
    [InlineData(MotivoDoPulo.Erro)]
    public void Sem_a_busca_online_e_offline_bom_sai_pulado_e_nunca_ok(MotivoDoPulo motivo)
    {
        var foto = EmDia() with { Atualizacoes = Lido.Pulado<AtualizacoesPendentes>(motivo) };

        var avaliacao = Atualizacoes(foto);

        Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
        Assert.Equal(motivo, avaliacao.Motivo);
        Assert.StartsWith($"a busca online não rodou ({Textos.Motivo(motivo)})", avaliacao.Resumo);
        // O que deu para ver sem rede continua no resumo
        Assert.Contains("última atualização instalada há 3 dias", avaliacao.Resumo);
    }

    [Fact]
    public void Sem_a_busca_online_e_sem_nada_offline_sai_pulado_com_o_motivo_da_busca()
    {
        var foto = Saudavel() with
        {
            Atualizacoes = Lido.Pulado<AtualizacoesPendentes>(MotivoDoPulo.SemConexao),
            ReinicioPendente = Lido.Pulado<bool>(MotivoDoPulo.Erro),
            UltimaAtualizacaoInstalada = Lido.Pulado<DateTimeOffset?>(MotivoDoPulo.Erro),
        };

        var avaliacao = Atualizacoes(foto);

        Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
        Assert.Equal(MotivoDoPulo.SemConexao, avaliacao.Motivo);
    }

    [Theory]
    [InlineData(10, false, Gravidade.Pulado)]
    [InlineData(29.9, false, Gravidade.Pulado)]
    [InlineData(30, false, Gravidade.Atencao)]
    [InlineData(60, false, Gravidade.Critico)]
    [InlineData(10, true, Gravidade.Atencao)]
    [InlineData(60, true, Gravidade.Critico)]
    public void Sem_a_busca_online_um_problema_offline_decide(double dias, bool reinicio, Gravidade esperada)
    {
        var foto = EmDia() with
        {
            Atualizacoes = Lido.Pulado<AtualizacoesPendentes>(MotivoDoPulo.SemConexao),
            ReinicioPendente = Lido.Ok(reinicio),
            UltimaAtualizacaoInstalada = Lido.Ok<DateTimeOffset?>(Agora.AddDays(-dias)),
        };

        var avaliacao = Atualizacoes(foto);

        Assert.Equal(esperada, avaliacao.Gravidade);
        // Com ou sem gravidade, o resumo avisa que a busca online ficou de fora
        Assert.Contains("a busca online não rodou (sem conexão)", avaliacao.Resumo);
    }

    [Fact]
    public void Leitura_gravada_antes_da_fase_3_sai_pulada()
    {
        var avaliacao = Atualizacoes(Saudavel());

        Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
        Assert.Equal(MotivoDoPulo.NaoSuportado, avaliacao.Motivo);
    }

    private static Avaliacao Programas(params ProgramaDesatualizado[] programas) =>
        new ProgramasDesatualizados().Avaliar(
            Saudavel() with { ProgramasDesatualizados = Lido.Ok<IReadOnlyList<ProgramaDesatualizado>>(programas) },
            Limites.Padrao);

    private static ProgramaDesatualizado[] Varios(int quantos) =>
        [.. Enumerable.Range(1, quantos).Select(i => new ProgramaDesatualizado($"Programa {i}", $"Teste.P{i}", $"{i}.0", $"{i}.1"))];

    [Theory]
    [InlineData(0, Gravidade.Ok)]
    [InlineData(4, Gravidade.Ok)]
    [InlineData(5, Gravidade.Atencao)]
    [InlineData(12, Gravidade.Atencao)]
    public void Programas_desatualizados_seguem_o_limite(int quantos, Gravidade esperada)
    {
        Assert.Equal(esperada, Programas(Varios(quantos)).Gravidade);
    }

    [Fact]
    public void Programas_desatualizados_mostram_ate_5_nomes_e_o_resto_em_numero()
    {
        var avaliacao = Programas([new ProgramaDesatualizado("Git", "Git.Git", "2.55.0.5", "2.56.0.3"), .. Varios(6)]);

        Assert.Equal("7 programas com versão nova: Git (2.55.0.5 → 2.56.0.3), Programa 1 (1.0 → 1.1), "
            + "Programa 2 (2.0 → 2.1), Programa 3 (3.0 → 3.1), Programa 4 (4.0 → 4.1) e mais 2", avaliacao.Resumo);
    }

    [Fact]
    public void Programas_desatualizados_no_singular_e_sem_resto()
    {
        var avaliacao = Programas(new ProgramaDesatualizado("Git", "Git.Git", "2.55.0.5", "2.56.0.3"));

        Assert.Equal("1 programa com versão nova: Git (2.55.0.5 → 2.56.0.3)", avaliacao.Resumo);
    }

    [Fact]
    public void Nenhum_programa_desatualizado_e_ok()
    {
        var avaliacao = Programas();

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.Equal("todos os programas do winget estão na versão mais nova", avaliacao.Resumo);
    }

    [Theory]
    [InlineData(MotivoDoPulo.SemConexao)]
    [InlineData(MotivoDoPulo.NaoSuportado)]
    public void Winget_sem_rede_ou_ausente_sai_pulado_e_nunca_ok(MotivoDoPulo motivo)
    {
        var foto = Saudavel() with { ProgramasDesatualizados = Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(motivo) };

        var avaliacao = new ProgramasDesatualizados().Avaliar(foto, Limites.Padrao);

        Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
        Assert.Equal(motivo, avaliacao.Motivo);
    }
}
