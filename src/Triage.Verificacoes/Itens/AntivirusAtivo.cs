namespace Triage.Verificacoes.Itens;

public sealed class AntivirusAtivo : IVerificacao
{
    public string Id => "antivirus";
    public string Titulo => "Antivírus";
    public string OQueE => "Se há um antivírus protegendo o PC em tempo real (o Microsoft Defender, que já vem no Windows, ou outro) e se as definições de vírus dele estão em dia.";
    public string PorQueImporta => "Sem antivírus ativo, um arquivo infectado roda sem nada para barrar. Com definições velhas, ele não reconhece as ameaças novas.";
    public string ComoResolver => "Abra Segurança do Windows > Proteção contra vírus e ameaças, ligue a proteção em tempo real e clique em \"Verificar se há atualizações\". Se você usa outro antivírus, abra-o e atualize por ele.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Antivirus.FoiLido)
            return Avaliacao.Pulada(foto.Antivirus.Pulo!.Value);
        var antivirus = foto.Antivirus.Valor!;
        if (antivirus.Produtos is null && antivirus.DefenderTempoReal is null)
            return Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "nem a Central de Segurança nem o Defender responderam");

        var ativos = antivirus.Produtos?.Where(p => p.Ativo).ToList() ?? [];
        // Com antivírus de outra marca ativo, o Windows desliga o Defender de propósito: isso é normal
        var defenderAtivo = antivirus.DefenderTempoReal == true;
        if (ativos.Count == 0 && !defenderAtivo)
            return new Avaliacao(Gravidade.Critico, "nenhum antivírus com a proteção em tempo real ligada");

        var nomes = ativos.Select(p => p.Nome).ToList();
        if (defenderAtivo && !nomes.Any(n => n.Contains("Defender", StringComparison.OrdinalIgnoreCase)))
            nomes.Add("Microsoft Defender");
        var gravidades = new List<Gravidade>();
        var detalhes = new List<string>();
        foreach (var velho in ativos.Where(p => !p.Atualizado))
        {
            gravidades.Add(Gravidade.Atencao);
            detalhes.Add($"{velho.Nome} com definições desatualizadas");
        }
        // A idade das definições do Defender só vale com ele ligado: desligado, ela envelhece sem importar
        if (defenderAtivo)
        {
            if (antivirus.DefenderIdadeDasDefinicoes is int idade)
            {
                gravidades.Add(idade >= limites.DefinicoesCriticoDias ? Gravidade.Critico
                    : idade >= limites.DefinicoesAtencaoDias ? Gravidade.Atencao
                    : Gravidade.Ok);
                detalhes.Add(idade == 0 ? "definições do Defender de hoje" : $"definições do Defender de {Resumos.Dias(idade)} atrás");
            }
            else
                detalhes.Add("o Defender não informou a idade das definições");
        }
        return new Avaliacao(Textos.Pior(gravidades), string.Join("; ", [$"ativo: {string.Join(", ", nomes)}", .. detalhes]));
    }
}
