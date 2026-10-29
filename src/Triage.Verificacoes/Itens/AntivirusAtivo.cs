namespace Triage.Verificacoes.Itens;

// ESQUELETO da fase 3 (grupo "locais"): o subagente troca os textos e o Avaliar de verdade
public sealed class AntivirusAtivo : IVerificacao
{
    public string Id => "antivirus";
    public string Titulo => "Antivírus";
    public string OQueE => "";
    public string PorQueImporta => "";
    public string ComoResolver => "";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites) =>
        foto.Antivirus.Pulo is MotivoDoPulo motivo
            ? Avaliacao.Pulada(motivo)
            : Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "ainda não verificado");
}
