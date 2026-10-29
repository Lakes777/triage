namespace Triage.Verificacoes.Itens;

/// <summary>Pedaços de resumo repetidos entre as verificações da fase 3.</summary>
internal static class Resumos
{
    /// <summary>Até <paramref name="maximo"/> itens e "e mais N" para o resto, para o resumo caber numa linha.</summary>
    public static string Alguns(IReadOnlyList<string> itens, int maximo = 5) =>
        string.Join(", ", itens.Take(maximo)) + (itens.Count > maximo ? $" e mais {itens.Count - maximo}" : "");

    public static string Dias(int dias) => dias == 1 ? "1 dia" : $"{dias} dias";

    /// <summary>Contagem de eventos do log do Sistema: "nenhum", "1" ou "N" nos últimos dias.</summary>
    public static string Eventos(int quantos, int dias) =>
        $"{(quantos == 0 ? "nenhum" : Textos.Numero(quantos))} nos últimos {Dias(dias)}";

    public static Gravidade PelaContagem(int quantos, int atencao, int critico) =>
        quantos >= critico ? Gravidade.Critico
        : quantos >= atencao ? Gravidade.Atencao
        : Gravidade.Ok;
}
