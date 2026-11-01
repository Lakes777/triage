using System.Net;
using Triage.Verificacoes;

namespace Triage.Relatorio.Tests;

public sealed class RelatorioTests
{
    private static readonly DateTimeOffset Quando = new(2026, 11, 1, 14, 30, 0, TimeSpan.FromHours(-3));
    private const string Versao = "0.1.0+abc1234";

    private static string Arquivo(string nome) => Path.Combine(AppContext.BaseDirectory, "dados", nome);

    private static Revisao DoNotebook() =>
        Motor.Padrao().Revisar(Gravacao.DeJson(File.ReadAllText(Arquivo("notebook-sem-admin.json"))), Limites.Padrao);

    private static Resultado Item(string id, Gravidade gravidade, string resumo = "resumo",
        MotivoDoPulo? motivo = null) =>
        new(id, $"Título {id}", new Avaliacao(gravidade, resumo, motivo),
            $"o que é {id}", $"por que importa {id}", $"como resolver {id}");

    private static Revisao UmDeCada() => new(Quando, Administrador: true,
    [
        Item("critico", Gravidade.Critico),
        Item("atencao", Gravidade.Atencao),
        Item("pulado", Gravidade.Pulado, "sem conexão", MotivoDoPulo.SemConexao),
        Item("ok", Gravidade.Ok),
        Item("na", Gravidade.NaoSeAplica),
    ]);

    private static string Gerar(Revisao revisao) => GeradorDeRelatorio.Gerar(revisao, Versao);

    [Fact]
    public async Task Relatorio_do_notebook_tem_todos_os_itens_do_motor()
    {
        var html = await GeradorDeRelatorio.GerarAsync(DoNotebook(), Versao);

        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
        foreach (var v in Motor.Padrao().Verificacoes)
        {
            Assert.Contains($"id=\"item-{v.Id}\"", html);
            Assert.Contains(v.Titulo, html); // acento direto, sem virar &#xFA; (o arquivo fica legível)
        }
    }

    [Fact]
    public void Relatorio_nao_tem_endereco_externo_nem_script()
    {
        Assert.Empty(HtmlSeguro.Problemas(Gerar(DoNotebook())));
        Assert.Empty(HtmlSeguro.Problemas(Gerar(UmDeCada())));
    }

    [Fact]
    public void Relatorio_traz_a_politica_que_bloqueia_script_e_rede_no_navegador()
    {
        // As aspas simples saem como &#x27; no atributo; o navegador decodifica antes de aplicar a política
        var html = WebUtility.HtmlDecode(Gerar(UmDeCada()));

        Assert.Contains($"<meta http-equiv=\"Content-Security-Policy\" content=\"{GeradorDeRelatorio.Politica}\"", html);
        Assert.StartsWith("default-src 'none';", GeradorDeRelatorio.Politica);
        Assert.DoesNotContain("script-src", GeradorDeRelatorio.Politica);
    }

    [Fact]
    public void Contraprova_o_verificador_acha_cada_tipo_de_problema()
    {
        // Se o verificador não achasse nada, o teste acima passaria até com um relatório perigoso
        string[] perigosos =
        [
            "<script>alert(1)</script>",
            "<SCRIPT src=x></SCRIPT>",
            "<a href=\"https://exemplo.com\">x</a>",
            "<a href=//exemplo.com>x</a>",
            "<div onclick=\"x()\">",
            "<img src=\"data:,\">",
            "<form action=\"/corrigir\">",
            "<link rel=stylesheet href=a.css>",
            "<iframe src=a.html>",
            "<a href=\"javascript:void(0)\">",
            "<style>@import 'a.css';</style>",
            "<style>body { background: url(a.png) }</style>",
            "<meta http-equiv=refresh content=\"0;url=a\">",
            // Os casos que o revisor achou passando na primeira versão
            "<a href=\"ms-msdt:/id PCWDiagnostic\">x</a>",
            "<a href=\"search-ms:query=x\">x</a>",
            "<a href=\"ms-settings:windowsupdate\">x</a>",
            "<a href=\"file://servidor/x.exe\">x</a>",
            "<a href=\"relatorio-antigo.html\">x</a>",
            "<meta content=\"0;url=//x\" http-equiv=\"refresh\">",
            "<svg/onload=alert(1)>",
            "<p title=x/onmouseover=y()>",
            "<a href=\"jav&#x61;script:x()\">",
            "<a href=\"java\tscript:x()\">",
            "<style>body { background: \\75rl(a.png) }</style>",
            "<style>body { background: image-set(\"a.png\" 1x) }</style>",
            "<input type=image src=a.png>",
            "<svg><image href=a.png></svg>",
            "<button formaction=x>",
        ];
        foreach (var html in perigosos)
            Assert.True(HtmlSeguro.Problemas(html).Count > 0, $"passou sem ser pego: {html}");
    }

    [Fact]
    public void Ancora_dentro_da_propria_pagina_e_permitida()
    {
        // Deixa a fase 9 ligar a contagem aos itens sem afrouxar a regra
        Assert.Empty(HtmlSeguro.Problemas("<a href=\"#item-firewall\">firewall</a>"));
    }

    [Fact]
    public void Texto_vindo_do_PC_sai_escapado_e_nao_vira_codigo()
    {
        // Nome de programa ou resumo é texto do PC: alguém poderia registrar um programa com nome malicioso
        var revisao = new Revisao(Quando, true,
            [Item("x", Gravidade.Atencao, "<script>alert('oi')</script><img src=https://a.b onerror=x()>")]);

        var html = Gerar(revisao);

        Assert.Empty(HtmlSeguro.Problemas(html.Replace("https://a.b", "")));
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void Problemas_vem_primeiro_e_abertos_e_o_resto_fechado()
    {
        var html = Gerar(UmDeCada());

        int Posicao(string id) => html.IndexOf($"id=\"item-{id}\"", StringComparison.Ordinal);
        Assert.True(Posicao("critico") < Posicao("atencao"));
        Assert.True(Posicao("atencao") < Posicao("pulado"));
        Assert.True(Posicao("pulado") < Posicao("ok"));
        Assert.True(Posicao("ok") < Posicao("na"));

        Assert.Equal(2, Ocorrencias(html, "<details open"));
        Assert.Equal(5, Ocorrencias(html, "<details"));
        Assert.Contains("<h2>Precisa de atenção <span class=\"quantos\">(2)</span></h2>", html);
    }

    [Fact]
    public void Item_pulado_mostra_o_motivo_e_nunca_aparece_como_ok()
    {
        var html = Gerar(UmDeCada());
        var cartao = html[html.IndexOf("id=\"item-pulado\"", StringComparison.Ordinal)..];
        cartao = cartao[..cartao.IndexOf("</article>", StringComparison.Ordinal)];

        Assert.Contains("<span class=\"selo\">Pulado</span>", cartao);
        Assert.Contains("sem conexão", cartao);
        Assert.Contains("g-pulado", html);
    }

    [Fact]
    public void Secao_sem_itens_nao_aparece()
    {
        var html = Gerar(new Revisao(Quando, true, [Item("ok", Gravidade.Ok)]));

        Assert.DoesNotContain("Precisa de atenção", html);
        Assert.Contains("Tudo certo", html);
    }

    [Fact]
    public void Cabecalho_tem_data_versao_contagem_e_aviso_de_admin()
    {
        var semAdmin = Gerar(UmDeCada() with { Administrador = false });
        var comAdmin = WebUtility.HtmlDecode(Gerar(UmDeCada())); // o "+" da versão sai como &#x2B;

        Assert.Contains("Feita em 01/11/2026 às 14:30 · versão 0.1.0+abc1234", comAdmin);
        Assert.Contains("<li class=\"g-critico\"><strong>1</strong> crítico</li>", comAdmin);
        Assert.Contains("Rodou sem administrador", semAdmin);
        Assert.DoesNotContain("Rodou sem administrador", comAdmin);
    }

    private static int Ocorrencias(string texto, string trecho)
    {
        var n = 0;
        for (var i = texto.IndexOf(trecho, StringComparison.Ordinal); i >= 0;
             i = texto.IndexOf(trecho, i + trecho.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
