namespace Triage.Verificacoes.Itens;

public sealed class EspacoLivre : IVerificacao
{
    public string Id => "espaco-livre";
    public string Titulo => "Espaço livre";
    public string OQueE => "Quanto sobra em cada unidade do PC.";
    public string PorQueImporta => "Com o disco quase cheio o Windows não consegue instalar atualizações, fica lento e programas começam a dar erro ao salvar.";
    public string ComoResolver => "Limpe os temporários e a lixeira (o Triage faz isso com confirmação), apague ou mova arquivos grandes de Downloads e desinstale programas que não usa.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Volumes.FoiLido)
            return Avaliacao.Pulada(foto.Volumes.Pulo!.Value);
        var volumes = foto.Volumes.Valor!.Where(v => v.Total > 0).ToList();
        if (volumes.Count == 0)
            return Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "nenhuma unidade encontrada");

        var gravidade = Textos.Pior(volumes.Select(v => PorcentoLivre(v) switch
        {
            var p when p < limites.EspacoLivreCriticoPorcento => Gravidade.Critico,
            var p when p < limites.EspacoLivreAtencaoPorcento => Gravidade.Atencao,
            _ => Gravidade.Ok,
        }));
        var resumo = string.Join("; ", volumes.Select(v =>
            $"{v.Letra} {Textos.Tamanho(v.Livre)} livres de {Textos.Tamanho(v.Total)} ({Textos.Porcento(PorcentoLivre(v))})"));
        return new Avaliacao(gravidade, resumo);
    }

    private static double PorcentoLivre(Volume v) => v.Livre * 100.0 / v.Total;
}
