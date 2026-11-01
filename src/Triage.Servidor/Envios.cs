using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Triage.Contratos.Grpc;
using Triage.Servidor.Banco;

namespace Triage.Servidor;

public enum RecusaDoEnvio
{
    /// <summary>Máquina desconhecida ou chave errada (a mesma resposta para os dois: não diz qual errou).</summary>
    NaoAutenticado,
    Revogada,
    Invalido,
    ConteudoDiferente,

    /// <summary>Mais revisões novas num dia do que uma máquina normal manda: chave vazada ou agente com defeito.</summary>
    LimiteDiario,
}

public sealed class EnvioRecusado(RecusaDoEnvio motivo, string mensagem) : Exception(mensagem)
{
    public RecusaDoEnvio Motivo => motivo;
}

/// <summary>Ganchos que só os testes usam, para segurar um envio no meio e provocar a corrida com a revogação.</summary>
public class PontosDeTeste
{
    public virtual Task DepoisDeConferirAMaquina() => Task.CompletedTask;
}

/// <summary>Recebe e grava revisões (P17, P25).</summary>
public sealed partial class Envios(TriageContexto banco, TimeProvider relogio, PontosDeTeste pontos,
    LimitesDoServidor limites)
{
    public const int MaximoDeResultados = 100;
    public const int TamanhoMaximoDoItem = 64;

    /// <summary>Antes disso o Triage não existia: um feita_em zerado (1970) estragaria a ordem do histórico.</summary>
    public static readonly DateTimeOffset InicioDoTriage = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Hash de uma chave qualquer: máquina desconhecida também faz uma comparação, e o tempo da resposta não diz
    // se o id existe
    private static readonly byte[] HashQualquer = Regras.Hash("máquina que não existe");

    /// <summary>
    /// Grava a revisão e diz se gravou agora (falso = a mesma revisão já estava lá). A máquina é lida com
    /// FOR SHARE: a revogação (UPDATE) espera este envio terminar, e um envio que chega depois da revogação já vê
    /// revogada_em. Então a revisão grava antes da revogação ou é recusada, nunca depois (P17).
    /// </summary>
    public async Task<bool> Gravar(string? maquinaTexto, string? chave, EnviarRevisaoPedido pedido,
        CancellationToken cancelar = default)
    {
        var revisaoId = Validar(pedido);
        var naoAutenticado = new EnvioRecusado(RecusaDoEnvio.NaoAutenticado, "Máquina ou chave inválida.");
        if (!Guid.TryParse(maquinaTexto, out var maquinaId) || string.IsNullOrEmpty(chave))
            throw naoAutenticado;

        await using var transacao = await banco.Database.BeginTransactionAsync(cancelar);
        var maquina = (await banco.Maquinas
            .FromSqlInterpolated($"SELECT * FROM maquinas WHERE id = {maquinaId} FOR SHARE")
            .AsNoTracking()
            .ToListAsync(cancelar)).SingleOrDefault();
        var chaveConfere = Regras.MesmaChave(chave, maquina?.ChaveHash ?? HashQualquer);
        if (maquina is null || !chaveConfere)
            throw naoAutenticado;
        if (maquina.RevogadaEm is not null)
            throw new EnvioRecusado(RecusaDoEnvio.Revogada, "Esta máquina foi revogada pelo administrador.");
        await pontos.DepoisDeConferirAMaquina();

        var conteudo = SHA256.HashData(pedido.ToByteArray());
        var agora = relogio.GetUtcNow();
        try
        {
            return await GravarNaTransacao(maquinaId, revisaoId, pedido, conteudo, agora, transacao, cancelar);
        }
        catch (PostgresException erro) when (erro.SqlState.StartsWith("22", StringComparison.Ordinal))
        {
            // Classe 22 = dado que o Postgres não aceita (\u0000 no jsonb, número fora da faixa...). Sem isto
            // virava "erro interno", e o agente tentaria de novo para sempre um envio que nunca vai entrar
            throw new EnvioRecusado(RecusaDoEnvio.Invalido, $"O banco recusou o conteúdo da revisão ({erro.SqlState}).");
        }
    }

    private async Task<bool> GravarNaTransacao(Guid maquinaId, Guid revisaoId, EnviarRevisaoPedido pedido,
        byte[] conteudo, DateTimeOffset agora, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transacao,
        CancellationToken cancelar)
    {
        var jaExiste = await banco.Revisoes.AnyAsync(r => r.MaquinaId == maquinaId && r.Id == revisaoId, cancelar);
        // O teto conta só revisões novas: o reenvio da fila (mesmo id) nunca é barrado por ele
        if (!jaExiste && await banco.Revisoes.CountAsync(
                r => r.MaquinaId == maquinaId && r.RecebidaEm > agora.AddDays(-1), cancelar) >= limites.RevisoesPorDia)
            throw new EnvioRecusado(RecusaDoEnvio.LimiteDiario,
                $"Esta máquina já mandou {limites.RevisoesPorDia} revisões nas últimas 24 h. Tente mais tarde.");
        var gravou = await banco.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO revisoes (maquina_id, id, feita_em, recebida_em, versao_do_agente, administrador, conteudo_hash, foto)
            VALUES ({maquinaId}, {revisaoId}, {pedido.FeitaEm.ToDateTimeOffset()}, {agora}, {pedido.VersaoDoAgente},
                    {pedido.Administrador}, {conteudo}, {pedido.FotoJson}::jsonb)
            ON CONFLICT (maquina_id, id) DO NOTHING
            """, cancelar) == 1;
        if (!gravou)
        {
            // Já estava lá: o mesmo conteúdo é o envio repetido pela fila (sucesso); outro conteúdo é erro
            var guardado = await banco.Revisoes.AsNoTracking()
                .Where(r => r.MaquinaId == maquinaId && r.Id == revisaoId)
                .Select(r => r.ConteudoHash).SingleAsync(cancelar);
            if (!CryptographicOperations.FixedTimeEquals(guardado, conteudo))
                throw new EnvioRecusado(RecusaDoEnvio.ConteudoDiferente,
                    "Já existe uma revisão com este id e outro conteúdo.");
            await transacao.CommitAsync(cancelar);
            return false;
        }

        banco.Resultados.AddRange(pedido.Resultados.Select(r => new ResultadoGravado
        {
            MaquinaId = maquinaId,
            RevisaoId = revisaoId,
            Item = r.Item,
            Gravidade = Nome(r.Gravidade),
            Motivo = r.Gravidade == Gravidade.Pulado ? Nome(r.Motivo) : null,
        }));
        await banco.SaveChangesAsync(cancelar);
        await transacao.CommitAsync(cancelar);
        return true;
    }

    private Guid Validar(EnviarRevisaoPedido pedido)
    {
        EnvioRecusado Invalido(string mensagem) => new(RecusaDoEnvio.Invalido, mensagem);
        if (!Guid.TryParse(pedido.RevisaoId, out var id) || id == Guid.Empty)
            throw Invalido("revisao_id precisa ser um UUID.");
        // Segundos fora da faixa fazem o ToDateTimeOffset lançar (virava erro interno): confere antes
        if (pedido.FeitaEm is not { Seconds: >= 0 and <= 253402300799, Nanos: >= 0 and <= 999_999_999 } feita)
            throw Invalido("feita_em é obrigatório e precisa ser uma data válida.");
        // Um dia de folga para relógio adiantado; fora disso estragaria a ordem do histórico (fase 8)
        if (feita.ToDateTimeOffset() is var quando && (quando < InicioDoTriage || quando > relogio.GetUtcNow().AddDays(1)))
            throw Invalido("feita_em fora do intervalo aceito (de 2026 até amanhã).");
        if (!VersaoValida().IsMatch(pedido.VersaoDoAgente))
            throw Invalido("versao_do_agente inválida (até 64 caracteres: letras, números, ponto, + e -).");
        if (pedido.Resultados.Count is 0 or > MaximoDeResultados)
            throw Invalido($"A revisão precisa ter de 1 a {MaximoDeResultados} itens.");
        if (pedido.Resultados.Select(r => r.Item).Distinct().Count() != pedido.Resultados.Count)
            throw Invalido("Item repetido na revisão.");
        foreach (var r in pedido.Resultados)
        {
            // Itens que este servidor ainda não conhece (agente mais novo) são aceitos, desde que tenham cara de id.
            // O tamanho antes da regex: a coluna é varchar(64), e a mensagem de erro não ecoa um texto enorme
            if (r.Item.Length > TamanhoMaximoDoItem || !IdDeItem().IsMatch(r.Item))
                throw Invalido($"Item inválido: {r.Item[..Math.Min(r.Item.Length, TamanhoMaximoDoItem)]}");
            // O protobuf guarda o número de um valor que o enum não tem (agente mais novo): não pode virar outro
            if (r.Gravidade == Gravidade.Desconhecida || !System.Enum.IsDefined(r.Gravidade))
                throw Invalido($"Gravidade faltando ou desconhecida em {r.Item}.");
            if (!System.Enum.IsDefined(r.Motivo))
                throw Invalido($"Motivo desconhecido em {r.Item}.");
            // A mesma regra da Avaliacao do agente: pulado sempre com motivo, e só pulado tem motivo
            if ((r.Gravidade == Gravidade.Pulado) != (r.Motivo != MotivoDoPulo.MotivoNenhum))
                throw Invalido($"Em {r.Item}: pulado precisa de motivo, e só pulado tem motivo.");
        }
        try
        {
            using var foto = JsonDocument.Parse(pedido.FotoJson);
            if (foto.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalido("foto_json precisa ser um objeto JSON.");
        }
        catch (JsonException)
        {
            throw Invalido("foto_json não é JSON válido.");
        }
        return id;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdDeItem();

    [GeneratedRegex("^[0-9A-Za-z.+-]{1,64}$")]
    private static partial Regex VersaoValida();

    private static string Nome(Gravidade g) => g switch
    {
        Gravidade.Ok => "ok",
        Gravidade.Atencao => "atencao",
        Gravidade.Critico => "critico",
        Gravidade.Pulado => "pulado",
        Gravidade.NaoSeAplica => "nao_se_aplica",
        _ => throw new ArgumentOutOfRangeException(nameof(g), g, "gravidade sem nome (a validação deveria ter barrado)"),
    };

    private static string Nome(MotivoDoPulo m) => m switch
    {
        MotivoDoPulo.MotivoSemAdmin => "sem_admin",
        MotivoDoPulo.MotivoSemConexao => "sem_conexao",
        MotivoDoPulo.MotivoSemPermissao => "sem_permissao",
        MotivoDoPulo.MotivoNaoSuportado => "nao_suportado",
        MotivoDoPulo.MotivoErro => "erro",
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, "motivo sem nome (a validação deveria ter barrado)"),
    };
}
