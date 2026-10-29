using System.Text.RegularExpressions;
using Triage.Verificacoes;

namespace Triage.Verificacoes.Tests;

public class GravacaoTests
{
    [Fact]
    public void Foto_gravada_e_lida_de_volta_fica_igual()
    {
        var original = Fotos.NotebookSemAdmin();

        var json = Gravacao.ParaJson(original);
        var lida = Gravacao.DeJson(json);

        // Records com listas não comparam por conteúdo: o JSON de novo é a comparação justa
        Assert.Equal(json, Gravacao.ParaJson(lida));
        Assert.DoesNotContain("foiLido", json);
    }

    [Fact]
    public void Leituras_gravadas_nao_tem_caminho_nem_nome_de_usuario()
    {
        foreach (var arquivo in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "dados"), "*.json"))
        {
            var json = File.ReadAllText(arquivo);
            // Caminho do Windows no JSON aparece como "C:\\..." (barra escapada)
            Assert.DoesNotMatch(new Regex(@"[A-Za-z]:\\\\"), json);
            Assert.DoesNotContain("Users", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Limites_do_usuario_trocam_so_o_que_ele_citou()
    {
        var limites = Limites.DeJson("""{ "espacoLivreAtencaoPorcento": 25 }""");

        Assert.Equal(25, limites.EspacoLivreAtencaoPorcento);
        Assert.Equal(Limites.Padrao.EspacoLivreCriticoPorcento, limites.EspacoLivreCriticoPorcento);
        Assert.Equal(Limites.Padrao.BateriaAtencaoPorcento, limites.BateriaAtencaoPorcento);
    }
}
