using Google.Protobuf.WellKnownTypes;
using Triage.Contratos;
using Triage.Contratos.Grpc;
using Triage.Verificacoes;
using Gravidade = Triage.Verificacoes.Gravidade;
using MotivoDoPulo = Triage.Verificacoes.MotivoDoPulo;

namespace Triage.Agente;

/// <summary>Monta o pedido de envio a partir da revisão e da foto. O id nasce aqui, uma vez por revisão (P17).</summary>
public static class PedidoDeEnvio
{
    public static EnviarRevisaoPedido Montar(FotoDoSistema foto, Revisao revisao) => new()
    {
        RevisaoId = Guid.CreateVersion7(revisao.FeitaEm).ToString(),
        FeitaEm = Timestamp.FromDateTimeOffset(revisao.FeitaEm),
        VersaoDoAgente = VersaoDoTriage.Atual,
        Administrador = revisao.Administrador,
        // A mesma foto do "triage gravar": só medidas, sem nome de usuário, arquivo ou pasta (P15)
        FotoJson = Gravacao.ParaJson(foto),
        Resultados =
        {
            revisao.Resultados.Select(r => new ResultadoDoItem
            {
                Item = r.Item,
                Gravidade = Converter(r.Avaliacao.Gravidade),
                Motivo = r.Avaliacao.Motivo is { } motivo ? Converter(motivo) : Contratos.Grpc.MotivoDoPulo.MotivoNenhum,
            }),
        },
    };

    private static Contratos.Grpc.Gravidade Converter(Gravidade g) => g switch
    {
        Gravidade.Ok => Contratos.Grpc.Gravidade.Ok,
        Gravidade.Atencao => Contratos.Grpc.Gravidade.Atencao,
        Gravidade.Critico => Contratos.Grpc.Gravidade.Critico,
        Gravidade.Pulado => Contratos.Grpc.Gravidade.Pulado,
        Gravidade.NaoSeAplica => Contratos.Grpc.Gravidade.NaoSeAplica,
        _ => throw new ArgumentOutOfRangeException(nameof(g), g, null),
    };

    private static Contratos.Grpc.MotivoDoPulo Converter(MotivoDoPulo m) => m switch
    {
        MotivoDoPulo.SemAdmin => Contratos.Grpc.MotivoDoPulo.MotivoSemAdmin,
        MotivoDoPulo.SemConexao => Contratos.Grpc.MotivoDoPulo.MotivoSemConexao,
        MotivoDoPulo.SemPermissao => Contratos.Grpc.MotivoDoPulo.MotivoSemPermissao,
        MotivoDoPulo.NaoSuportado => Contratos.Grpc.MotivoDoPulo.MotivoNaoSuportado,
        MotivoDoPulo.Erro => Contratos.Grpc.MotivoDoPulo.MotivoErro,
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, null),
    };
}
