using Triage.Agente;
using Triage.Verificacoes;

namespace Triage.Agente.Tests;

public sealed class CorrecoesTests : IDisposable
{
    private readonly AmbienteFalso pc = new();
    private RegistroDeCorrecoes Registro => new(Path.Combine(pc.Raiz, "correcoes.jsonl"));

    public void Dispose() => pc.Dispose();

    private sealed record Rodada(int Codigo, string Saida, string Erros);

    private Rodada Rodar(Func<ExecutorDeCorrecoes, int> acao, string resposta)
    {
        var saida = new StringWriter();
        var erros = new StringWriter();
        var codigo = acao(new ExecutorDeCorrecoes(new StringReader(resposta), saida, erros, pc, Registro));
        return new Rodada(codigo, saida.ToString(), erros.ToString());
    }

    private Rodada Corrigir(string item, string resposta) => Rodar(e => e.Corrigir(Correcoes.Achar(item)!), resposta);

    [Theory]
    [InlineData("")] // só Enter
    [InlineData("n")]
    [InlineData("não")]
    [InlineData("sss")]
    [InlineData("yes")]
    public void Contraprova_sem_confirmar_nada_e_apagado(string resposta)
    {
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp");
        pc.Lixeira = new ConteudoDaLixeira(5000, 3);

        var temp = Corrigir("temporarios", resposta + "\n");
        var lixeira = Corrigir("lixeira", resposta + "\n");

        Assert.True(File.Exists(lixo));
        Assert.Empty(pc.Chamadas);
        Assert.Contains("Cancelado: nada foi alterado.", temp.Saida);
        Assert.Contains("Cancelado: nada foi alterado.", lixeira.Saida);
        Assert.All(Registro.Ler(), e => Assert.Equal(Decisao.Recusada, e.Decisao));
    }

    [Fact]
    public void Contraprova_entrada_fechada_rodando_sem_terminal_tambem_cancela()
    {
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp");

        var r = Corrigir("temporarios", resposta: "");

        Assert.True(File.Exists(lixo));
        Assert.Equal(Comandos.Sucesso, r.Codigo);
    }

    [Theory]
    [InlineData("s")]
    [InlineData("S")]
    [InlineData(" sim ")]
    public void Confirmando_limpa_e_mostra_a_pergunta_antes(string resposta)
    {
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp", 2048);

        var r = Corrigir("temporarios", resposta + "\n");

        Assert.False(File.Exists(lixo));
        Assert.Equal(Comandos.Sucesso, r.Codigo);
        var pergunta = r.Saida.IndexOf("Confirma apagar 2 KB em 1 arquivo(s) dos temporários?", StringComparison.Ordinal);
        var feito = r.Saida.IndexOf("Arquivos temporários: liberou 2 KB em 1 arquivo(s).", StringComparison.Ordinal);
        Assert.InRange(pergunta, 0, feito);
    }

    [Fact]
    public void Sem_admin_os_temporarios_do_Windows_ficam()
    {
        var doUsuario = pc.Arquivo(pc.TempDoUsuario, "u.tmp");
        var doWindows = pc.Arquivo(pc.TempDoWindows, "w.tmp");

        var r = Corrigir("temporarios", "s\n");

        Assert.False(File.Exists(doUsuario));
        Assert.True(File.Exists(doWindows));
        Assert.Contains("a pasta do Windows só é limpa rodando como administrador", r.Saida);
    }

    [Fact]
    public void Com_admin_limpa_as_duas_pastas_de_temporarios()
    {
        pc.Administrador = true;
        var doUsuario = pc.Arquivo(pc.TempDoUsuario, "u.tmp");
        var doWindows = pc.Arquivo(pc.TempDoWindows, "w.tmp");

        var r = Corrigir("temporarios", "s\n");

        Assert.False(File.Exists(doUsuario));
        Assert.False(File.Exists(doWindows));
        Assert.Contains("nos temporários do usuário e temporários do Windows", r.Saida);
    }

    [Fact]
    public void Contraprova_TEMP_apontando_para_a_pasta_do_usuario_nao_apaga_nada()
    {
        // Alguém trocou a variável TEMP para a pasta do usuário: o "Temp" da correção seria a pasta inteira
        var pastaDoUsuario = Path.Combine(pc.Raiz, "Usuario");
        var foto = pc.Arquivo(pastaDoUsuario, "foto-antiga.jpg");
        pc.TempDoUsuario = pastaDoUsuario;

        var r = Corrigir("temporarios", "s\n");

        Assert.True(File.Exists(foto));
        Assert.Contains("fora do padrão", r.Saida);
    }

    [Fact]
    public void Nada_para_limpar_nao_pergunta()
    {
        pc.Arquivo(pc.TempDoUsuario, "recente.tmp", antigo: false);

        var r = Corrigir("temporarios", "s\n");

        Assert.DoesNotContain("Confirma", r.Saida);
        Assert.Contains("Nada para apagar nos temporários com mais de 24 h", r.Saida);
        Assert.Equal(Decisao.NadaAFazer, Assert.Single(Registro.Ler()).Decisao);
    }

    [Fact]
    public void Lixeira_avisa_que_e_para_sempre_e_esvazia_confirmando()
    {
        pc.Lixeira = new ConteudoDaLixeira(2 * 1024 * 1024, 4);

        var r = Corrigir("lixeira", "s\n");

        Assert.Contains("apagados para sempre", r.Saida);
        Assert.Equal(["esvaziar lixeira"], pc.Chamadas);
        Assert.Contains("Lixeira: liberou 2 MB em 4 item(ns).", r.Saida);
    }

    [Fact]
    public void Cache_do_Windows_Update_sem_admin_e_recusado_sem_parar_o_servico()
    {
        pc.Arquivo(pc.CacheDoWindowsUpdate, "kb.cab");

        var r = Corrigir("cache-windows-update", "s\n");

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Contains("Precisa de administrador", r.Saida);
        Assert.Empty(pc.Chamadas);
    }

    [Fact]
    public void Cache_do_Windows_Update_para_o_servico_limpa_e_religa()
    {
        pc.Administrador = true;
        var cab = pc.Arquivo(pc.CacheDoWindowsUpdate, "kb.cab");

        var r = Corrigir("cache-windows-update", "s\n");

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.False(File.Exists(cab));
        Assert.Equal(["parar wuauserv", "iniciar wuauserv"], pc.Chamadas);
        Assert.True(pc.ServicoRodando);
    }

    [Fact]
    public void Contraprova_servico_que_demora_a_parar_e_religado_mesmo_assim()
    {
        // Achado do revisor: o parar ficava fora do try, e um timeout deixava o Windows Update desligado
        pc.Administrador = true;
        pc.ServicoNaoPara = true;
        var cab = pc.Arquivo(pc.CacheDoWindowsUpdate, "kb.cab");

        var r = Corrigir("cache-windows-update", "s\n");

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.Equal(["parar wuauserv", "iniciar wuauserv"], pc.Chamadas);
        Assert.True(File.Exists(cab));
        Assert.Contains("não parou em 60 s", r.Erros);
    }

    [Fact]
    public void Cache_com_o_servico_ja_parado_nao_liga_o_servico()
    {
        pc.Administrador = true;
        pc.ServicoRodando = false;
        pc.Arquivo(pc.CacheDoWindowsUpdate, "kb.cab");

        Corrigir("cache-windows-update", "s\n");

        Assert.Equal(["parar wuauserv"], pc.Chamadas);
        Assert.False(pc.ServicoRodando);
    }

    [Fact]
    public void TEMP_igual_ao_do_Windows_com_admin_conta_a_pasta_uma_vez_so()
    {
        pc.Administrador = true;
        pc.TempDoUsuario = pc.TempDoWindows;
        pc.Arquivo(pc.TempDoWindows, "w.tmp", 1024);

        var r = Corrigir("temporarios", "n\n");

        Assert.Contains("1 KB em 1 arquivo(s)", r.Saida);
        Assert.DoesNotContain("2 arquivo(s)", r.Saida);
    }

    [Fact]
    public void Lixeira_que_nao_le_nao_impede_os_temporarios_no_seguras()
    {
        pc.LeituraDaLixeiraQuebrada = true;
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp");

        var r = Rodar(e => e.CorrigirSeguras(), "s\n");

        Assert.False(File.Exists(lixo));
        Assert.Contains("Lixeira: erro ao verificar", r.Erros);
        var registro = Registro.Ler();
        Assert.Contains(registro, e => e.Item == "lixeira" && e.Decisao == Decisao.Erro && e.Resumo == "IOException (0x80131620)");
        Assert.Contains(registro, e => e.Item == "temporarios" && e.Decisao == Decisao.Aplicada);
        Assert.DoesNotContain("andre", File.ReadAllText(Registro.Arquivo));
    }

    [Fact]
    public void Seguras_e_so_a_lista_fixada()
    {
        // Entrar na lista = uma pergunta só para tudo (P26): mudar isto exige motivo escrito no SPEC
        Assert.Equal(["temporarios", "lixeira"], Correcoes.Todas.Where(c => c.Segura).Select(c => c.Item));
    }

    [Fact]
    public void Seguras_mostra_a_lista_e_pergunta_uma_vez_so()
    {
        pc.Administrador = true;
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp");
        var cab = pc.Arquivo(pc.CacheDoWindowsUpdate, "kb.cab");
        pc.Lixeira = new ConteudoDaLixeira(1000, 1);

        var r = Rodar(e => e.CorrigirSeguras(), "s\n"); // um "s" só: uma segunda pergunta leria o fim da entrada

        Assert.Equal(Comandos.Sucesso, r.Codigo);
        Assert.Equal(1, r.Saida.Split("Confirma").Length - 1);
        Assert.Contains("Confirma as 2 correções?", r.Saida);
        Assert.False(File.Exists(lixo));
        Assert.Equal(["esvaziar lixeira"], pc.Chamadas); // o cache do Windows Update não é seguro
        Assert.True(File.Exists(cab));
    }

    [Fact]
    public void Seguras_recusada_nao_faz_nenhuma()
    {
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp");
        pc.Lixeira = new ConteudoDaLixeira(1000, 1);

        var r = Rodar(e => e.CorrigirSeguras(), "n\n");

        Assert.True(File.Exists(lixo));
        Assert.Empty(pc.Chamadas);
        Assert.Equal(2, Registro.Ler().Count(e => e.Decisao == Decisao.Recusada));
    }

    [Fact]
    public void Seguras_sem_nada_a_fazer_nao_pergunta()
    {
        var r = Rodar(e => e.CorrigirSeguras(), "s\n");

        Assert.DoesNotContain("Confirma", r.Saida);
        Assert.Contains("Nenhuma correção segura a fazer agora.", r.Saida);
    }

    [Fact]
    public void Uma_correcao_que_falha_nao_impede_a_outra_e_fica_no_registro()
    {
        var lixo = pc.Arquivo(pc.TempDoUsuario, "lixo.tmp");
        pc.Lixeira = new ConteudoDaLixeira(1000, 1);
        pc.LixeiraQuebrada = true;

        var r = Rodar(e => e.CorrigirSeguras(), "s\n");

        Assert.Equal(Comandos.Falha, r.Codigo);
        Assert.False(File.Exists(lixo));
        Assert.Contains("Lixeira: erro ao corrigir: lixeira travada", r.Erros);
        var registro = Registro.Ler();
        Assert.Contains(registro, e => e.Item == "temporarios" && e.Decisao == Decisao.Aplicada && e.Apagados == 1);
        Assert.Contains(registro, e => e.Item == "lixeira" && e.Decisao == Decisao.Erro);
    }

    [Fact]
    public void Registro_nao_guarda_o_caminho_quando_o_TEMP_esta_fora_do_padrao()
    {
        pc.TempDoUsuario = Path.Combine(pc.Raiz, "Usuario Ana", "Documentos");

        var r = Corrigir("temporarios", "s\n");

        Assert.Contains(pc.TempDoUsuario, r.Saida); // a tela mostra, para a pessoa entender
        var bruto = File.ReadAllText(Registro.Arquivo);
        Assert.DoesNotContain(pc.Raiz, bruto);
        Assert.Contains("(caminho)", bruto);
    }

    [Theory]
    [InlineData(@"fora do padrão (C:\Users\Ana Maria\Docs) e não", "fora do padrão ((caminho)) e não")]
    [InlineData(@"no caminho \\servidor\pasta (x)", "no caminho (caminho) (x)")]
    [InlineData("fora (/home/ana/x y) e não", "fora ((caminho)) e não")]
    [InlineData("1/2 de 24 h, sem caminho", "1/2 de 24 h, sem caminho")]
    public void Registro_troca_qualquer_caminho(string texto, string esperado)
    {
        Assert.Equal(esperado, RegistroDeCorrecoes.SemCaminho(texto));
    }

    [Fact]
    public void Registro_anota_quanto_liberou_e_sem_caminho_de_arquivo()
    {
        pc.Arquivo(pc.TempDoUsuario, "segredo-do-andre.tmp", 4096);

        Corrigir("temporarios", "s\n");

        var entrada = Assert.Single(Registro.Ler());
        Assert.Equal(("temporarios", Decisao.Aplicada, 4096L, 1), (entrada.Item, entrada.Decisao, entrada.BytesLiberados, entrada.Apagados));
        var bruto = File.ReadAllText(Registro.Arquivo);
        Assert.Contains("\"decisao\":\"aplicada\"", bruto);
        Assert.DoesNotContain("segredo", bruto);
        Assert.DoesNotContain(pc.Raiz, bruto);
    }
}
