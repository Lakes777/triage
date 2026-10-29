using Triage.Verificacoes;
using Triage.Verificacoes.Itens;
using static Triage.Verificacoes.Tests.Fotos;

namespace Triage.Verificacoes.Tests;

/// <summary>Fase 3: antivírus e firewall, com o Defender, antivírus de outra marca e o Windows Server (sem Central de Segurança).</summary>
public class SegurancaTests
{
    private static readonly ProdutoDeSeguranca DefenderNaCentral = new("Windows Defender", Ativo: true, Atualizado: true);
    private static readonly ProdutoDeSeguranca DefenderDesligado = new("Windows Defender", Ativo: false, Atualizado: true);
    private static readonly ProdutoDeSeguranca Terceiro = new("Avast Antivirus", Ativo: true, Atualizado: true);

    // Como o notebook dele (29/10): Defender na Central (397568) e em tempo real, definições do dia
    [Fact]
    public void Defender_ligado_e_em_dia_e_ok()
    {
        var avaliacao = AvaliarAntivirus(new Antivirus([DefenderNaCentral], true, 0));

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.Equal("ativo: Windows Defender; definições do Defender de hoje", avaliacao.Resumo);
    }

    [Fact]
    public void Nenhum_antivirus_ativo_e_critico()
    {
        var avaliacao = AvaliarAntivirus(new Antivirus([DefenderDesligado], false, 0));

        Assert.Equal(Gravidade.Critico, avaliacao.Gravidade);
    }

    [Fact]
    public void Antivirus_de_outra_marca_com_o_Defender_desligado_e_ok()
    {
        // O Windows desliga o Defender de propósito; as definições dele envelhecem sem importar
        var avaliacao = AvaliarAntivirus(new Antivirus([DefenderDesligado, Terceiro], false, 200));

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.Equal("ativo: Avast Antivirus", avaliacao.Resumo);
    }

    [Fact]
    public void Antivirus_de_outra_marca_desatualizado_e_atencao()
    {
        var avaliacao = AvaliarAntivirus(new Antivirus([DefenderDesligado, Terceiro with { Atualizado = false }], false, null));

        Assert.Equal(Gravidade.Atencao, avaliacao.Gravidade);
        Assert.Contains("Avast Antivirus com definições desatualizadas", avaliacao.Resumo);
    }

    [Theory]
    [InlineData(6, Gravidade.Ok)]
    [InlineData(7, Gravidade.Atencao)]
    [InlineData(13, Gravidade.Atencao)]
    [InlineData(14, Gravidade.Critico)]
    public void Definicoes_do_Defender_pela_idade(int dias, Gravidade esperada)
    {
        Assert.Equal(esperada, AvaliarAntivirus(new Antivirus([DefenderNaCentral], true, dias)).Gravidade);
    }

    [Fact]
    public void Windows_Server_sem_Central_usa_o_Defender()
    {
        var ligado = AvaliarAntivirus(new Antivirus(null, true, 1));
        var desligado = AvaliarAntivirus(new Antivirus(null, false, 1));

        Assert.Equal(Gravidade.Ok, ligado.Gravidade);
        Assert.Equal("ativo: Microsoft Defender; definições do Defender de 1 dia atrás", ligado.Resumo);
        Assert.Equal(Gravidade.Critico, desligado.Gravidade);
    }

    [Fact]
    public void Sem_Central_e_sem_Defender_e_pulado_e_nao_critico_nem_ok()
    {
        var avaliacao = AvaliarAntivirus(new Antivirus(null, null, null));

        Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
        Assert.Equal(MotivoDoPulo.NaoSuportado, avaliacao.Motivo);
    }

    [Fact]
    public void Mudar_o_limite_das_definicoes_muda_a_gravidade()
    {
        var foto = Saudavel() with { Antivirus = Lido.Ok(new Antivirus([DefenderNaCentral], true, 3)) };

        Assert.Equal(Gravidade.Atencao, new AntivirusAtivo().Avaliar(foto, Limites.Padrao with { DefinicoesAtencaoDias = 3 }).Gravidade);
    }

    [Fact]
    public void Firewall_ligado_nos_tres_perfis_e_ok()
    {
        var avaliacao = AvaliarFirewall(new Firewall(Perfis(true, true, true), []));

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.Equal("ligado em: Domínio, Particular, Público", avaliacao.Resumo);
    }

    [Fact]
    public void Firewall_desligado_num_perfil_privado_e_atencao()
    {
        var avaliacao = AvaliarFirewall(new Firewall(Perfis(true, false, true), []));

        Assert.Equal(Gravidade.Atencao, avaliacao.Gravidade);
        Assert.Equal("desligado em: Particular (ligado em: Domínio, Público)", avaliacao.Resumo);
    }

    [Fact]
    public void Firewall_desligado_no_perfil_publico_e_critico()
    {
        Assert.Equal(Gravidade.Critico, AvaliarFirewall(new Firewall(Perfis(true, true, false), [])).Gravidade);
    }

    [Fact]
    public void Firewall_de_outra_marca_ativo_e_ok_e_diz_qual()
    {
        var outro = new ProdutoDeSeguranca("Norton Firewall", Ativo: true, Atualizado: true);

        var avaliacao = AvaliarFirewall(new Firewall(Perfis(false, false, false), [outro]));

        Assert.Equal(Gravidade.Ok, avaliacao.Gravidade);
        Assert.StartsWith("protegido por Norton Firewall", avaliacao.Resumo);
    }

    [Fact]
    public void Firewall_de_outra_marca_desligado_nao_conta()
    {
        var outro = new ProdutoDeSeguranca("Norton Firewall", Ativo: false, Atualizado: true);

        Assert.Equal(Gravidade.Critico, AvaliarFirewall(new Firewall(Perfis(true, true, false), [outro])).Gravidade);
    }

    [Fact]
    public void Windows_Server_sem_Central_avalia_so_os_perfis()
    {
        Assert.Equal(Gravidade.Ok, AvaliarFirewall(new Firewall(Perfis(true, true, true), null)).Gravidade);
        Assert.Equal(Gravidade.Critico, AvaliarFirewall(new Firewall(Perfis(true, true, false), null)).Gravidade);
    }

    [Fact]
    public void Firewall_sem_nenhum_perfil_lido_e_pulado_e_nao_ok()
    {
        var avaliacao = AvaliarFirewall(new Firewall([], null));

        Assert.Equal(Gravidade.Pulado, avaliacao.Gravidade);
        Assert.Equal(MotivoDoPulo.NaoSuportado, avaliacao.Motivo);
    }

    private static Avaliacao AvaliarAntivirus(Antivirus antivirus) =>
        new AntivirusAtivo().Avaliar(Saudavel() with { Antivirus = Lido.Ok(antivirus) }, Limites.Padrao);

    private static Avaliacao AvaliarFirewall(Firewall firewall) =>
        new FirewallLigado().Avaliar(Saudavel() with { Firewall = Lido.Ok(firewall) }, Limites.Padrao);

    // Os nomes vêm em inglês do Windows, em qualquer idioma
    private static IReadOnlyList<PerfilDoFirewall> Perfis(bool dominio, bool particular, bool publico) =>
        [new("Domain", dominio), new("Private", particular), new("Public", publico)];
}
