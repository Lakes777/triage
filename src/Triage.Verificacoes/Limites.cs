using System.Text.Json;
using System.Text.Json.Serialization;

namespace Triage.Verificacoes;

/// <summary>
/// Onde cada medida vira "atenção" ou "crítico" (P18). Estes são os padrões; o `limites.json` do usuário pode
/// trocar qualquer um (o que ele não citar fica igual).
/// </summary>
public sealed record Limites
{
    // Regra prática, sem número oficial único: com pouco espaço o Windows não instala as atualizações grandes
    // e o SSD perde desempenho
    public double EspacoLivreAtencaoPorcento { get; init; } = 15;
    public double EspacoLivreCriticoPorcento { get; init; } = 5;

    // Especificação NVMe, campo "Percentage Used": 100% = a vida útil prevista pelo fabricante acabou
    public int DesgasteAtencaoPorcento { get; init; } = 70;
    public int DesgasteCriticoPorcento { get; init; } = 90;

    // Fabricantes (a Apple, por exemplo) tratam 80% da capacidade original como o fim da vida útil prevista
    // da bateria; 60% é regra prática (a autonomia já caiu pela metade perto disso)
    public double BateriaAtencaoPorcento { get; init; } = 80;
    public double BateriaCriticoPorcento { get; init; } = 60;

    // As correções do Windows saem uma vez por mês e muitas só valem depois de reiniciar: 30 dias = um mês de
    // correções esperando. Regra prática para a atenção
    public double ReinicioAtencaoDias { get; init; } = 7;
    public double ReinicioCriticoDias { get; init; } = 30;

    // Regra prática: abaixo disso, limpar não faz diferença que se note
    public double TemporariosAtencaoGb { get; init; } = 2;
    public double CacheDoWindowsUpdateAtencaoGb { get; init; } = 2;
    public double LixeiraAtencaoGb { get; init; } = 2;

    // Fase 3. Regra prática: as correções saem uma vez por mês, então 30 dias sem instalar nada = um mês perdido
    public double AtualizacaoAtencaoDias { get; init; } = 30;
    public double AtualizacaoCriticoDias { get; init; } = 60;

    // Regra prática: cada programa a mais na inicialização atrasa o boot e fica ocupando memória
    public int InicializacaoAtencao { get; init; } = 10;

    // Um desligamento inesperado pode ser a energia; vários no mês apontam fonte, bateria ou superaquecimento
    public int DesligamentosAtencao { get; init; } = 1;
    public int DesligamentosCritico { get; init; } = 3;

    // Uma tela azul isolada acontece; repetir no mês aponta driver, memória ou disco com defeito
    public int TelasAzuisAtencao { get; init; } = 1;
    public int TelasAzuisCritico { get; init; } = 2;

    // O Defender atualiza as definições várias vezes por dia: uma semana parado já é sinal de problema
    public int DefinicoesAtencaoDias { get; init; } = 7;
    public int DefinicoesCriticoDias { get; init; } = 14;

    // Regra prática: alguns sempre estão atrasados; muitos de uma vez mostram que ninguém atualiza
    public int ProgramasDesatualizadosAtencao { get; init; } = 5;

    public static Limites Padrao { get; } = new();

    // Chave com erro de digitação é recusada: ignorada, o usuário acharia que mudou o limite
    private static readonly JsonSerializerOptions Opcoes = new(Gravacao.Opcoes)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static Limites DeJson(string json) =>
        JsonSerializer.Deserialize<Limites>(json, Opcoes)
        ?? throw new JsonException("O arquivo de limites está vazio.");
}
