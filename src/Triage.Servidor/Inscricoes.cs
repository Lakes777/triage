using Microsoft.EntityFrameworkCore;
using Triage.Servidor.Banco;

namespace Triage.Servidor;

public sealed record ResultadoDaInscricao(Guid MaquinaId, string Chave);

public sealed class ProblemaDeInscricao(string mensagem) : Exception(mensagem);

/// <summary>Gerar código, inscrever e revogar. Usado pelo gRPC, pelos comandos do administrador e (fase 9) pelo painel.</summary>
public sealed class Inscricoes(TriageContexto banco, TimeProvider relogio)
{
    public async Task<string> GerarCodigo(CancellationToken cancelar = default)
    {
        var codigo = Regras.NovoCodigo();
        var agora = relogio.GetUtcNow();
        banco.Codigos.Add(new CodigoDeInscricao
        {
            Id = Guid.CreateVersion7(),
            CodigoHash = Regras.Hash(Regras.NormalizarCodigo(codigo)!),
            CriadoEm = agora,
            ExpiraEm = agora + Regras.ValidadeDoCodigo,
        });
        await banco.SaveChangesAsync(cancelar);
        return codigo;
    }

    /// <summary>
    /// Troca o código pela chave. "Usar" o código é um UPDATE que só pega código não usado e não vencido e precisa
    /// afetar 1 linha: com 20 pedidos ao mesmo tempo, o Postgres trava a linha no primeiro e os outros, quando a
    /// trava solta, já veem usado_em preenchido e afetam 0. Resultado: 1 máquina (P17).
    /// </summary>
    public async Task<ResultadoDaInscricao> Inscrever(string? codigoDigitado, string? apelidoDigitado,
        CancellationToken cancelar = default)
    {
        var apelido = Regras.NormalizarApelido(apelidoDigitado)
            ?? throw new ProblemaDeInscricao($"O apelido precisa ter de 1 a {Regras.TamanhoMaximoDoApelido} caracteres.");
        // Mesmo texto para código malformado, inexistente, usado ou vencido: não ajuda quem tenta adivinhar
        var invalido = new ProblemaDeInscricao("Código inválido, já usado ou vencido. Peça um novo ao administrador.");
        var codigo = Regras.NormalizarCodigo(codigoDigitado) ?? throw invalido;
        var hash = Regras.Hash(codigo);
        var agora = relogio.GetUtcNow();

        await using var transacao = await banco.Database.BeginTransactionAsync(cancelar);
        var usados = await banco.Codigos
            .Where(c => c.CodigoHash == hash && c.UsadoEm == null && c.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsadoEm, agora), cancelar);
        if (usados != 1)
            throw invalido;

        var chave = Regras.NovaChave();
        var maquina = new Maquina
        {
            Id = Guid.CreateVersion7(),
            Apelido = apelido,
            ChaveHash = Regras.Hash(chave),
            InscritaEm = agora,
        };
        banco.Maquinas.Add(maquina);
        await banco.SaveChangesAsync(cancelar);
        await banco.Codigos.Where(c => c.CodigoHash == hash)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.MaquinaId, maquina.Id), cancelar);
        await transacao.CommitAsync(cancelar);
        return new ResultadoDaInscricao(maquina.Id, chave);
    }

    /// <summary>
    /// Revoga a chave. O UPDATE pede a trava de escrita da linha: espera um envio em andamento (que segura a de
    /// leitura) terminar. Devolve falso se a máquina não existe ou já estava revogada.
    /// </summary>
    public async Task<bool> Revogar(Guid maquinaId, CancellationToken cancelar = default)
    {
        var agora = relogio.GetUtcNow();
        return await banco.Maquinas
            .Where(m => m.Id == maquinaId && m.RevogadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RevogadaEm, agora), cancelar) == 1;
    }

    public async Task<IReadOnlyList<Maquina>> Listar(CancellationToken cancelar = default) =>
        await banco.Maquinas.AsNoTracking().Where(m => !m.Demo).OrderBy(m => m.InscritaEm).ToListAsync(cancelar);
}
