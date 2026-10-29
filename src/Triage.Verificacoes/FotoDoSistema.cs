using System.Text.Json.Serialization;

namespace Triage.Verificacoes;

/// <summary>
/// Tudo que as verificações precisam do PC, lido de uma vez. Vira JSON no `triage gravar`, então não pode ter
/// nome de usuário, de arquivo ou de pasta (P15): só medidas.
/// </summary>
public sealed record FotoDoSistema(
    DateTimeOffset LidaEm,
    bool Administrador,
    Lido<IReadOnlyList<Volume>> Volumes,
    Lido<IReadOnlyList<Disco>> Discos,
    Lido<Pasta> TempDoUsuario,
    Lido<Pasta> TempDoWindows,
    Lido<Pasta> CacheDoWindowsUpdate,
    Lido<ConteudoDaLixeira> Lixeira,
    Lido<DateTimeOffset> UltimaInicializacao,
    // Lido com valor nulo = o PC não tem bateria (desktop)
    Lido<Bateria?> Bateria)
{
    // Fase 3. Leituras gravadas antes dela não têm estes campos: saem pulados ("este PC não informa")
    private static Lido<T> NaoGravado<T>() => Lido.Pulado<T>(MotivoDoPulo.NaoSuportado);

    /// <summary>Busca online no Windows Update (precisa de rede: falhou = SemConexao).</summary>
    public Lido<AtualizacoesPendentes> Atualizacoes { get; init; } = NaoGravado<AtualizacoesPendentes>();
    /// <summary>O Windows pediu reinício para terminar uma atualização (offline).</summary>
    public Lido<bool> ReinicioPendente { get; init; } = NaoGravado<bool>();
    /// <summary>Última atualização instalada com sucesso, sem contar definições do antivírus (offline). Nulo = nenhuma no histórico.</summary>
    public Lido<DateTimeOffset?> UltimaAtualizacaoInstalada { get; init; } = NaoGravado<DateTimeOffset?>();
    /// <summary>`winget upgrade` (precisa de rede: falhou = SemConexao; sem winget = NaoSuportado).</summary>
    public Lido<IReadOnlyList<ProgramaDesatualizado>> ProgramasDesatualizados { get; init; } = NaoGravado<IReadOnlyList<ProgramaDesatualizado>>();
    /// <summary>Só os que estão ligados (o Gerenciador de Tarefas desliga sem apagar).</summary>
    public Lido<IReadOnlyList<ProgramaNaInicializacao>> Inicializacao { get; init; } = NaoGravado<IReadOnlyList<ProgramaNaInicializacao>>();
    public Lido<IReadOnlyList<DispositivoComErro>> DriversComErro { get; init; } = NaoGravado<IReadOnlyList<DispositivoComErro>>();
    public Lido<EventosDoSistema> Eventos { get; init; } = NaoGravado<EventosDoSistema>();
    public Lido<Antivirus> Antivirus { get; init; } = NaoGravado<Antivirus>();
    public Lido<Firewall> Firewall { get; init; } = NaoGravado<Firewall>();
}

/// <summary>Uma leitura que pode ter falhado: ou tem o valor, ou diz por que não tem.</summary>
public sealed record Lido<T>(T? Valor, MotivoDoPulo? Pulo = null)
{
    [JsonIgnore]
    public bool FoiLido => Pulo is null;
}

public static class Lido
{
    public static Lido<T> Ok<T>(T valor) => new(valor);
    public static Lido<T> Pulado<T>(MotivoDoPulo motivo) => new(default, motivo);
}

/// <summary>Uma unidade fixa, como "C:".</summary>
public sealed record Volume(string Letra, long Total, long Livre, bool DoSistema);

public enum TipoDeDisco { Desconhecido, Hd, Ssd, Nvme }

public enum SaudeDoDisco { Desconhecida, Saudavel, Alerta, Ruim }

/// <summary>Disco físico. O desgaste (0-100% da vida útil prevista) só sai com admin.</summary>
public sealed record Disco(string Modelo, TipoDeDisco Tipo, long Tamanho, SaudeDoDisco Saude,
    int? Desgaste, MotivoDoPulo? DesgasteNaoLido = null);

public sealed record Pasta(long Bytes, int Arquivos);

public sealed record ConteudoDaLixeira(long Bytes, long Itens);

/// <summary>Capacidades em mWh, somando todas as baterias.</summary>
public sealed record Bateria(int CapacidadeDeProjeto, int CapacidadeAtual, int? Ciclos);

public sealed record AtualizacoesPendentes(int Pendentes, int DeSeguranca);

/// <summary>Nome e versões como o winget mostra (P15 permite nome/versão de programa desatualizado).</summary>
public sealed record ProgramaDesatualizado(string Nome, string Id, string Versao, string Disponivel);

/// <summary>
/// Nome do programa como ele mesmo se registrou (valor do registro), nunca o comando ou o caminho. Atalho da pasta
/// Inicializar não leva o nome: é nome de arquivo, e pode ser um documento do usuário (P15).
/// </summary>
public sealed record ProgramaNaInicializacao(string Nome, OrigemDaInicializacao Origem);

public enum OrigemDaInicializacao { RegistroDoUsuario, RegistroDoSistema, PastaDoUsuario, PastaDoSistema }

/// <summary>
/// Dispositivo com erro no Gerenciador de Dispositivos (código do ConfigManagerErrorCode). Sem o nome: aparelho
/// pareado tem nome dado pelo dono ("iPhone de André"), e a P15 não deixa nome de pessoa entrar na foto.
/// </summary>
public sealed record DispositivoComErro(string Classe, int Codigo);

/// <summary>Contagens no log do Sistema nos últimos <see cref="Dias"/> dias.</summary>
public sealed record EventosDoSistema(int Dias, int DesligamentosInesperados, int TelasAzuis);

/// <summary>Produto registrado na Central de Segurança do Windows (não existe no Windows Server).</summary>
public sealed record ProdutoDeSeguranca(string Nome, bool Ativo, bool Atualizado);

/// <summary>Produtos nulos = sem Central de Segurança; campos do Defender nulos = Defender não respondeu.</summary>
public sealed record Antivirus(IReadOnlyList<ProdutoDeSeguranca>? Produtos, bool? DefenderTempoReal, int? DefenderIdadeDasDefinicoes);

public sealed record PerfilDoFirewall(string Nome, bool Ligado);

/// <summary>Perfis do Firewall do Windows (Domínio, Particular, Público) e firewalls de terceiros, se houver.</summary>
public sealed record Firewall(IReadOnlyList<PerfilDoFirewall> Perfis, IReadOnlyList<ProdutoDeSeguranca>? OutrosFirewalls);
