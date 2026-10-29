namespace Triage.Verificacoes.Itens;

public sealed class DriversComErro : IVerificacao
{
    public string Id => "drivers";
    public string Titulo => "Drivers com erro";
    public string OQueE => "Aparelhos que o Windows marca com erro no Gerenciador de Dispositivos (o triângulo amarelo): o driver falta, falhou ou não iniciou.";
    public string PorQueImporta => "O aparelho com problema (Wi-Fi, som, câmera, USB...) pode parar de funcionar, e driver com defeito também causa travamentos e telas azuis.";
    public string ComoResolver => "Abra o Gerenciador de Dispositivos (botão direito no Iniciar), clique duas vezes no item com aviso e use \"Atualizar driver\". Se não resolver, baixe o driver no site do fabricante do PC; o código do erro ajuda a pesquisar.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.DriversComErro.FoiLido)
            return Avaliacao.Pulada(foto.DriversComErro.Pulo!.Value);
        var dispositivos = foto.DriversComErro.Valor!;
        if (dispositivos.Count == 0)
            return new Avaliacao(Gravidade.Ok, "nenhum aparelho com erro");
        return new Avaliacao(Gravidade.Atencao,
            Resumos.Alguns(dispositivos.Select(d => $"{d.Classe} (código {d.Codigo})").ToList()));
    }
}
