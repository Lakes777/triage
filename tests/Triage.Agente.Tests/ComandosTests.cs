using Triage.Agente;
using Triage.Contratos;

namespace Triage.Agente.Tests;

public class ComandosTests
{
    private static (int codigo, string saida, string erros) Rodar(params string[] args)
    {
        var saida = new StringWriter();
        var erros = new StringWriter();
        var codigo = Comandos.Executar(args, saida, erros);
        return (codigo, saida.ToString(), erros.ToString());
    }

    [Fact]
    public void Versao_mostra_a_versao_do_triage()
    {
        var (codigo, saida, erros) = Rodar("--versao");

        Assert.Equal(Comandos.Sucesso, codigo);
        Assert.Equal($"triage {VersaoDoTriage.Atual}", saida.Trim());
        Assert.Empty(erros);
    }

    [Theory]
    [InlineData]
    [InlineData("--ajuda")]
    public void Sem_comando_ou_ajuda_mostra_o_uso(params string[] args)
    {
        var (codigo, saida, _) = Rodar(args);

        Assert.Equal(Comandos.Sucesso, codigo);
        Assert.Contains("triage --versao", saida);
    }

    [Fact]
    public void Comando_desconhecido_avisa_e_devolve_erro_de_uso()
    {
        var (codigo, saida, erros) = Rodar("formatar", "C:");

        Assert.Equal(Comandos.UsoErrado, codigo);
        Assert.Empty(saida);
        Assert.Contains("Comando desconhecido: formatar C:", erros);
    }

    [Theory]
    [InlineData("0.1.0+9c3e94b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7", "0.1.0+9c3e94b")]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+9c3e", "0.1.0+9c3e")]
    public void Versao_fica_com_o_commit_curto(string completa, string esperada)
    {
        Assert.Equal(esperada, VersaoDoTriage.Encurtar(completa));
    }
}
