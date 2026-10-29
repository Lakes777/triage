using Triage.Agente;
using Triage.Verificacoes;

namespace Triage.Agente.Tests;

public class SaidaDoWingetTests
{
    // Saída real do `winget upgrade` no notebook (29/10/2026), em pt-BR, com CRLF e duas tabelas
    private static readonly string Notebook =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dados", "winget-upgrade-ptbr.txt"));

    [Fact]
    public void Saida_real_do_notebook_tem_os_12_programas_das_duas_tabelas()
    {
        var programas = SaidaDoWinget.LerTabelas(Notebook)!;

        // 11 na tabela principal + o Discord na de "segmentação explícita" (o winget também diz "12 atualizações")
        Assert.Equal(12, programas.Count);
        Assert.Equal(new ProgramaDesatualizado("Git", "Git.Git", "2.55.0.5", "2.56.0.3"), programas[2]);
        // Nome com espaços e parênteses, e id que ocupa a coluna inteira
        Assert.Equal(new ProgramaDesatualizado("Eclipse Temurin JDK with Hotspot 25.0.4.1+1 (x64)",
            "EclipseAdoptium.Temurin.25.JDK", "25.0.4.101", "25.0.5.8"), programas[1]);
        // Versões que ocupam a coluna inteira
        Assert.Equal(new ProgramaDesatualizado("Spotify", "Spotify.Spotify",
            "1.3.5.3058.g5d40d5480152", "1.3.6.1921.g7086c071a617"), programas[9]);
        Assert.Equal("Microsoft Visual C++ v14 Redistributable (x64) - 14.50.35719", programas[4].Nome);
        Assert.Equal("Microsoft.VCRedist.2015+.x64", programas[4].Id);
        // A segunda tabela tem colunas em outras posições
        Assert.Equal(new ProgramaDesatualizado("Discord", "Discord.Discord", "1.0.9262", "1.0.9266"), programas[11]);
        // A linha de total e a frase da segunda tabela não viram programa
        Assert.DoesNotContain(programas, p => p.Nome.Contains("atualizações") || p.Nome.Contains("pacotes"));
    }

    [Fact]
    public void Saida_real_com_codigo_de_erro_ainda_vale_pela_tabela()
    {
        var lido = SaidaDoWinget.Interpretar(Notebook, unchecked((int)0x8A150044));

        Assert.True(lido.FoiLido);
        Assert.Equal(12, lido.Valor!.Count);
    }

    [Fact]
    public void Mesma_saida_so_com_LF_da_o_mesmo_resultado()
    {
        Assert.Equal(SaidaDoWinget.LerTabelas(Notebook), SaidaDoWinget.LerTabelas(Notebook.Replace("\r\n", "\n")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Nenhuma atualização disponível.\r\n")]
    public void Sem_tabela_e_codigo_zero_e_lista_vazia(string saida)
    {
        Assert.Null(SaidaDoWinget.LerTabelas(saida));

        var lido = SaidaDoWinget.Interpretar(saida, 0);

        Assert.True(lido.FoiLido);
        Assert.Empty(lido.Valor!);
    }

    [Fact]
    public void Sem_tabela_e_codigo_de_nenhuma_atualizacao_e_lista_vazia()
    {
        var lido = SaidaDoWinget.Interpretar("Nenhuma atualização aplicável encontrada.\r\n", SaidaDoWinget.NenhumaAtualizacao);

        Assert.True(lido.FoiLido);
        Assert.Empty(lido.Valor!);
    }

    // Sem rede o winget só escreve a mensagem de erro: isso nunca pode virar "nenhum programa desatualizado"
    [Theory]
    [InlineData("")]
    [InlineData("Falha ao pesquisar na origem: winget\r\nOcorreu um erro inesperado ao executar o comando:\r\n0x80072ee7 : unknown error\r\n")]
    public void Sem_tabela_e_codigo_de_erro_e_sem_conexao(string saida)
    {
        var lido = SaidaDoWinget.Interpretar(saida, unchecked((int)0x80072EE7));

        Assert.False(lido.FoiLido);
        Assert.Equal(MotivoDoPulo.SemConexao, lido.Pulo);
    }

    [Fact]
    public void Restos_da_animacao_de_progresso_sao_ignorados()
    {
        var saida = "   - \r   \\ \r   | \r   / \r\n"
            + "  ██████████████▒▒▒▒▒▒  1.00 MB / 2.00 MB\r  ████████████████████  2.00 MB / 2.00 MB\r\n"
            + "   - \r\n"
            + "\r   \\ \r      \rName           Id              Version  Available Source\r\n"
            + "----------------------------------------------------------------\r\n"
            + "Git            Git.Git         2.55.0.5 2.56.0.3  winget\r\n"
            + "\x1b[0mNode.js LTS    OpenJS.NodeJS   22.1.0   22.2.0    winget\r\n"
            + "2 upgrades available.\r\n";

        var programas = SaidaDoWinget.LerTabelas(saida)!;

        Assert.Equal(
            [
                new ProgramaDesatualizado("Git", "Git.Git", "2.55.0.5", "2.56.0.3"),
                new ProgramaDesatualizado("Node.js LTS", "OpenJS.NodeJS", "22.1.0", "22.2.0"),
            ],
            programas);
    }

    [Fact]
    public void Duas_tabelas_separadas_por_linha_em_branco_somam()
    {
        var saida = """
            Nome  ID      Versão Disponível Origem
            --------------------------------------
            Git   Git.Git 2.55   2.56       winget

            Os pacotes a seguir têm uma atualização disponível, mas exigem uma segmentação explícita para atualização:
            Nome    ID              Versão   Disponível Origem
            ---------------------------------------------------
            Discord Discord.Discord 1.0.9262 1.0.9266   winget
            """;

        var programas = SaidaDoWinget.LerTabelas(saida)!;

        Assert.Equal(["Git.Git", "Discord.Discord"], programas.Select(p => p.Id));
    }

    [Fact]
    public void Linha_que_nao_cabe_nas_colunas_encerra_a_tabela()
    {
        var saida = """
            Nome  ID      Versão Disponível Origem
            --------------------------------------
            Git   Git.Git 2.55   2.56       winget
            1 atualização disponível.
            Linha solta qualquer que não é programa
            """;

        Assert.Single(SaidaDoWinget.LerTabelas(saida)!);
    }

    [Fact]
    public void Tabela_sem_nenhuma_linha_legivel_e_erro_e_nao_tudo_atualizado()
    {
        // Nome com caracteres largos: o winget alinha pela largura na tela, e a linha não cabe nas colunas
        const string saida = "Nome      ID         Versão  Disponível Origem\n"
            + "-----------------------------------------------\n"
            + "微信微信 Tencent.WeChat 3.9.0 3.9.1 winget\n";

        var lido = SaidaDoWinget.Interpretar(saida, 0);

        Assert.False(lido.FoiLido);
        Assert.Equal(MotivoDoPulo.Erro, lido.Pulo);
    }
}
