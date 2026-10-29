namespace Triage.Verificacoes.Itens;

// ESQUELETO da fase 3 (grupo "rede"): o subagente troca os textos e o Avaliar de verdade
public sealed class AtualizacoesDoWindows : IVerificacao
{
    public string Id => "atualizacoes-windows";
    public string Titulo => "Atualizações do Windows";
    public string OQueE => "";
    public string PorQueImporta => "";
    public string ComoResolver => "";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites) =>
        foto.Atualizacoes.Pulo is MotivoDoPulo motivo
            ? Avaliacao.Pulada(motivo)
            : Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "ainda não verificado");
}
