namespace Triage.Verificacoes.Itens;

// ESQUELETO da fase 3 (grupo "locais"): o subagente troca os textos e o Avaliar de verdade
public sealed class DesligamentosInesperados : IVerificacao
{
    public string Id => "desligamentos";
    public string Titulo => "Desligamentos inesperados";
    public string OQueE => "";
    public string PorQueImporta => "";
    public string ComoResolver => "";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites) =>
        foto.Eventos.Pulo is MotivoDoPulo motivo
            ? Avaliacao.Pulada(motivo)
            : Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "ainda não verificado");
}
