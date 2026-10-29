namespace Triage.Verificacoes.Itens;

public sealed class ArquivosTemporarios : IVerificacao
{
    public string Id => "temporarios";
    public string Titulo => "Arquivos temporários";
    public string OQueE => "Arquivos que programas e o Windows criam para uso passageiro e muitas vezes esquecem de apagar.";
    public string PorQueImporta => "Ocupam espaço sem servir para nada. Apagar é seguro: o que estiver em uso fica.";
    public string ComoResolver => "triage corrigir temporarios (pede confirmação antes). A pasta do Windows só é limpa rodando como administrador.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        Lido<Pasta>[] pastas = [foto.TempDoUsuario, foto.TempDoWindows];
        var lidas = pastas.Where(p => p.FoiLido).Select(p => p.Valor!).ToList();
        if (lidas.Count == 0)
            return Avaliacao.Pulada(foto.TempDoWindows.Pulo ?? foto.TempDoUsuario.Pulo!.Value);

        var bytes = lidas.Sum(p => p.Bytes);
        var resumo = $"{Textos.Tamanho(bytes)} em {Textos.Numero(lidas.Sum(p => p.Arquivos))} arquivos";
        if (foto.TempDoUsuario.Pulo is MotivoDoPulo doUsuario)
            resumo += $" (a pasta do usuário ficou de fora: {Textos.Motivo(doUsuario)})";
        if (foto.TempDoWindows.Pulo is MotivoDoPulo doWindows)
            resumo += $" (a pasta do Windows ficou de fora: {Textos.Motivo(doWindows)})";
        var gravidade = bytes >= limites.TemporariosAtencaoGb * (1L << 30) ? Gravidade.Atencao : Gravidade.Ok;
        return new Avaliacao(gravidade, resumo);
    }
}
