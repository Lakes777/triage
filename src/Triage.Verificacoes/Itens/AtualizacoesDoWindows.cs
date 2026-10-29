namespace Triage.Verificacoes.Itens;

public sealed class AtualizacoesDoWindows : IVerificacao
{
    public string Id => "atualizacoes-windows";
    public string Titulo => "Atualizações do Windows";
    public string OQueE => "Se o Windows tem atualizações esperando para instalar, se pediu para reiniciar e há quanto tempo instalou a última.";
    public string PorQueImporta => "As atualizações fecham brechas de segurança que já estão sendo exploradas. Windows parado há meses é a porta de entrada mais comum de vírus.";
    public string ComoResolver => "Abra Configurações > Windows Update e clique em \"Verificar se há atualizações\". Se ele pedir, reinicie (Iniciar > Energia > Reiniciar). A busca precisa de internet.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        // A parte offline (reinício pendente e última instalação) já pode mostrar um problema sem a busca online
        var offline = new List<(Gravidade Gravidade, string Texto)>();
        if (foto.ReinicioPendente is { FoiLido: true, Valor: true })
            offline.Add((Gravidade.Atencao, "o Windows espera reiniciar para terminar uma atualização"));
        if (foto.UltimaAtualizacaoInstalada.FoiLido)
            offline.Add(UltimaInstalacao(foto.UltimaAtualizacaoInstalada.Valor, foto.LidaEm, limites));

        var gravidades = offline.Select(o => o.Gravidade).ToList();
        var textos = offline.Select(o => o.Texto).ToList();

        if (foto.Atualizacoes.Pulo is MotivoDoPulo motivo)
        {
            var semBusca = $"a busca online não rodou ({Textos.Motivo(motivo)})";
            // Sem a busca, só um problema já visto decide; parte offline boa não basta para dizer "ok" (P7, P25)
            if (Textos.Pior(gravidades) is var pior && pior != Gravidade.Ok)
                return new Avaliacao(pior, string.Join("; ", [.. textos, semBusca]));
            return Avaliacao.Pulada(motivo, string.Join("; ", [semBusca, .. textos]));
        }

        var pendentes = foto.Atualizacoes.Valor!;
        gravidades.Add(pendentes.Pendentes > 0 ? Gravidade.Atencao : Gravidade.Ok);
        textos.Insert(0, pendentes switch
        {
            { Pendentes: 0 } => "nenhuma atualização pendente",
            { Pendentes: 1, DeSeguranca: 1 } => "1 atualização pendente, de segurança",
            { Pendentes: 1 } => "1 atualização pendente",
            { DeSeguranca: 0 } => $"{pendentes.Pendentes} atualizações pendentes",
            _ => $"{pendentes.Pendentes} atualizações pendentes ({pendentes.DeSeguranca} de segurança)",
        });
        if (foto.ReinicioPendente.Pulo is MotivoDoPulo reinicio)
            textos.Add($"reinício pendente não lido: {Textos.Motivo(reinicio)}");
        if (foto.UltimaAtualizacaoInstalada.Pulo is MotivoDoPulo historico)
            textos.Add($"histórico não lido: {Textos.Motivo(historico)}");
        return new Avaliacao(Textos.Pior(gravidades), string.Join("; ", textos));
    }

    private static (Gravidade, string) UltimaInstalacao(DateTimeOffset? ultima, DateTimeOffset agora, Limites limites)
    {
        // Histórico vazio (Windows recém-instalado ou histórico apagado) não é problema por si só
        if (ultima is not DateTimeOffset data)
            return (Gravidade.Ok, "nenhuma atualização no histórico");
        var dias = (agora - data).TotalDays;
        var gravidade = dias switch
        {
            var d when d >= limites.AtualizacaoCriticoDias => Gravidade.Critico,
            var d when d >= limites.AtualizacaoAtencaoDias => Gravidade.Atencao,
            _ => Gravidade.Ok,
        };
        // A data vem em UTC; mostrada no fuso de quando a foto foi lida (o do PC)
        var quando = (int)dias switch
        {
            <= 0 => "há menos de 1 dia",
            1 => "há 1 dia",
            var n => $"há {n} dias",
        };
        return (gravidade, $"última atualização instalada {quando} ({data.ToOffset(agora.Offset).ToString("dd/MM/yyyy", Textos.Cultura)})");
    }
}
