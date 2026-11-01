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

    private readonly AmbienteFalso pc = new();
    private readonly AgendadorFalso agendador = new();
    private string? executavel = @"C:\Ferramentas\triage.exe";
    private static readonly FakeRelogio Domingo = new(new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.FromHours(-3)));

    public void Dispose()
    {
        Directory.Delete(pasta, recursive: true);
        pc.Dispose();
    }

    // Por padrão o "PC" é o notebook gravado; foraDoWindows faz todas as fábricas devolverem nulo
    private AcessoAoWindows Windows(Func<ILeitorDoSistema?> leitor, bool foraDoWindows = false) => new(
        () => foraDoWindows ? null : leitor(),
        () => foraDoWindows ? null : pc,
        () => foraDoWindows ? null : agendador,
        Abrir,
        executavel,
        Domingo,
        () => foraDoWindows ? null : cofre,
        endereco =>
        {
            servidor.Enderecos.Add(endereco);
            return servidor;
        });

    private readonly ServidorFalso servidor = new();
    private ICofre cofre = new CofreFalso();

    private sealed class FakeRelogio(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("BRT", agora.Offset, "BRT", "BRT");
    }

    private sealed record Rodada(int Codigo, string Saida, string Erros);

    // Por padrão o "PC" é o notebook gravado; nulo simula rodar fora do Windows
    private Rodada Rodar(string[] args, string entrada = "", bool foraDoWindows = false)
    {
        var saida = new StringWriter();
        var erros = new StringWriter();
        var comandos = new Comandos(new StringReader(entrada), saida, erros, pasta,
            Windows(() => new LeitorGravado(Notebook), foraDoWindows));
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
        var comandos = new Comandos(entrada, saida, new StringWriter(), pasta, Windows(() => new LeitorGravado(Notebook)));

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
    [InlineData("agendar")]
    [InlineData("desagendar")]
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
        var comandos = new Comandos(new StringReader("\n"), saida, erros, pasta, Windows(() => new LeitorQuebrado()));

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

    [Fact]
    public void Corrigir_sem_item_lista_as_correcoes()
    {
        var r = Rodar(["corrigir"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("triage corrigir temporarios", r.Saida);
        Assert.Contains("(segura)", r.Saida);
        Assert.Contains("--seguras", r.Saida);
    }

    [Fact]
    public void Corrigir_um_item_pergunta_e_anota_na_pasta_do_triage()
    {
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp");

        var r = Rodar(["corrigir", "temporarios"], entrada: "s\n");

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.False(File.Exists(lixo));
        Assert.Contains("\"item\":\"temporarios\"", File.ReadAllText(Path.Combine(pasta, "correcoes.jsonl")));
    }

    [Fact]
    public void Corrigir_seguras_pelo_comando()
    {
        pc.Lixeira = new ConteudoDaLixeira(10, 1);

        var r = Rodar(["corrigir", "--seguras"], entrada: "s\n");

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Equal(["esvaziar lixeira"], pc.Chamadas);
    }

    [Fact]
    public void Corrigir_item_desconhecido_e_erro_de_uso_sem_tocar_em_nada()
    {
        var r = Rodar(["corrigir", "registro-do-windows"]);

        Assert.Equal(Comandos.UsoErrado, r.Codigo);
        Assert.Contains("Correção desconhecida: registro-do-windows", r.Erros);
        Assert.Empty(pc.Chamadas);
    }

    [Fact]
    public void Corrigir_fora_do_Windows_explica()
    {
        var r = Rodar(["corrigir", "lixeira"], foraDoWindows: true);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("Corrigir só funciona no Windows.", r.Erros);
    }

    [Fact]
    public void Duplo_clique_nunca_corrige()
    {
        // O Enter do duplo clique fecha a janela; ele nunca pode virar a confirmação de uma correção
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp");
        pc.Lixeira = new ConteudoDaLixeira(10, 1);

        Rodar([], entrada: "s\ns\n");

        Assert.True(File.Exists(lixo));
        Assert.Empty(pc.Chamadas);
    }

    [Fact]
    public void Agendar_cria_a_tarefa_semanal_com_este_exe()
    {
        var r = Rodar(["agendar"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        var xml = Assert.Single(agendador.Tarefas, t => t.Key == TarefaSemanal.Nome).Value;
        Assert.Contains("<Command>C:\\Ferramentas\\triage.exe</Command>", xml);
        Assert.Contains("<Arguments>revisar --sem-abrir</Arguments>", xml);
        Assert.Contains("<StartBoundary>2026-11-02T12:00:00</StartBoundary>", xml); // domingo 15h -> segunda
        Assert.Contains("a próxima em 02/11/2026", r.Saida);
        Assert.Contains("nunca corrige nada", r.Saida);
    }

    [Fact]
    public void Agendar_aceita_o_exe_com_outro_nome()
    {
        executavel = @"C:\Users\ana\Downloads\triage (1).exe";

        var r = Rodar(["agendar"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("triage (1).exe</Command>", agendador.Tarefas[TarefaSemanal.Nome]);
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData("/usr/bin/dotnet")]
    [InlineData(null)]
    public void Agendar_pelo_dotnet_run_e_recusado(string? processo)
    {
        executavel = processo;

        var r = Rodar(["agendar"]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("rode pelo triage.exe", r.Erros);
        Assert.Empty(agendador.Tarefas);
    }

    [Fact]
    public void Desagendar_tira_a_tarefa_e_avisa_quando_nao_havia()
    {
        Rodar(["agendar"]);

        var primeira = Rodar(["desagendar"]);
        var segunda = Rodar(["desagendar"]);

        Assert.Empty(agendador.Tarefas);
        Assert.Contains("removida", primeira.Saida);
        Assert.Contains("Não havia", segunda.Saida);
    }

    private FilaDeEnvios Fila => new(pasta);

    private void Inscrito() => Rodar(["inscrever", "7K2M-9QXA-4TRW", "notebook", "da", "sala"]);

    [Fact]
    public void Inscrever_guarda_a_inscricao_e_a_chave_protegida()
    {
        var r = Rodar(["inscrever", "7K2M-9QXA-4TRW", "notebook", "da", "sala"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("Inscrito como \"notebook da sala\"", r.Saida);
        Assert.Equal([new Uri(Comandos.ServidorPadrao)], servidor.Enderecos);
        var (inscricao, chave) = new ArquivosDaInscricao(pasta, new CofreFalso()).Ler()!.Value;
        Assert.Equal("notebook da sala", inscricao.Apelido);
        Assert.Equal("chave-secreta-da-maquina", chave);
    }

    [Fact]
    public void Inscrever_com_outro_servidor()
    {
        Rodar(["inscrever", "--servidor", "http://localhost:8081", "7K2M-9QXA-4TRW", "pc"]);

        Assert.Equal([new Uri("http://localhost:8081")], servidor.Enderecos);
    }

    [Theory]
    [InlineData("http://triage.andrelagos.dev")] // a chave iria aberta pela rede
    [InlineData("ftp://localhost")]
    [InlineData("nao e endereco")]
    public void Inscrever_recusa_endereco_sem_https(string endereco)
    {
        var r = Rodar(["inscrever", "--servidor", endereco, "7K2M-9QXA-4TRW", "pc"]);

        Assert.Equal(Comandos.UsoErrado, r.Codigo);
        Assert.Empty(servidor.Enderecos);
    }

    [Fact]
    public void Inscrever_de_novo_e_recusado_para_nao_deixar_maquina_orfa()
    {
        Inscrito();

        var r = Rodar(["inscrever", "AAAA-BBBB-CCCC", "outro"]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("já está inscrito como \"notebook da sala\"", r.Erros);
        Assert.Single(servidor.Enderecos);
    }

    [Fact]
    public void Codigo_recusado_mostra_a_mensagem_do_servidor_e_nao_inscreve()
    {
        servidor.ErroNaInscricao = new Grpc.Core.RpcException(new Grpc.Core.Status(
            Grpc.Core.StatusCode.InvalidArgument, "Código inválido, já usado ou vencido."));

        var r = Rodar(["inscrever", "AAAA-BBBB-CCCC", "pc"]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("Código inválido, já usado ou vencido.", r.Erros);
        Assert.False(new ArquivosDaInscricao(pasta, new CofreFalso()).Inscrito);
    }

    [Fact]
    public void Sem_inscricao_o_revisar_nao_manda_nada()
    {
        Rodar(["revisar", "--sem-abrir"]);

        Assert.Empty(servidor.Enderecos);
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public void Inscrito_o_revisar_manda_ao_servidor()
    {
        Inscrito();

        var r = Rodar(["revisar", "--sem-abrir"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("Enviada(s) ao servidor: 1", r.Saida);
        Assert.Single(servidor.Recebidos);
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public void Sem_rede_a_revisao_fica_na_fila_e_sobe_depois()
    {
        // Critério da fase 7: sem rede a revisão fica na fila e sobe depois
        Inscrito();
        servidor.Padrao = new Envio(RespostaDoEnvio.TentarDepois, "sem rede");

        var semRede = Rodar(["revisar", "--sem-abrir"]);
        servidor.Padrao = new Envio(RespostaDoEnvio.Entregue);
        var depois = Rodar(["enviar"]);

        Assert.Equal(Comandos.Sucesso, semRede.Codigo); // a revisão local deu certo
        Assert.Contains("Sem conexão com o servidor agora (sem rede): 1 revisão(ões) na fila", semRede.Saida);
        Assert.Equal(Comandos.Sucesso, depois.Codigo);
        Assert.Single(servidor.Recebidos);
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public void Chave_revogada_avisa_e_deixa_a_fila()
    {
        Inscrito();
        servidor.Padrao = new Envio(RespostaDoEnvio.SemPermissao, "Esta máquina foi revogada pelo administrador.");

        var r = Rodar(["revisar", "--sem-abrir"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("foi revogada", r.Erros);
        Assert.Single(Fila.Pendentes());
    }

    [Fact]
    public void Excecao_no_envio_vira_aviso_e_a_revisao_fica_na_fila()
    {
        // Achado do revisor: sem este teste, trocar o catch do envio não quebrava nada
        Inscrito();
        servidor.ErroNoEnvio = new InvalidOperationException("algo inesperado");

        var r = Rodar(["revisar", "--sem-abrir"]);

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Contains("Não deu para enviar agora (algo inesperado)", r.Saida);
        Assert.Single(Fila.Pendentes());
    }

    [Fact]
    public void Chave_que_nao_abre_mais_aponta_a_saida_em_todos_os_comandos()
    {
        Inscrito();
        cofre = new CofreQuebrado();

        var revisar = Rodar(["revisar", "--sem-abrir"]);
        var enviar = Rodar(["enviar"]);
        var inscrever = Rodar(["inscrever", "AAAA-BBBB-CCCC", "pc"]);
        var desinscrever = Rodar(["desinscrever"]);

        Assert.Equal(Comandos.Sucesso, revisar.Codigo); // a revisão local continua valendo
        Assert.Contains("não abre mais", revisar.Erros);
        Assert.Contains("triage desinscrever", revisar.Erros);
        Assert.Empty(Fila.Pendentes()); // não enche a fila à toa
        Assert.Equal(Comandos.Falha, enviar.Codigo);
        Assert.Contains("triage desinscrever", enviar.Erros);
        Assert.Contains("já está inscrito como \"notebook da sala\"", inscrever.Erros);
        Assert.Equal(Comandos.Sucesso, desinscrever.Codigo); // a saída funciona
    }

    [Theory]
    [InlineData("{ isto não é json")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Inscricao_json_estragado_tambem_aponta_a_saida(string conteudo)
    {
        Inscrito();
        File.WriteAllText(Path.Combine(pasta, "inscricao.json"), conteudo);

        var r = Rodar(["enviar"]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("triage desinscrever", r.Erros);
        Assert.Equal(Comandos.Sucesso, Rodar(["desinscrever"]).Codigo);
    }

    [Fact]
    public void Chave_que_nao_grava_avisa_que_o_codigo_foi_gasto()
    {
        cofre = new CofreQuebrado(tambemNaoGrava: true);

        var r = Rodar(["inscrever", "7K2M-9QXA-4TRW", "pc"]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("O código já foi usado", r.Erros);
        Assert.Contains("revogar a máquina 0193f0c4-0000-7000-8000-000000000001", r.Erros);
    }

    [Fact]
    public void Servidor_sem_valor_e_erro_de_uso_e_nao_vira_apelido()
    {
        var r = Rodar(["inscrever", "7K2M-9QXA-4TRW", "pc", "--servidor"]);

        Assert.Equal(Comandos.UsoErrado, r.Codigo);
        Assert.Contains("Falta o endereço depois de --servidor", r.Erros);
        Assert.Empty(servidor.Enderecos);
    }

    [Fact]
    public void Leitura_gravada_nunca_vai_ao_servidor()
    {
        // Uma gravação não é este PC agora: mandar seria histórico falso
        Inscrito();

        Rodar(["revisar", "--leitura", Notebook, "--sem-abrir"]);

        Assert.Empty(servidor.Recebidos);
        Assert.Empty(Fila.Pendentes());
    }

    [Fact]
    public void Enviar_sem_inscricao_explica()
    {
        var r = Rodar(["enviar"]);

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("não está inscrito", r.Erros);
    }

    [Fact]
    public void Desinscrever_apaga_chave_e_fila_daqui()
    {
        Inscrito();
        servidor.Padrao = new Envio(RespostaDoEnvio.TentarDepois);
        Rodar(["revisar", "--sem-abrir"]);

        var r = Rodar(["desinscrever"]);

        Assert.Contains("1 revisão(ões) que não tinham subido foram descartadas", r.Saida);
        Assert.False(File.Exists(Path.Combine(pasta, "chave")));
        Assert.Empty(Fila.Pendentes());
        Assert.Equal(Comandos.Sucesso, Rodar(["inscrever", "AAAA-BBBB-CCCC", "de novo"]).Codigo);
    }

    [Theory]
    [InlineData("https://triage.andrelagos.dev", true)]
    [InlineData("http://localhost:8081", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    [InlineData("http://triage.andrelagos.dev", false)]
    [InlineData("http://192.168.0.10:8081", false)]
    public void Endereco_aceito_so_com_https_ou_local(string endereco, bool aceito)
    {
        Assert.Equal(aceito, Comandos.EnderecoAceito(endereco, out _));
    }

    private sealed class LeitorQuebrado : ILeitorDoSistema
    {
        public FotoDoSistema Ler() => throw new InvalidOperationException("WMI caiu");
    }
}
