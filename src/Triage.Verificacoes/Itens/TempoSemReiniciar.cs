namespace Triage.Verificacoes.Itens;

public sealed class TempoSemReiniciar : IVerificacao
{
    public string Id => "reinicio";
    public string Titulo => "Tempo sem reiniciar";
    public string OQueE => "Há quanto tempo o Windows não reinicia de verdade. Com a inicialização rápida ligada (o padrão), \"Desligar\" não conta: só \"Reiniciar\".";
    public string PorQueImporta => "Muitas atualizações de segurança só passam a valer depois de reiniciar, e o sistema acumula lentidão e travamentos com o tempo.";
    public string ComoResolver => "Use Iniciar > Energia > Reiniciar (não Desligar).";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.UltimaInicializacao.FoiLido)
            return Avaliacao.Pulada(foto.UltimaInicializacao.Pulo!.Value);
        var desde = foto.UltimaInicializacao.Valor;
        var ligado = foto.LidaEm - desde;
        var gravidade = ligado.TotalDays switch
        {
            var d when d >= limites.ReinicioCriticoDias => Gravidade.Critico,
            var d when d >= limites.ReinicioAtencaoDias => Gravidade.Atencao,
            _ => Gravidade.Ok,
        };
        var dias = (int)ligado.TotalDays;
        var tempo = dias switch
        {
            0 => $"{ligado.Hours} h",
            1 => $"1 dia e {ligado.Hours} h",
            _ => $"{dias} dias e {ligado.Hours} h",
        };
        return new Avaliacao(gravidade,
            $"sem reiniciar há {tempo} (desde {desde.ToString("dd/MM HH:mm", Textos.Cultura)})");
    }
}
