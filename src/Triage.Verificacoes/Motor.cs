using Triage.Verificacoes.Itens;

namespace Triage.Verificacoes;

public sealed class Motor(IReadOnlyList<IVerificacao> verificacoes)
{
    public IReadOnlyList<IVerificacao> Verificacoes => verificacoes;

    public static Motor Padrao() => new(
    [
        new SaudeDosDiscos(),
        new EspacoLivre(),
        new ArquivosTemporarios(),
        new CacheDoWindowsUpdate(),
        new TamanhoDaLixeira(),
        new TempoSemReiniciar(),
        new SaudeDaBateria(),
        new AtualizacoesDoWindows(),
        new ProgramasDesatualizados(),
        new ProgramasNaInicializacao(),
        new DriversComErro(),
        new DesligamentosInesperados(),
        new TelasAzuis(),
        new AntivirusAtivo(),
        new FirewallLigado(),
    ]);

    public Revisao Revisar(FotoDoSistema foto, Limites limites)
    {
        var resultados = verificacoes
            .Select(v => new Resultado(v.Id, v.Titulo, AvaliarSemQuebrar(v, foto, limites),
                v.OQueE, v.PorQueImporta, v.ComoResolver))
            // Críticos primeiro (P18); OrderBy é estável, então dentro da mesma gravidade fica a ordem da lista
            .OrderBy(r => Ordem(r.Avaliacao.Gravidade))
            .ToList();
        return new Revisao(foto.LidaEm, foto.Administrador, resultados);
    }

    // Uma verificação com defeito não derruba a revisão inteira, e o item sai pulado, nunca "ok"
    private static Avaliacao AvaliarSemQuebrar(IVerificacao verificacao, FotoDoSistema foto, Limites limites)
    {
        try
        {
            return verificacao.Avaliar(foto, limites);
        }
        catch (Exception erro)
        {
            return Avaliacao.Pulada(MotivoDoPulo.Erro, $"erro ao verificar: {erro.Message}");
        }
    }

    private static int Ordem(Gravidade g) => g switch
    {
        Gravidade.Critico => 0,
        Gravidade.Atencao => 1,
        Gravidade.Pulado => 2,
        Gravidade.Ok => 3,
        _ => 4,
    };
}
