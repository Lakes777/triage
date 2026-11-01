using Triage.Verificacoes;

namespace Triage.Servidor.Tests;

public sealed class HistoricoTests
{
    private static Dictionary<string, string> G(params (string Item, string Gravidade)[] itens) =>
        itens.ToDictionary(i => i.Item, i => i.Gravidade);

    [Theory]
    [InlineData("ok", "atencao", true)]
    [InlineData("atencao", "critico", true)]
    [InlineData("ok", "critico", true)]
    [InlineData("atencao", "ok", false)] // melhora não avisa (vai no resumo, P19)
    [InlineData("critico", "critico", false)]
    [InlineData("ok", "pulado", false)]
    [InlineData("pulado", "critico", false)] // sem um "antes" comparável, não dá para dizer que piorou
    [InlineData("nao_se_aplica", "critico", false)]
    public void Piora_so_entre_gravidades_comparaveis(string antes, string depois, bool avisa)
    {
        var pioras = Historico.Pioras(G(("bateria", antes)), G(("bateria", depois)));

        Assert.Equal(avisa, pioras.Count == 1);
    }

    [Fact]
    public void Item_novo_nao_e_piora()
    {
        Assert.Empty(Historico.Pioras(G(), G(("telas-azuis", "critico"))));
    }

    private static string Foto(params (string Modelo, long Tamanho, int? Desgaste)[] discos) =>
        Gravacao.ParaJson(new FotoDoSistema(DateTimeOffset.UtcNow, true,
            Lido.Ok<IReadOnlyList<Volume>>([]),
            Lido.Ok<IReadOnlyList<Disco>>(discos.Select(d => new Disco(d.Modelo, TipoDeDisco.Nvme, d.Tamanho, SaudeDoDisco.Saudavel, d.Desgaste)).ToList()),
            Lido.Ok(new Pasta(0, 0)), Lido.Ok(new Pasta(0, 0)), Lido.Ok(new Pasta(0, 0)),
            Lido.Ok(new ConteudoDaLixeira(0, 0)), Lido.Ok(DateTimeOffset.UtcNow), Lido.Ok<Bateria?>(null)));

    [Theory]
    [InlineData(40, 43, true)]
    [InlineData(40, 42, false)]
    [InlineData(40, 39, false)]
    public void Desgaste_do_SSD_subindo_tres_pontos_avisa(int antes, int depois, bool avisa)
    {
        var pioras = Historico.DesgasteSubindo(Foto(("SSD A", 512, antes)), Foto(("SSD A", 512, depois)));

        Assert.Equal(avisa, pioras.Count == 1);
        if (avisa)
            Assert.Equal(new Piora("desgaste-ssd", $"{antes}%", $"{depois}%", "SSD A"), pioras[0]);
    }

    [Fact]
    public void Desgaste_so_compara_o_mesmo_disco_e_ignora_o_que_nao_le()
    {
        // Disco trocado (outro modelo ou tamanho), desgaste não lido (sem admin) e foto estragada: nada
        Assert.Empty(Historico.DesgasteSubindo(Foto(("SSD A", 512, 10)), Foto(("SSD B", 512, 60))));
        Assert.Empty(Historico.DesgasteSubindo(Foto(("SSD A", 512, 10)), Foto(("SSD A", 1024, 60))));
        Assert.Empty(Historico.DesgasteSubindo(Foto(("SSD A", 512, null)), Foto(("SSD A", 512, 60))));
        Assert.Empty(Historico.DesgasteSubindo(Foto(("SSD A", 512, 10)), Foto(("SSD A", 512, null))));
        Assert.Empty(Historico.DesgasteSubindo(null, Foto(("SSD A", 512, 60))));
        Assert.Empty(Historico.DesgasteSubindo("{ estragada", Foto(("SSD A", 512, 60))));
    }
}
