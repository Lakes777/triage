namespace Triage.Verificacoes.Itens;

// ESQUELETO da fase 3 (grupo "locais"): o subagente troca os textos e o Avaliar de verdade
public sealed class DriversComErro : IVerificacao
{
    public string Id => "drivers";
    public string Titulo => "Drivers com erro";
    public string OQueE => "";
    public string PorQueImporta => "";
    public string ComoResolver => "";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites) =>
        foto.DriversComErro.Pulo is MotivoDoPulo motivo
            ? Avaliacao.Pulada(motivo)
            : Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "ainda não verificado");
}
