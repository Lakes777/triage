using Triage.Agente;
using Triage.Contratos;
using Triage.Verificacoes;

namespace Triage.Agente.Tests;

public sealed class ComandosTests : IDisposable
{
    private static readonly string Notebook = Path.Combine(AppContext.BaseDirectory, "dados", "notebook-sem-admin.json");
    private readonly string pasta = Directory.CreateTempSubdirectory("triage-testes-").FullName;

    public void Dispose() => Directory.Delete(pasta, recursive: true);

    private sealed record Rodada(int Codigo, string Saida, string Erros);

    // Por padrão o "PC" é o notebook gravado; nulo simula rodar fora do Windows
    private Rodada Rodar(string[] args, string entrada = "", bool foraDoWindows = false)
    {
        var saida = new StringWriter();
        var erros = new StringWriter();
        var comandos = new Comandos(new StringReader(entrada), saida, erros,
            () => foraDoWindows ? null : new LeitorGravado(Notebook), pasta);
        return new Rodada(comandos.Executar(args), saida.ToString(), erros.ToString());
    }

    [Fact]
    public void Versao_mostra_a_versao_do_triage()
    {
        var r = Rodar(["--versao"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Equal($"triage {VersaoDoTriage.Atual}", r.Saida.Trim());
        Assert.Empty(r.Erros);
    }

    [Fact]
    public void Ajuda_mostra_o_uso()
    {
        var r = Rodar(["--ajuda"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("triage revisar", r.Saida);
    }

    [Fact]
    public void Comando_desconhecido_avisa_e_devolve_erro_de_uso()
    {
        var r = Rodar(["formatar", "C:"]);

        Assert.Equal(Comandos.UsoErrado, r.Codigo);
        Assert.Empty(r.Saida);
        Assert.Contains("Comando desconhecido: formatar C:", r.Erros);
    }

    [Fact]
    public void Revisar_mostra_os_itens_e_a_contagem()
    {
        var r = Rodar(["revisar"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("Rodando sem admin", r.Saida);
        Assert.Contains("[OK           ] Saúde da bateria: 88% da capacidade original", r.Saida);
        Assert.Contains("0 crítico · 0 atenção · 7 ok · 0 pulado", r.Saida);
    }

    [Fact]
    public void Sem_argumento_revisa_e_espera_Enter_para_o_duplo_clique()
    {
        var entrada = new StringReader("\n");
        var saida = new StringWriter();
        var comandos = new Comandos(entrada, saida, new StringWriter(), () => new LeitorGravado(Notebook), pasta);

        var codigo = comandos.Executar([]);

        Assert.Equal(Comandos.Sucesso, codigo);
        Assert.Contains("Saúde dos discos", saida.ToString());
        Assert.EndsWith("Aperte Enter para fechar." + Environment.NewLine, saida.ToString());
        Assert.Equal(-1, entrada.Peek()); // o Enter foi consumido
    }

    [Fact]
    public void Revisar_uma_leitura_gravada_funciona_fora_do_Windows()
    {
        var r = Rodar(["revisar", "--leitura", Notebook], foraDoWindows: true);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("Espaço livre", r.Saida);
    }

    [Theory]
    [InlineData("revisar")]
    [InlineData("gravar", "x.json")]
    public void Ler_este_PC_fora_do_Windows_explica_e_falha(params string[] args)
    {
        var r = Rodar(args, foraDoWindows: true);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("só funciona no Windows", r.Erros);
    }

    [Fact]
    public void Gravar_escreve_uma_leitura_que_da_para_revisar_depois()
    {
        var arquivo = Path.Combine(pasta, "leitura.json");

        var r = Rodar(["gravar", arquivo]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Equal(Gravacao.ParaJson(new LeitorGravado(Notebook).Ler()), File.ReadAllText(arquivo));
    }

    [Fact]
    public void Limites_do_usuario_mudam_a_revisao()
    {
        // O notebook tem C: com 54% livre: com a atenção em 60%, o espaço vira atenção
        File.WriteAllText(Path.Combine(pasta, "limites.json"), """{ "espacoLivreAtencaoPorcento": 60 }""");

        var r = Rodar(["revisar"]);

        Assert.Contains("[ATENÇÃO      ] Espaço livre", r.Saida);
        Assert.Contains("Como resolver:", r.Saida);
    }

    [Fact]
    public void Limites_com_erro_param_a_revisao_com_aviso()
    {
        File.WriteAllText(Path.Combine(pasta, "limites.json"), "{ isto não é json");

        var r = Rodar(["revisar"]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("limites.json tem um erro", r.Erros);
        Assert.Empty(r.Saida);
    }

    [Theory]
    [InlineData("0.1.0+9c3e94b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7", "0.1.0+9c3e94b")]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+9c3e", "0.1.0+9c3e")]
    public void Versao_fica_com_o_commit_curto(string completa, string esperada)
    {
        Assert.Equal(esperada, VersaoDoTriage.Encurtar(completa));
    }

    [Fact]
    public void Leitura_que_nao_existe_vira_mensagem_e_nao_stack_trace()
    {
        var r = Rodar(["revisar", "--leitura", Path.Combine(pasta, "nao-existe.json")]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.StartsWith("Erro: ", r.Erros);
        Assert.DoesNotContain("   at ", r.Erros);
    }

    [Fact]
    public void Erro_no_duplo_clique_ainda_espera_o_Enter()
    {
        var saida = new StringWriter();
        var erros = new StringWriter();
        var comandos = new Comandos(new StringReader("\n"), saida, erros, () => new LeitorQuebrado(), pasta);

        var codigo = comandos.Executar([]);

        Assert.Equal(Comandos.Falha, codigo);
        Assert.Contains("Erro: WMI caiu", erros.ToString());
        Assert.Contains("Aperte Enter para fechar.", saida.ToString());
    }

    [Fact]
    public void Limites_com_chave_errada_sao_recusados()
    {
        File.WriteAllText(Path.Combine(pasta, "limites.json"), """{ "espacoLivreAtencaoPorcentu": 60 }""");

        var r = Rodar(["revisar"]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("espacoLivreAtencaoPorcentu", r.Erros);
    }

    private sealed class LeitorQuebrado : ILeitorDoSistema
    {
        public FotoDoSistema Ler() => throw new InvalidOperationException("WMI caiu");
    }
}
