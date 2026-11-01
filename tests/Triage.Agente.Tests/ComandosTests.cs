using Triage.Agente;
using Triage.Contratos;
using Triage.Verificacoes;

namespace Triage.Agente.Tests;

public sealed class ComandosTests : IDisposable
{
    private static readonly string Notebook = Path.Combine(AppContext.BaseDirectory, "dados", "notebook-sem-admin.json");
    private readonly string pasta = Directory.CreateTempSubdirectory("triage-testes-").FullName;
    private readonly List<string> abertos = [];
    private bool navegadorFunciona = true;

    private bool Abrir(string arquivo)
    {
        abertos.Add(arquivo);
        return navegadorFunciona;
    }

    private string PastaDosRelatorios => Path.Combine(pasta, "relatorios");

    public void Dispose() => Directory.Delete(pasta, recursive: true);

    private sealed record Rodada(int Codigo, string Saida, string Erros);

    // Por padrão o "PC" é o notebook gravado; nulo simula rodar fora do Windows
    private Rodada Rodar(string[] args, string entrada = "", bool foraDoWindows = false)
    {
        var saida = new StringWriter();
        var erros = new StringWriter();
        var comandos = new Comandos(new StringReader(entrada), saida, erros,
            () => foraDoWindows ? null : new LeitorGravado(Notebook), pasta, Abrir);
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
        Assert.Contains("0 crítico · 0 atenção · 7 ok", r.Saida);
    }

    [Fact]
    public void Sem_argumento_revisa_e_espera_Enter_para_o_duplo_clique()
    {
        var entrada = new StringReader("\n");
        var saida = new StringWriter();
        var comandos = new Comandos(entrada, saida, new StringWriter(), () => new LeitorGravado(Notebook), pasta, Abrir);

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
        var comandos = new Comandos(new StringReader("\n"), saida, erros, () => new LeitorQuebrado(), pasta, Abrir);

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

    [Fact]
    public void Revisar_guarda_o_relatorio_e_abre_no_navegador()
    {
        var r = Rodar(["revisar"]);

        var relatorio = Assert.Single(Directory.GetFiles(PastaDosRelatorios));
        Assert.Equal([relatorio], abertos);
        Assert.Contains($"Relatório completo: {relatorio}", r.Saida);
        Assert.Matches(@"revisao-\d{4}-\d{2}-\d{2}-\d{6}\.html$", relatorio);
        Assert.Contains("Saúde dos discos", File.ReadAllText(relatorio));
    }

    [Fact]
    public void Sem_abrir_gera_o_relatorio_mas_nao_abre_o_navegador()
    {
        // Para o CI e para a revisão agendada da fase 5, que não pode abrir janela no meio do dia
        var r = Rodar(["revisar", "--sem-abrir"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Single(Directory.GetFiles(PastaDosRelatorios));
        Assert.Empty(abertos);
    }

    [Fact]
    public void Opcoes_do_revisar_valem_em_qualquer_ordem()
    {
        var r = Rodar(["revisar", "--sem-abrir", "--leitura", Notebook], foraDoWindows: true);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Empty(abertos);
    }

    [Fact]
    public void Navegador_que_nao_abre_vira_aviso_e_a_revisao_continua_valendo()
    {
        navegadorFunciona = false;

        var r = Rodar(["revisar"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("Não deu para abrir o navegador: abra o arquivo acima.", r.Saida);
    }

    [Theory]
    [InlineData("Opção desconhecida: --abrir", "revisar", "--abrir")]
    [InlineData("Falta o arquivo depois de --leitura", "revisar", "--leitura")]
    [InlineData("Falta o arquivo depois de --leitura", "revisar", "--leitura", "--sem-abrir")]
    [InlineData("Opção repetida: --sem-abrir", "revisar", "--sem-abrir", "--sem-abrir")]
    [InlineData("Opção repetida: --leitura", "revisar", "--leitura", "a.json", "--leitura", "b.json")]
    public void Opcao_errada_no_revisar_e_erro_de_uso(string mensagem, params string[] args)
    {
        var r = Rodar(args);

        Assert.Equal(Comandos.UsoErrado, r.Codigo);
        Assert.StartsWith(mensagem, r.Erros);
        Assert.False(Directory.Exists(PastaDosRelatorios));
    }

    [Fact]
    public void Guarda_so_os_relatorios_mais_recentes()
    {
        Directory.CreateDirectory(PastaDosRelatorios);
        var antigos = Enumerable.Range(1, PastaDeRelatorios.Guardados)
            .Select(i => Path.Combine(PastaDosRelatorios, $"revisao-2020-01-{i:00}-120000.html")).ToList();
        antigos.ForEach(a => File.WriteAllText(a, "antigo"));
        var outro = Path.Combine(PastaDosRelatorios, "anotacoes.txt");
        File.WriteAllText(outro, "do usuário");

        Rodar(["revisar", "--sem-abrir"]);

        var ficaram = Directory.GetFiles(PastaDosRelatorios, "revisao-*.html");
        Assert.Equal(PastaDeRelatorios.Guardados, ficaram.Length);
        Assert.False(File.Exists(antigos[0])); // só o mais antigo saiu
        Assert.True(File.Exists(antigos[1]));
        Assert.True(File.Exists(outro)); // o que não é relatório do Triage nunca é apagado
    }

    [Fact]
    public void Leitura_antiga_nao_apaga_o_relatorio_que_acabou_de_sair()
    {
        // Achado do revisor: a leitura gravada é de 29/10, os relatórios na pasta são de 30/10; o novo sai com o nome
        // mais "velho" e era apagado na hora, deixando um caminho que não existe na tela e no navegador
        Directory.CreateDirectory(PastaDosRelatorios);
        var novos = Enumerable.Range(0, PastaDeRelatorios.Guardados)
            .Select(i => Path.Combine(PastaDosRelatorios, $"revisao-2026-10-30-{i:00}0000.html")).ToList();
        novos.ForEach(a => File.WriteAllText(a, "mais novo"));

        var r = Rodar(["revisar", "--leitura", Notebook]);

        var relatorio = Assert.Single(abertos);
        Assert.True(File.Exists(relatorio));
        Assert.Contains($"Relatório completo: {relatorio}", r.Saida);
        Assert.Equal(PastaDeRelatorios.Guardados, Directory.GetFiles(PastaDosRelatorios, "revisao-*.html").Length);
        Assert.False(File.Exists(novos[0])); // saiu o mais antigo dos outros
    }

    [Fact]
    public void Relatorio_antigo_que_nao_apaga_e_contado_e_os_outros_saem()
    {
        Directory.CreateDirectory(PastaDosRelatorios);
        var antigos = Enumerable.Range(1, PastaDeRelatorios.Guardados + 2)
            .Select(i => Path.Combine(PastaDosRelatorios, $"revisao-2020-01-{i:00}-120000.html")).ToList();
        antigos.ForEach(a => File.WriteAllText(a, "antigo"));
        var atual = antigos[^1];

        // O primeiro está travado (antivírus, somente leitura): o segundo sai mesmo assim
        var falhas = PastaDeRelatorios.ApagarAntigos(PastaDosRelatorios, atual,
            a => { if (a == antigos[0]) throw new IOException("em uso"); File.Delete(a); });

        Assert.Equal(1, falhas);
        Assert.True(File.Exists(antigos[0]));
        Assert.False(File.Exists(antigos[1]));
        Assert.True(File.Exists(antigos[2]));
        Assert.True(File.Exists(atual));
    }

    private sealed class LeitorQuebrado : ILeitorDoSistema
    {
        public FotoDoSistema Ler() => throw new InvalidOperationException("WMI caiu");
    }
}
