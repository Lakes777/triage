namespace Triage.Verificacoes.Itens;

public sealed class DesligamentosInesperados : IVerificacao
{
    public string Id => "desligamentos";
    public string Titulo => "Desligamentos inesperados";
    public string OQueE => "Vezes em que o Windows desligou sem passar pelo desligamento normal: falta de luz, bateria que acabou, botão segurado ou travamento.";
    public string PorQueImporta => "Uma vez pode ser falta de luz. Repetindo, costuma ser fonte, bateria ou superaquecimento, e cada desligamento assim pode corromper arquivos.";
    public string ComoResolver => "Se não foi falta de luz nem você segurando o botão, confira se o PC esquenta muito (saídas de ar limpas, ventoinha girando), a saúde da bateria e, no computador de mesa, a fonte.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Eventos.FoiLido)
            return Avaliacao.Pulada(foto.Eventos.Pulo!.Value);
        var eventos = foto.Eventos.Valor!;
        return new Avaliacao(
            Resumos.PelaContagem(eventos.DesligamentosInesperados, limites.DesligamentosAtencao, limites.DesligamentosCritico),
            Resumos.Eventos(eventos.DesligamentosInesperados, eventos.Dias));
    }
}
