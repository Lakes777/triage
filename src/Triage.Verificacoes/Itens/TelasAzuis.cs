namespace Triage.Verificacoes.Itens;

public sealed class TelasAzuis : IVerificacao
{
    public string Id => "telas-azuis";
    public string Titulo => "Telas azuis";
    public string OQueE => "Vezes em que o Windows parou com a tela azul de erro (\"Seu PC encontrou um problema\") e reiniciou sozinho.";
    public string PorQueImporta => "Uma isolada acontece. Repetindo, quase sempre é driver com defeito, memória RAM ou disco falhando, e tende a piorar.";
    public string ComoResolver => "Instale as atualizações do Windows e dos drivers (principalmente o de vídeo). Se continuar, rode o \"Diagnóstico de Memória do Windows\" e confira a saúde do disco neste relatório.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Eventos.FoiLido)
            return Avaliacao.Pulada(foto.Eventos.Pulo!.Value);
        var eventos = foto.Eventos.Valor!;
        return new Avaliacao(
            Resumos.PelaContagem(eventos.TelasAzuis, limites.TelasAzuisAtencao, limites.TelasAzuisCritico),
            Resumos.Eventos(eventos.TelasAzuis, eventos.Dias, feminino: true));
    }
}
