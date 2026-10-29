using System.Reflection;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// Traduz o erro do Windows Update (o HRESULT da exceção) num motivo de pulo. Fica fora do leitor para ser testado
/// no Linux: falha de rede tem que sair "sem conexão", nunca "erro" nem "ok".
/// </summary>
public static class ErroDoWindowsUpdate
{
    public static MotivoDoPulo Motivo(Exception erro, bool admin)
    {
        while (erro is TargetInvocationException or AggregateException && erro.InnerException is not null)
            erro = erro.InnerException;
        var codigo = (uint)erro.HResult;
        var semPermissao = admin ? MotivoDoPulo.SemPermissao : MotivoDoPulo.SemAdmin;
        return codigo switch
        {
            // WU_E_PT_* (0x80244xxx): rede, proxy e servidor do Windows Update
            _ when (codigo & 0xFFFFF000) == 0x80244000 => MotivoDoPulo.SemConexao,
            // Erros 12xxx do WinINet/WinHTTP (0x80072xxx): sem rota, DNS, tempo esgotado, conexão recusada
            _ when (codigo & 0xFFFFF000) == 0x80072000 => MotivoDoPulo.SemConexao,
            // Confiança de certificado (0x800B0xxx): um firewall que abre o HTTPS no meio do caminho. Medido em
            // 29/10/2026 na rede da PUC (Fortinet): a busca falha com 0x800B0109 (raiz não confiável)
            _ when (codigo & 0xFFFFF000) == 0x800B0000 => MotivoDoPulo.SemConexao,
            _ when erro is TimeoutException => MotivoDoPulo.SemConexao,
            // E_ACCESSDENIED
            0x80070005 => semPermissao,
            _ when erro is UnauthorizedAccessException => semPermissao,
            _ => MotivoDoPulo.Erro,
        };
    }
}
