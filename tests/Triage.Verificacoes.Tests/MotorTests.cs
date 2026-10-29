using Triage.Verificacoes;

namespace Triage.Verificacoes.Tests;

public class MotorTests
{
    [Fact]
    public void Notebook_de_verdade_tem_os_7_itens_da_fase_2_lidos()
    {
        var revisao = Motor.Padrao().Revisar(Fotos.NotebookSemAdmin(), Limites.Padrao);
        string[] fase2 = ["disco-saude", "espaco-livre", "temporarios", "cache-windows-update", "lixeira", "reinicio", "bateria"];

        Assert.Equal(15, revisao.Resultados.Count);
        Assert.False(revisao.Administrador);
        Assert.DoesNotContain(revisao.Resultados, r => fase2.Contains(r.Item) && r.Avaliacao.Gravidade == Gravidade.Pulado);
        // Sem admin, o que ficou de fora aparece no resumo em vez de sumir
        Assert.Contains("precisa de admin", Resumo(revisao, "disco-saude"));
        Assert.Contains("pasta do Windows ficou de fora", Resumo(revisao, "temporarios"));
        Assert.Contains("88%", Resumo(revisao, "bateria"));
    }

    [Theory]
    [InlineData(MotivoDoPulo.SemAdmin)]
    [InlineData(MotivoDoPulo.SemConexao)]
    [InlineData(MotivoDoPulo.Erro)]
    public void Leitura_pulada_vira_pulado_com_o_motivo_e_nunca_ok(MotivoDoPulo motivo)
    {
        var revisao = Motor.Padrao().Revisar(Fotos.TudoPulado(motivo), Limites.Padrao);

        Assert.All(revisao.Resultados, r =>
        {
            Assert.Equal(Gravidade.Pulado, r.Avaliacao.Gravidade);
            Assert.Equal(motivo, r.Avaliacao.Motivo);
        });
    }

    [Fact]
    public void Verificacao_com_defeito_sai_pulada_sem_derrubar_as_outras()
    {
        var motor = new Motor([new Quebrada(), .. Motor.Padrao().Verificacoes]);

        var revisao = motor.Revisar(Fotos.Saudavel(), Limites.Padrao);

        var quebrada = Assert.Single(revisao.Resultados, r => r.Item == "quebrada");
        Assert.Equal(Gravidade.Pulado, quebrada.Avaliacao.Gravidade);
        Assert.Equal(MotivoDoPulo.Erro, quebrada.Avaliacao.Motivo);
        Assert.Equal(Motor.Padrao().Verificacoes.Count + 1, revisao.Resultados.Count);
    }

    [Fact]
    public void Criticos_vem_primeiro_depois_atencao_pulado_ok_e_nao_se_aplica()
    {
        var foto = Fotos.Saudavel() with
        {
            Volumes = Lido.Ok<IReadOnlyList<Volume>>([new Volume("C:", 100 * Fotos.Gb, 1 * Fotos.Gb, true)]),
            Lixeira = Lido.Ok(new ConteudoDaLixeira(5 * Fotos.Gb, 10)),
            Bateria = Lido.Ok<Bateria?>(null),
            UltimaInicializacao = Lido.Pulado<DateTimeOffset>(MotivoDoPulo.Erro),
        };

        var ordem = Motor.Padrao().Revisar(foto, Limites.Padrao).Resultados.Select(r => r.Avaliacao.Gravidade).ToList();

        Assert.Equal(Gravidade.Critico, ordem[0]);
        Assert.Equal(Gravidade.Atencao, ordem[1]);
        Assert.Equal(Gravidade.Pulado, ordem[2]);
        Assert.Equal(Gravidade.NaoSeAplica, ordem[^1]);
    }

    [Fact]
    public void Pulado_sem_motivo_nao_existe()
    {
        Assert.Throws<ArgumentException>(() => new Avaliacao(Gravidade.Pulado, "sem motivo"));
        Assert.Throws<ArgumentException>(() => new Avaliacao(Gravidade.Ok, "ok", MotivoDoPulo.SemAdmin));
    }

    private static string Resumo(Revisao revisao, string item) =>
        revisao.Resultados.Single(r => r.Item == item).Avaliacao.Resumo;

    private sealed class Quebrada : IVerificacao
    {
        public string Id => "quebrada";
        public string Titulo => "Quebrada";
        public string OQueE => "";
        public string PorQueImporta => "";
        public string ComoResolver => "";
        public Avaliacao Avaliar(FotoDoSistema foto, Limites limites) => throw new InvalidOperationException("defeito");
    }
}
