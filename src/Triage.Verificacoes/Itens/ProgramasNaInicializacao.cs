namespace Triage.Verificacoes.Itens;

public sealed class ProgramasNaInicializacao : IVerificacao
{
    public string Id => "inicializacao";
    public string Titulo => "Programas na inicialização";
    public string OQueE => "Programas que abrem sozinhos quando você entra no Windows (os mesmos da aba \"Aplicativos de inicialização\" do Gerenciador de Tarefas).";
    public string PorQueImporta => "Cada um atrasa a entrada no Windows e fica ocupando memória o tempo todo, mesmo sem você usar.";
    public string ComoResolver => "Abra o Gerenciador de Tarefas (Ctrl+Shift+Esc) > Aplicativos de inicialização e desabilite o que você não precisa logo ao ligar o PC. Desabilitar não desinstala nada.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Inicializacao.FoiLido)
            return Avaliacao.Pulada(foto.Inicializacao.Pulo!.Value);
        var programas = foto.Inicializacao.Valor!;
        var gravidade = programas.Count >= limites.InicializacaoAtencao ? Gravidade.Atencao : Gravidade.Ok;
        var resumo = programas.Count switch
        {
            0 => "nenhum programa abre sozinho",
            1 => $"1 programa: {programas[0].Nome}",
            var n => $"{n} programas: {Resumos.Alguns(programas.Select(p => p.Nome).ToList())}",
        };
        return new Avaliacao(gravidade, resumo);
    }
}
