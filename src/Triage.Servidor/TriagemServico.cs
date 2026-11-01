using Grpc.Core;
using Microsoft.AspNetCore.RateLimiting;
using Triage.Contratos;
using Triage.Contratos.Grpc;

namespace Triage.Servidor;

/// <summary>
/// O serviço gRPC que o agente chama. Só traduz: as regras ficam em Inscricoes e Envios, que o painel (fase 9)
/// também usa.
/// </summary>
public sealed class TriagemServico(Inscricoes inscricoes, Envios envios) : Triagem.TriagemBase
{
    public const string CabecalhoDaMaquina = "triage-maquina";
    public const string CabecalhoDaChave = "triage-chave";

    public override Task<VersaoResposta> Versao(VersaoPedido pedido, ServerCallContext contexto) =>
        Task.FromResult(new VersaoResposta { VersaoDoServidor = VersaoDoTriage.Atual });

    [EnableRateLimiting(LimitesDoServidor.PoliticaDeInscricao)]
    public override async Task<InscreverResposta> Inscrever(InscreverPedido pedido, ServerCallContext contexto)
    {
        try
        {
            var (maquina, chave) = await inscricoes.Inscrever(pedido.Codigo, pedido.Apelido, contexto.CancellationToken);
            return new InscreverResposta { MaquinaId = maquina.ToString(), Chave = chave };
        }
        catch (ProblemaDeInscricao problema)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, problema.Message));
        }
    }

    [EnableRateLimiting(LimitesDoServidor.PoliticaDeEnvio)]
    public override async Task<EnviarRevisaoResposta> EnviarRevisao(EnviarRevisaoPedido pedido, ServerCallContext contexto)
    {
        try
        {
            var gravou = await envios.Gravar(contexto.RequestHeaders.GetValue(CabecalhoDaMaquina),
                contexto.RequestHeaders.GetValue(CabecalhoDaChave), pedido, contexto.CancellationToken);
            return new EnviarRevisaoResposta { GravadaAgora = gravou };
        }
        catch (EnvioRecusado recusa)
        {
            // O agente (fase 7) decide pelo código: Unauthenticated/PermissionDenied = parar de tentar e avisar;
            // InvalidArgument/AlreadyExists = tirar da fila (não vai melhorar tentando de novo); ResourceExhausted e
            // Unavailable = tentar mais tarde
            var codigo = recusa.Motivo switch
            {
                RecusaDoEnvio.NaoAutenticado => StatusCode.Unauthenticated,
                RecusaDoEnvio.Revogada => StatusCode.PermissionDenied,
                RecusaDoEnvio.ConteudoDiferente => StatusCode.AlreadyExists,
                RecusaDoEnvio.LimiteDiario => StatusCode.ResourceExhausted,
                _ => StatusCode.InvalidArgument,
            };
            throw new RpcException(new Status(codigo, recusa.Message));
        }
    }
}
