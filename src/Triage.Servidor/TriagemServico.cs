using Grpc.Core;
using Triage.Contratos;
using Triage.Contratos.Grpc;

namespace Triage.Servidor;

/// <summary>O serviço gRPC que o agente chama. Inscrever e EnviarRevisao chegam nas fases 6 e 7.</summary>
public sealed class TriagemServico : Triagem.TriagemBase
{
    public override Task<VersaoResposta> Versao(VersaoPedido pedido, ServerCallContext contexto) =>
        Task.FromResult(new VersaoResposta { VersaoDoServidor = VersaoDoTriage.Atual });
}
