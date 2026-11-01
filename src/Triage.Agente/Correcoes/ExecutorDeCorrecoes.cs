using Triage.Contratos;

namespace Triage.Agente;

/// <summary>
/// Roda as correções no terminal: mostra o que vai ser feito, pergunta, faz e anota. Só "s" ou "sim" confirmam;
/// Enter vazio, qualquer outra resposta ou entrada fechada (rodando sem terminal) cancelam (P4, P20, P26).
/// </summary>
public sealed class ExecutorDeCorrecoes(
    TextReader entrada, TextWriter saida, TextWriter erros, IAmbienteDeCorrecao ambiente, RegistroDeCorrecoes registro)
{
    // Um horário só por comando: o arquivo que passa das 24 h enquanto a pessoa lê a pergunta não entra
    private readonly DateTimeOffset agora = ambiente.Agora;

    public int Corrigir(ICorrecao correcao)
    {
        if (PlanejarSemQuebrar(correcao) is not { } plano)
            return Comandos.Falha;
        if (!plano.TemOQueFazer)
        {
            saida.WriteLine($"{correcao.Titulo}: {plano.Descricao}");
            Anotar(correcao, Decisao.NadaAFazer, null, plano.Descricao);
            return plano.Bloqueado ? Comandos.Falha : Comandos.Sucesso;
        }

        saida.WriteLine($"{correcao.Titulo}: {plano.Descricao}.");
        if (!Confirmar($"Confirma {plano.Acao}?"))
        {
            saida.WriteLine("Cancelado: nada foi alterado.");
            Anotar(correcao, Decisao.Recusada, null, plano.Acao!);
            return Comandos.Sucesso;
        }
        return Aplicar(correcao) ? Comandos.Sucesso : Comandos.Falha;
    }

    /// <summary>As correções seguras de uma vez: a lista inteira primeiro, depois UMA pergunta (P26).</summary>
    public int CorrigirSeguras()
    {
        // Uma que nem consegue planejar (lixeira que não lê) fica de fora e anotada; as outras seguem
        var planos = Correcoes.Todas.Where(c => c.Segura)
            .Select(c => (Correcao: c, Plano: PlanejarSemQuebrar(c)))
            .Where(p => p.Plano is not null)
            .Select(p => (p.Correcao, Plano: p.Plano!))
            .ToList();
        var aFazer = planos.Where(p => p.Plano.TemOQueFazer).ToList();
        foreach (var (correcao, plano) in planos.Where(p => !p.Plano.TemOQueFazer))
        {
            saida.WriteLine($"{correcao.Titulo}: {plano.Descricao}");
            Anotar(correcao, Decisao.NadaAFazer, null, plano.Descricao);
        }
        if (aFazer.Count == 0)
        {
            saida.WriteLine("Nenhuma correção segura a fazer agora.");
            return Comandos.Sucesso;
        }

        saida.WriteLine();
        saida.WriteLine("Correções seguras que serão feitas:");
        foreach (var (correcao, plano) in aFazer)
            saida.WriteLine($"  - {correcao.Titulo}: {plano.Descricao}");
        if (!Confirmar(aFazer.Count == 1 ? "Confirma esta correção?" : $"Confirma as {aFazer.Count} correções?"))
        {
            saida.WriteLine("Cancelado: nada foi alterado.");
            foreach (var (correcao, plano) in aFazer)
                Anotar(correcao, Decisao.Recusada, null, plano.Acao!);
            return Comandos.Sucesso;
        }

        // Uma que falha não impede as outras; o código de saída conta a falha
        var todasDeramCerto = true;
        foreach (var (correcao, _) in aFazer)
            todasDeramCerto &= Aplicar(correcao);
        return todasDeramCerto ? Comandos.Sucesso : Comandos.Falha;
    }

    private Plano? PlanejarSemQuebrar(ICorrecao correcao)
    {
        try
        {
            return correcao.Planejar(ambiente, agora);
        }
        catch (Exception erro)
        {
            erros.WriteLine($"{correcao.Titulo}: erro ao verificar: {erro.Message}");
            Anotar(correcao, Decisao.Erro, null, Erro(erro));
            return null;
        }
    }

    private bool Aplicar(ICorrecao correcao)
    {
        try
        {
            var resultado = correcao.Aplicar(ambiente, agora);
            saida.WriteLine($"{correcao.Titulo}: {resultado.Resumo}.");
            Anotar(correcao, Decisao.Aplicada, resultado, resultado.Resumo);
            return true;
        }
        catch (Exception erro)
        {
            erros.WriteLine($"{correcao.Titulo}: erro ao corrigir: {erro.Message}");
            Anotar(correcao, Decisao.Erro, null, Erro(erro));
            return false;
        }
    }

    private bool Confirmar(string pergunta)
    {
        saida.Write($"{pergunta} Digite s e Enter para confirmar (qualquer outra coisa cancela): ");
        saida.Flush();
        var resposta = entrada.ReadLine();
        saida.WriteLine();
        return resposta?.Trim().ToLowerInvariant() is "s" or "sim";
    }

    // A mensagem da exceção costuma trazer caminho (P15): no registro só o tipo e o código; a tela mostra tudo
    private static string Erro(Exception erro) => $"{erro.GetType().Name} (0x{erro.HResult:X8})";

    private void Anotar(ICorrecao correcao, Decisao decisao, ResultadoDaCorrecao? resultado, string resumo) =>
        registro.Anotar(new EntradaDoRegistro(agora, correcao.Item, decisao,
            resultado?.BytesLiberados ?? 0, resultado?.Apagados ?? 0, resultado?.Pulados ?? 0, resumo,
            VersaoDoTriage.Atual));
}
