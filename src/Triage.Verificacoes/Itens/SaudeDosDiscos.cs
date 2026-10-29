namespace Triage.Verificacoes.Itens;

public sealed class SaudeDosDiscos : IVerificacao
{
    public string Id => "disco-saude";
    public string Titulo => "Saúde dos discos";
    public string OQueE => "O que o próprio disco informa sobre a saúde dele e, nos SSDs, quanto da vida útil prevista já foi usada.";
    public string PorQueImporta => "Disco que está falhando perde arquivos sem aviso. Com antecedência dá para copiar tudo e trocar com calma.";
    public string ComoResolver => "Faça backup agora dos arquivos importantes. Disco em alerta ou muito gasto: planeje a troca. Para ver o desgaste, rode o Triage como administrador.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Discos.FoiLido)
            return Avaliacao.Pulada(foto.Discos.Pulo!.Value);
        var discos = foto.Discos.Valor!;
        // Saúde desconhecida com desgaste lido ainda dá para avaliar: um SSD 95% gasto não pode sair "ok" por omissão
        var conhecidos = discos.Where(d => d.Saude != SaudeDoDisco.Desconhecida || d.Desgaste is not null).ToList();
        if (conhecidos.Count == 0)
            return Avaliacao.Pulada(MotivoDoPulo.NaoSuportado,
                discos.Count == 0 ? "nenhum disco encontrado" : "os discos não informam a saúde");

        var gravidade = Textos.Pior(conhecidos.Select(d => GravidadeDo(d, limites)));
        return new Avaliacao(gravidade, string.Join("; ", discos.Select(Descrever)));
    }

    private static Gravidade GravidadeDo(Disco disco, Limites limites)
    {
        var pelaSaude = disco.Saude switch
        {
            SaudeDoDisco.Ruim => Gravidade.Critico,
            SaudeDoDisco.Alerta => Gravidade.Atencao,
            _ => Gravidade.Ok,
        };
        var peloDesgaste = disco.Desgaste switch
        {
            int d when d >= limites.DesgasteCriticoPorcento => Gravidade.Critico,
            int d when d >= limites.DesgasteAtencaoPorcento => Gravidade.Atencao,
            _ => Gravidade.Ok,
        };
        return Textos.Pior([pelaSaude, peloDesgaste]);
    }

    private static string Descrever(Disco disco)
    {
        var saude = disco.Saude switch
        {
            SaudeDoDisco.Saudavel => "saudável",
            SaudeDoDisco.Alerta => "em alerta",
            SaudeDoDisco.Ruim => "falhando",
            _ => "saúde desconhecida",
        };
        var desgaste = disco.Desgaste is int d
            ? $", desgaste {d}%"
            : disco.DesgasteNaoLido is MotivoDoPulo m ? $" (desgaste: {Textos.Motivo(m)})" : "";
        var tipo = disco.Tipo switch
        {
            TipoDeDisco.Nvme => "SSD NVMe",
            TipoDeDisco.Ssd => "SSD",
            TipoDeDisco.Hd => "HD",
            _ => "disco",
        };
        return $"{disco.Modelo} ({tipo}, {Textos.Tamanho(disco.Tamanho)}): {saude}{desgaste}";
    }
}
