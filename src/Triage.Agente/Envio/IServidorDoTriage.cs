using Grpc.Core;
using Grpc.Net.Client;
using Triage.Contratos.Grpc;

namespace Triage.Agente;

/// <summary>O que fazer com a revisão depois de tentar mandar (nota da fase 6 no SPEC).</summary>
public enum RespostaDoEnvio
{
    /// <summary>Gravada agora, ou já estava gravada (reenvio): sai da fila.</summary>
    Entregue,

    /// <summary>O servidor recusou o conteúdo (inválido, ou o mesmo id com outro conteúdo): sai da fila, não melhora tentando.</summary>
    Recusada,

    /// <summary>Chave errada ou máquina revogada: para tudo e avisa; a fila fica.</summary>
    SemPermissao,

    /// <summary>Sem rede, servidor fora, limite: para e tenta na próxima vez; a fila fica.</summary>
    TentarDepois,
}

public sealed record Envio(RespostaDoEnvio Resposta, string? Detalhe = null);

public interface IServidorDoTriage
{
    Task<(string MaquinaId, string Chave)> Inscrever(string codigo, string apelido, CancellationToken cancelar);
    Task<Envio> Enviar(EnviarRevisaoPedido pedido, string maquinaId, string chave, CancellationToken cancelar);
}

/// <summary>O servidor de verdade, por gRPC. Cada chamada tem prazo: o revisar não pode ficar preso esperando a rede.</summary>
public sealed class ServidorGrpc(Triagem.TriagemClient cliente) : IServidorDoTriage
{
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(15);

    public static ServidorGrpc Conectar(Uri endereco, HttpMessageHandler? transporte = null) =>
        new(new Triagem.TriagemClient(GrpcChannel.ForAddress(endereco,
            new GrpcChannelOptions { HttpHandler = transporte, DisposeHttpClient = true })));

    public async Task<(string MaquinaId, string Chave)> Inscrever(string codigo, string apelido, CancellationToken cancelar)
    {
        var resposta = await cliente.InscreverAsync(
            new InscreverPedido { Codigo = codigo, Apelido = apelido, VersaoDoAgente = Contratos.VersaoDoTriage.Atual },
            deadline: DateTime.UtcNow + Prazo, cancellationToken: cancelar);
        return (resposta.MaquinaId, resposta.Chave);
    }

    public async Task<Envio> Enviar(EnviarRevisaoPedido pedido, string maquinaId, string chave, CancellationToken cancelar)
    {
        // A chave vai no Authorization: o log de acesso do Caddy esconde esse cabeçalho, e não esconderia um próprio
        var credenciais = new Metadata { { "triage-maquina", maquinaId }, { "authorization", $"Bearer {chave}" } };
        try
        {
            await cliente.EnviarRevisaoAsync(pedido, credenciais, DateTime.UtcNow + Prazo, cancelar);
            return new Envio(RespostaDoEnvio.Entregue);
        }
        catch (RpcException erro)
        {
            return new Envio(Classificar(erro.StatusCode), erro.Status.Detail);
        }
    }

    /// <summary>Pura, testada: o que cada código do gRPC quer dizer para a fila.</summary>
    public static RespostaDoEnvio Classificar(StatusCode codigo) => codigo switch
    {
        StatusCode.OK => RespostaDoEnvio.Entregue,
        StatusCode.InvalidArgument or StatusCode.AlreadyExists => RespostaDoEnvio.Recusada,
        StatusCode.Unauthenticated or StatusCode.PermissionDenied => RespostaDoEnvio.SemPermissao,
        // Sem rede (Unavailable), prazo, limite (ResourceExhausted) e qualquer erro do servidor: tenta depois.
        // Na dúvida a revisão fica: perder uma revisão é pior que tentar de novo (o servidor não grava duas vezes)
        _ => RespostaDoEnvio.TentarDepois,
    };
}
