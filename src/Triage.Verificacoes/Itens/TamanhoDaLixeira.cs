namespace Triage.Verificacoes.Itens;

public sealed class TamanhoDaLixeira : IVerificacao
{
    public string Id => "lixeira";
    public string Titulo => "Lixeira";
    public string OQueE => "O que foi apagado mas ainda ocupa espaço, esperando ser esvaziado.";
    public string PorQueImporta => "Arquivo na lixeira continua ocupando o disco.";
    public string ComoResolver => "Confira se não tem nada que queira de volta e rode triage corrigir lixeira (pede confirmação antes).";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Lixeira.FoiLido)
            return Avaliacao.Pulada(foto.Lixeira.Pulo!.Value);
        var lixeira = foto.Lixeira.Valor!;
        if (lixeira.Itens == 0)
            return new Avaliacao(Gravidade.Ok, "vazia");
        var gravidade = lixeira.Bytes >= limites.LixeiraAtencaoGb * (1L << 30) ? Gravidade.Atencao : Gravidade.Ok;
        return new Avaliacao(gravidade, $"{Textos.Tamanho(lixeira.Bytes)} em {Textos.Numero(lixeira.Itens)} itens");
    }
}
