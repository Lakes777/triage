namespace Triage.Verificacoes.Itens;

public sealed class SaudeDaBateria : IVerificacao
{
    public string Id => "bateria";
    public string Titulo => "Saúde da bateria";
    public string OQueE => "Quanto a bateria ainda guarda de carga comparado a quando era nova.";
    public string PorQueImporta => "Bateria gasta dura menos fora da tomada e, muito gasta, pode desligar o notebook de repente.";
    public string ComoResolver => "Evite deixar sempre em 100% na tomada (muitos fabricantes têm um modo que para em 80%). Abaixo do crítico, procure a troca da bateria.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Bateria.FoiLido)
            return Avaliacao.Pulada(foto.Bateria.Pulo!.Value);
        if (foto.Bateria.Valor is not Bateria bateria)
            return new Avaliacao(Gravidade.NaoSeAplica, "este PC não tem bateria");
        if (bateria.CapacidadeDeProjeto <= 0 || bateria.CapacidadeAtual <= 0)
            return Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "a bateria não informa a capacidade");

        var saude = bateria.CapacidadeAtual * 100.0 / bateria.CapacidadeDeProjeto;
        var gravidade = saude switch
        {
            var s when s < limites.BateriaCriticoPorcento => Gravidade.Critico,
            var s when s < limites.BateriaAtencaoPorcento => Gravidade.Atencao,
            _ => Gravidade.Ok,
        };
        var ciclos = bateria.Ciclos is int c ? $", {c} ciclos de carga" : "";
        return new Avaliacao(gravidade, string.Format(Textos.Cultura,
            "{0} da capacidade original ({1:0.0} de {2:0.0} Wh){3}",
            Textos.Porcento(saude), bateria.CapacidadeAtual / 1000.0, bateria.CapacidadeDeProjeto / 1000.0, ciclos));
    }
}
