namespace Triage.Verificacoes.Itens;

public sealed class CacheDoWindowsUpdate : IVerificacao
{
    public string Id => "cache-windows-update";
    public string Titulo => "Cache do Windows Update";
    public string OQueE => "Os pacotes de atualização que o Windows baixou. Depois de instalados, quase sempre sobram.";
    public string PorQueImporta => "Pode passar de vários GB. O Windows baixa de novo o que precisar.";
    public string ComoResolver => "triage corrigir cache-windows-update, como administrador (pede confirmação e para o serviço de atualização enquanto limpa).";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.CacheDoWindowsUpdate.FoiLido)
            return Avaliacao.Pulada(foto.CacheDoWindowsUpdate.Pulo!.Value);
        var pasta = foto.CacheDoWindowsUpdate.Valor!;
        var gravidade = pasta.Bytes >= limites.CacheDoWindowsUpdateAtencaoGb * (1L << 30) ? Gravidade.Atencao : Gravidade.Ok;
        return new Avaliacao(gravidade, $"{Textos.Tamanho(pasta.Bytes)} em {Textos.Numero(pasta.Arquivos)} arquivos");
    }
}
