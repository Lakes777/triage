namespace Triage.Agente;

/// <summary>Resultado de esvaziar a fila, para o terminal.</summary>
public sealed record ResultadoDoEnvio(int Entregues, IReadOnlyList<string> Recusadas, int Restantes,
    RespostaDoEnvio? Parou, string? Detalhe);

/// <summary>
/// Esvazia a fila, da revisão mais antiga para a mais nova. Uma revisão só sai da fila depois da confirmação do
/// servidor (P17): sem rede ela fica e sobe na próxima vez; o servidor não grava duas vezes a mesma.
/// </summary>
public sealed class Enviador(FilaDeEnvios fila, IServidorDoTriage servidor, Inscricao inscricao, string chave)
{
    public async Task<ResultadoDoEnvio> EsvaziarFila(CancellationToken cancelar = default)
    {
        var entregues = 0;
        var recusadas = new List<string>();
        using var trava = fila.Travar();
        if (trava is null)
            return new ResultadoDoEnvio(0, [], fila.Pendentes().Count, RespostaDoEnvio.TentarDepois,
                "outra execução do Triage está enviando agora");
        foreach (var arquivo in fila.Pendentes())
        {
            Contratos.Grpc.EnviarRevisaoPedido pedido;
            try
            {
                pedido = FilaDeEnvios.Ler(arquivo);
            }
            catch (FileNotFoundException)
            {
                continue; // tirada no meio (teto da fila): segue com as outras
            }
            catch (Google.Protobuf.InvalidProtocolBufferException)
            {
                // Arquivo estragado (disco, edição à mão): nunca vai entrar; tirar para não travar a fila
                fila.Tirar(arquivo);
                recusadas.Add("arquivo da fila estragado");
                continue;
            }

            var envio = await servidor.Enviar(pedido, inscricao.MaquinaId, chave, cancelar);
            switch (envio.Resposta)
            {
                case RespostaDoEnvio.Entregue:
                    fila.Tirar(arquivo);
                    entregues++;
                    break;
                case RespostaDoEnvio.Recusada:
                    fila.Tirar(arquivo);
                    recusadas.Add(envio.Detalhe ?? "recusada pelo servidor");
                    break;
                default:
                    // Sem permissão ou sem rede: as outras dariam o mesmo; para e deixa tudo na fila
                    return new ResultadoDoEnvio(entregues, recusadas, fila.Pendentes().Count, envio.Resposta, envio.Detalhe);
            }
        }
        return new ResultadoDoEnvio(entregues, recusadas, 0, null, null);
    }
}
