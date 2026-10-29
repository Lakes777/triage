using Triage.Agente;

namespace Triage.Agente.Tests;

/// <summary>A tradução dos números crus do Windows, testada no Linux com valores de verdade.</summary>
public class CodigosDoWindowsTests
{
    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0x8D, 0x41, 0x5B }, false)]   // desligado no Gerenciador de Tarefas
    [InlineData(new byte[] { 0x01 }, false)]
    public void Inicializacao_desligada_pelo_primeiro_byte_impar(byte[] aprovacao, bool ligada)
    {
        Assert.Equal(ligada, CodigosDoWindows.InicializacaoLigada(aprovacao));
    }

    [Fact]
    public void Inicializacao_sem_aprovacao_gravada_esta_ligada()
    {
        Assert.True(CodigosDoWindows.InicializacaoLigada(null));
        Assert.True(CodigosDoWindows.InicializacaoLigada([]));
    }

    [Theory]
    [InlineData("OneDrive", "OneDrive")]
    [InlineData(@"C:\Users\fulano\AppData\Local\app.exe", "app.exe")]
    [InlineData("C:/Users/fulano/app/", "app")]
    [InlineData(@"\", "sem nome")]
    [InlineData("  Spotify  ", "Spotify")]
    public void Nome_da_inicializacao_nunca_leva_caminho(string nome, string esperado)
    {
        Assert.Equal(esperado, CodigosDoWindows.SemCaminho(nome));
    }

    [Theory]
    [InlineData(null, false)]   // sem código não é erro inventado (e não vira 0 por acidente)
    [InlineData(0, false)]
    [InlineData(22, false)]     // desativado pelo usuário de propósito
    [InlineData(45, false)]     // aparelho desconectado agora
    [InlineData(28, true)]      // driver não instalado
    [InlineData(10, true)]      // não iniciou
    public void Driver_com_erro_pelo_codigo(int? codigo, bool comErro)
    {
        Assert.Equal(comErro, CodigosDoWindows.DriverComErro(codigo));
    }

    [Theory]
    [InlineData(397568u, true, true)]    // 0x61100: Defender ligado e em dia (o notebook dele, 29/10)
    [InlineData(393472u, false, true)]   // 0x60100: Defender desligado
    [InlineData(397584u, true, false)]   // 0x61110: ligado com definições velhas
    [InlineData(266240u, true, true)]    // 0x41000: antivírus de outra marca ligado
    [InlineData(262144u, false, true)]   // 0x40000: de outra marca desligado
    public void Estado_do_produto_da_Central_de_Seguranca(uint estado, bool ativo, bool atualizado)
    {
        var produto = CodigosDoWindows.ProdutoDaCentral("Produto", estado);

        Assert.Equal(ativo, produto.Ativo);
        Assert.Equal(atualizado, produto.Atualizado);
    }

    [Fact]
    public void Produto_sem_nome_ganha_um_nome_padrao()
    {
        Assert.Equal("sem nome", CodigosDoWindows.ProdutoDaCentral(null, 397568).Nome);
        Assert.Equal("sem nome", CodigosDoWindows.ProdutoDaCentral("  ", 397568).Nome);
    }
}
