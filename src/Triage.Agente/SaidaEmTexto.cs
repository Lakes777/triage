using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>A revisão no terminal. O relatório HTML chega na fase 4.</summary>
public static class SaidaEmTexto
{
    public static void Escrever(Revisao revisao, TextWriter saida)
    {
        saida.WriteLine($"Triage · revisão de {revisao.FeitaEm.ToString("dd/MM/yyyy HH:mm", Textos.Cultura)}");
        if (!revisao.Administrador)
            saida.WriteLine("Rodando sem admin: alguns itens ficam de fora (rode como administrador para ver tudo).");
        saida.WriteLine();

        foreach (var r in revisao.Resultados)
        {
            saida.WriteLine($"[{Rotulo(r.Avaliacao.Gravidade),-13}] {r.Titulo}: {r.Avaliacao.Resumo}");
            if (r.Avaliacao.Gravidade is Gravidade.Atencao or Gravidade.Critico)
            {
                saida.WriteLine($"{"",16}Por que importa: {r.PorQueImporta}");
                saida.WriteLine($"{"",16}Como resolver: {r.ComoResolver}");
            }
        }

        saida.WriteLine();
        var contagem = revisao.Resultados.GroupBy(r => r.Avaliacao.Gravidade).ToDictionary(g => g.Key, g => g.Count());
        saida.WriteLine(string.Join(" · ", new[] { Gravidade.Critico, Gravidade.Atencao, Gravidade.Ok, Gravidade.Pulado }
            .Select(g => $"{contagem.GetValueOrDefault(g)} {Rotulo(g).ToLower(Textos.Cultura)}")));
    }

    private static string Rotulo(Gravidade g) => g switch
    {
        Gravidade.Critico => "CRÍTICO",
        Gravidade.Atencao => "ATENÇÃO",
        Gravidade.Ok => "OK",
        Gravidade.Pulado => "PULADO",
        _ => "NÃO SE APLICA",
    };
}
