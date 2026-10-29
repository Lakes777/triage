using Triage.Verificacoes;
using Triage.Verificacoes.Itens;
using static Triage.Verificacoes.Tests.Fotos;

namespace Triage.Verificacoes.Tests;

/// <summary>Fase 3, grupo "locais": inicialização, drivers, desligamentos e telas azuis (e o pulado dos 6 itens).</summary>
public class SistemaTests
{
    private static readonly IVerificacao[] Locais =
        [new ProgramasNaInicializacao(), new DriversComErro(), new DesligamentosInesperados(), new TelasAzuis(), new AntivirusAtivo(), new FirewallLigado()];

    [Theory]
    [InlineData(MotivoDoPulo.SemAdmin)]
    [InlineData(MotivoDoPulo.SemPermissao)]
    [InlineData(MotivoDoPulo.Erro)]
    public void Leitura_pulada_vira_pulado_com_o_motivo_e_nunca_ok(MotivoDoPulo motivo)
    {
        var foto = TudoPulado(motivo);

        Assert.All(Locais, v =>
        {
            var avaliacao = v.Avaliar(foto, Limites.Padrao);
            Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
            Assert.Equal(motivo, avaliacao.Motivo);
        });
    }

    [Fact]
    public void Leitura_gravada_antes_da_fase_3_sai_pulada_e_nao_ok()
    {
        // O JSON do notebook é da fase 2: não tem os campos novos
        var antiga = NotebookSemAdmin();

        Assert.All(Locais, v => Assert.Equal(Gravidade.Pulado, v.Avaliar(antiga, Limites.Padrao).Gravidade));
    }

    [Theory]
    [InlineData(9, Gravidade.Ok)]
    [InlineData(10, Gravidade.Atencao)]
    public void Inicializacao_pela_quantidade(int quantos, Gravidade esperada)
    {
        var foto = Saudavel() with { Inicializacao = Programas(quantos) };

        Assert.Equal(esperada, new ProgramasNaInicializacao().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Fact]
    public void Inicializacao_mostra_a_contagem_e_ate_5_nomes()
    {
        var foto = Saudavel() with { Inicializacao = Programas(7) };

        Assert.Equal("7 programas: Programa 1, Programa 2, Programa 3, Programa 4, Programa 5 e mais 2",
            new ProgramasNaInicializacao().Avaliar(foto, Limites.Padrao).Resumo);
    }

    [Fact]
    public void Inicializacao_vazia_e_ok()
    {
        var avaliacao = new ProgramasNaInicializacao().Avaliar(Saudavel() with { Inicializacao = Programas(0) }, Limites.Padrao);

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.Equal("nenhum programa abre sozinho", avaliacao.Resumo);
    }

    [Fact]
    public void Mudar_o_limite_da_inicializacao_muda_a_gravidade()
    {
        var foto = Saudavel() with { Inicializacao = Programas(4) };

        Assert.Equal(Gravidade.Atencao, new ProgramasNaInicializacao().Avaliar(foto, Limites.Padrao with { InicializacaoAtencao = 4 }).Gravidade);
    }

    [Fact]
    public void Driver_com_erro_e_atencao_com_classe_e_codigo_sem_o_nome()
    {
        var foto = Saudavel() with
        {
            DriversComErro = Lido.Ok<IReadOnlyList<DispositivoComErro>>([new DispositivoComErro("Camera", 28)]),
        };

        var avaliacao = new DriversComErro().Avaliar(foto, Limites.Padrao);

        Assert.Equal(Gravidade.Atencao, avaliacao.Gravidade);
        Assert.Equal("Camera (código 28)", avaliacao.Resumo);
    }

    [Fact]
    public void Sem_driver_com_erro_e_ok()
    {
        var foto = Saudavel() with { DriversComErro = Lido.Ok<IReadOnlyList<DispositivoComErro>>([]) };

        Assert.Equal(Gravidade.Ok, new DriversComErro().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Theory]
    [InlineData(0, Gravidade.Ok)]
    [InlineData(1, Gravidade.Atencao)]
    [InlineData(2, Gravidade.Atencao)]
    [InlineData(3, Gravidade.Critico)]
    public void Desligamentos_pelos_limites(int quantos, Gravidade esperada)
    {
        var foto = Saudavel() with { Eventos = Lido.Ok(new EventosDoSistema(30, quantos, 0)) };

        Assert.Equal(esperada, new DesligamentosInesperados().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Theory]
    [InlineData(0, Gravidade.Ok)]
    [InlineData(1, Gravidade.Atencao)]
    [InlineData(2, Gravidade.Critico)]
    public void Telas_azuis_pelos_limites(int quantos, Gravidade esperada)
    {
        var foto = Saudavel() with { Eventos = Lido.Ok(new EventosDoSistema(30, 0, quantos)) };

        Assert.Equal(esperada, new TelasAzuis().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    [Fact]
    public void Eventos_contam_cada_um_o_seu()
    {
        var foto = Saudavel() with { Eventos = Lido.Ok(new EventosDoSistema(30, 4, 0)) };

        Assert.Equal("4 nos últimos 30 dias", new DesligamentosInesperados().Avaliar(foto, Limites.Padrao).Resumo);
        Assert.Equal("nenhuma nos últimos 30 dias", new TelasAzuis().Avaliar(foto, Limites.Padrao).Resumo);
        Assert.Equal(Gravidade.Ok, new TelasAzuis().Avaliar(foto, Limites.Padrao).Gravidade);
    }

    private static Lido<IReadOnlyList<ProgramaNaInicializacao>> Programas(int quantos) =>
        Lido.Ok<IReadOnlyList<ProgramaNaInicializacao>>(Enumerable.Range(1, quantos)
            .Select(i => new ProgramaNaInicializacao($"Programa {i}", OrigemDaInicializacao.RegistroDoUsuario)).ToList());
}
