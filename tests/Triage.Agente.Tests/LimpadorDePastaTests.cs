using Triage.Agente;

namespace Triage.Agente.Tests;

public sealed class LimpadorDePastaTests : IDisposable
{
    private readonly AmbienteFalso pc = new();

    public void Dispose() => pc.Dispose();

    private string Temp => pc.TempDoUsuario;

    [Fact]
    public void Apaga_so_o_que_tem_mais_de_24_horas()
    {
        var velho = pc.Arquivo(Temp, "velho.tmp", 3000);
        var novo = pc.Arquivo(Temp, "instalador-rodando.tmp", 5000, antigo: false);

        var medida = LimpadorDePasta.Medir(Temp, pc.Agora);
        var limpeza = LimpadorDePasta.Limpar(Temp, pc.Agora);

        Assert.Equal(new Limpeza(3000, 1, 0, 1), medida);
        Assert.Equal(new Limpeza(3000, 1, 0, 1), limpeza);
        Assert.False(File.Exists(velho));
        Assert.True(File.Exists(novo));
    }

    [Fact]
    public void Nunca_apaga_a_pasta_raiz_e_tira_subpastas_que_ficaram_vazias()
    {
        var sub = Path.Combine(Temp, "pacote", "interno");
        pc.Arquivo(sub, "a.tmp");
        pc.Arquivo(Path.Combine(Temp, "com-recente"), "novo.tmp", antigo: false);
        foreach (var pasta in new[] { sub, Path.Combine(Temp, "pacote"), Path.Combine(Temp, "com-recente") })
            Directory.SetLastWriteTimeUtc(pasta, pc.Agora.UtcDateTime.AddDays(-3));

        LimpadorDePasta.Limpar(Temp, pc.Agora);

        Assert.True(Directory.Exists(Temp));
        Assert.False(Directory.Exists(Path.Combine(Temp, "pacote")));
        Assert.True(File.Exists(Path.Combine(Temp, "com-recente", "novo.tmp")));
    }

    [Fact]
    public void Contraprova_link_no_Temp_apontando_para_fora_nao_e_seguido()
    {
        // Um link no Temp apontando para Documentos: seguir o link apagaria Documentos
        var documentos = Directory.CreateDirectory(Path.Combine(pc.Raiz, "Documentos")).FullName;
        var importante = pc.Arquivo(documentos, "tcc.docx");
        Directory.CreateSymbolicLink(Path.Combine(Temp, "atalho-pasta"), documentos);
        File.CreateSymbolicLink(Path.Combine(Temp, "atalho-arquivo.docx"), importante);
        pc.Arquivo(Temp, "lixo.tmp");

        var limpeza = LimpadorDePasta.Limpar(Temp, pc.Agora);

        Assert.True(File.Exists(importante));
        Assert.Equal(1, limpeza.Arquivos); // só o lixo.tmp
        Assert.True(Directory.Exists(Path.Combine(Temp, "atalho-pasta"))); // nem o link é apagado
    }

    [Fact]
    public void Conferencia_antes_de_apagar_recusa_pasta_que_virou_link_e_caminho_de_fora()
    {
        var documentos = Directory.CreateDirectory(Path.Combine(pc.Raiz, "Documentos")).FullName;
        var importante = pc.Arquivo(documentos, "tcc.docx");
        var pastaNormal = Directory.CreateDirectory(Path.Combine(Temp, "pacote")).FullName;
        var virouLink = Path.Combine(Temp, "trocada");
        Directory.CreateSymbolicLink(virouLink, documentos);

        Assert.True(LimpadorDePasta.SemLinkAteARaiz(Temp, Path.Combine(pastaNormal, "a.tmp")));
        Assert.True(LimpadorDePasta.SemLinkAteARaiz(Temp, Path.Combine(Temp, "a.tmp")));
        // Uma pasta trocada por link entre a enumeração e o apagar (a corrida que o revisor apontou)
        Assert.False(LimpadorDePasta.SemLinkAteARaiz(Temp, Path.Combine(virouLink, "tcc.docx")));
        Assert.False(LimpadorDePasta.SemLinkAteARaiz(Temp, importante));
        Assert.False(LimpadorDePasta.SemLinkAteARaiz(Temp, Path.Combine(Temp + "-outra", "a.tmp")));
        Assert.False(LimpadorDePasta.SemLinkAteARaiz(Temp, Path.Combine(Temp, "..", "a.tmp")));
    }

    [Fact]
    public void Pasta_que_nao_existe_e_nada_a_fazer()
    {
        var sumida = Path.Combine(pc.Raiz, "nao-existe", "Temp");

        Assert.Equal(Limpeza.Nada, LimpadorDePasta.Medir(sumida, pc.Agora));
        Assert.Equal(Limpeza.Nada, LimpadorDePasta.Limpar(sumida, pc.Agora));
    }

    [Theory]
    [InlineData(new[] { "Temp" }, "C:/Users/ana/AppData/Local/Temp", true)]
    [InlineData(new[] { "Temp" }, "C:/Users/ana/AppData/Local/temp/", true)]
    [InlineData(new[] { "Temp" }, "C:/Users/ana", false)]
    [InlineData(new[] { "Temp" }, "C:/Temp/../Users/ana", false)]
    [InlineData(new[] { "Temp" }, "Temp", false)]
    [InlineData(new[] { "Temp" }, "", false)]
    [InlineData(new[] { "Windows", "Temp" }, "C:/Windows/Temp", true)]
    [InlineData(new[] { "Windows", "Temp" }, "C:/Users/ana/AppData/Local/Temp", false)]
    [InlineData(new[] { "SoftwareDistribution", "Download" }, "C:/Windows/SoftwareDistribution/Download", true)]
    [InlineData(new[] { "SoftwareDistribution", "Download" }, "C:/Users/ana/Downloads", false)]
    public void Pasta_so_confere_com_o_nome_esperado(string[] final, string caminho, bool confere)
    {
        // No Linux "C:/..." não é absoluto: troca pela raiz daqui para testar só a regra do nome
        var real = caminho.StartsWith("C:/", StringComparison.Ordinal) ? "/" + caminho[3..] : caminho;
        Assert.Equal(confere, Correcoes.PastaConfere(real, final));
    }
}
