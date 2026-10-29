namespace Triage.Verificacoes.Itens;

// ESQUELETO da fase 3 (grupo "rede"): o subagente troca os textos e o Avaliar de verdade
public sealed class ProgramasDesatualizados : IVerificacao
{
    public string Id => "programas-desatualizados";
    public string Titulo => "Programas desatualizados";
    public string OQueE => "";
    public string PorQueImporta => "";
    public string ComoResolver => "";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites) =>
        foto.ProgramasDesatualizados.Pulo is MotivoDoPulo motivo
            ? Avaliacao.Pulada(motivo)
            : Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "ainda não verificado");
}
