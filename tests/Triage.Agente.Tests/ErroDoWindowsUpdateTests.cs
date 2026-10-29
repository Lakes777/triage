using System.Reflection;
using System.Runtime.InteropServices;
using Triage.Agente;
using Triage.Verificacoes;

namespace Triage.Agente.Tests;

public class ErroDoWindowsUpdateTests
{
    private static MotivoDoPulo Motivo(uint hresult, bool admin = false) =>
        ErroDoWindowsUpdate.Motivo(new COMException("erro", unchecked((int)hresult)), admin);

    [Theory]
    [InlineData(0x800B0109u)] // raiz não confiável: o Fortinet da PUC abrindo o HTTPS (medido em 29/10/2026)
    [InlineData(0x800B010Fu)] // nome do certificado não bate
    [InlineData(0x80072EFDu)] // WinINet: não conectou
    [InlineData(0x80072EE7u)] // WinINet: nome não resolvido (DNS)
    [InlineData(0x80072F8Fu)] // WinINet: erro de segurança
    [InlineData(0x8024402Cu)] // WU: proxy ou nome do servidor
    [InlineData(0x80244022u)] // WU: servidor respondeu 503
    public void Falha_de_rede_e_sem_conexao(uint hresult)
    {
        Assert.Equal(MotivoDoPulo.SemConexao, Motivo(hresult));
    }

    [Fact]
    public void Prazo_estourado_e_sem_conexao()
    {
        Assert.Equal(MotivoDoPulo.SemConexao, ErroDoWindowsUpdate.Motivo(new TimeoutException(), admin: true));
    }

    [Fact]
    public void Acesso_negado_e_falta_de_admin_ou_de_permissao()
    {
        Assert.Equal(MotivoDoPulo.SemAdmin, Motivo(0x80070005));
        Assert.Equal(MotivoDoPulo.SemPermissao, Motivo(0x80070005, admin: true));
        Assert.Equal(MotivoDoPulo.SemAdmin, ErroDoWindowsUpdate.Motivo(new UnauthorizedAccessException(), admin: false));
    }

    [Theory]
    [InlineData(0x80070422u)] // serviço do Windows Update desativado
    [InlineData(0x8024001Eu)] // WU: serviço parando
    [InlineData(0x80004005u)] // falha genérica
    public void Outros_erros_sao_erro(uint hresult)
    {
        Assert.Equal(MotivoDoPulo.Erro, Motivo(hresult));
    }

    [Fact]
    public void Erro_embrulhado_vale_pelo_de_dentro()
    {
        var embrulhado = new AggregateException(new TargetInvocationException(new COMException("certificado", unchecked((int)0x800B0109))));

        Assert.Equal(MotivoDoPulo.SemConexao, ErroDoWindowsUpdate.Motivo(embrulhado, admin: false));
    }
}
