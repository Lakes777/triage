namespace Triage.Servidor.Banco;

/// <summary>Um PC inscrito. O servidor só conhece o apelido (P15); a chave fica guardada como SHA-256.</summary>
public sealed class Maquina
{
    public Guid Id { get; init; }
    public required string Apelido { get; set; }
    public required byte[] ChaveHash { get; init; }
    public DateTimeOffset InscritaEm { get; init; }
    public DateTimeOffset? RevogadaEm { get; set; }

    /// <summary>Da frota inventada da demonstração: o visitante sem login só vê estas (P31).</summary>
    public bool Demo { get; init; }
}

/// <summary>Código de inscrição: uso único, 24 h, guardado só como hash (P10).</summary>
public sealed class CodigoDeInscricao
{
    public Guid Id { get; init; }
    public required byte[] CodigoHash { get; init; }
    public DateTimeOffset CriadoEm { get; init; }
    public DateTimeOffset ExpiraEm { get; init; }
    public DateTimeOffset? UsadoEm { get; set; }
    public Guid? MaquinaId { get; set; }
}

/// <summary>Uma revisão recebida. O id veio do agente; (máquina, id) é a chave, então repetir o envio não duplica.</summary>
public sealed class RevisaoGravada
{
    public Guid MaquinaId { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset FeitaEm { get; init; }
    public DateTimeOffset RecebidaEm { get; init; }
    public required string VersaoDoAgente { get; init; }
    public bool Administrador { get; init; }

    /// <summary>SHA-256 do pedido inteiro: o mesmo id com outro conteúdo é recusado.</summary>
    public required byte[] ConteudoHash { get; init; }

    /// <summary>A foto do sistema (jsonb), só medidas.</summary>
    public required string Foto { get; init; }
}

public sealed class ResultadoGravado
{
    public Guid MaquinaId { get; init; }
    public Guid RevisaoId { get; init; }
    public required string Item { get; init; }
    public required string Gravidade { get; init; }
    public string? Motivo { get; init; }
}

/// <summary>
/// Uma piora a avisar no Telegram (P19). Nasce na mesma transação da revisão, uma por (máquina, revisão, item):
/// repetir o envio não repete o aviso. EnviadoEm é marcado por um UPDATE condicional antes de mandar.
/// </summary>
public sealed class Aviso
{
    public Guid Id { get; init; }
    public Guid MaquinaId { get; init; }
    public Guid RevisaoId { get; init; }
    public required string Item { get; init; }
    public required string De { get; init; }
    public required string Para { get; init; }

    /// <summary>Texto extra (o modelo do disco no desgaste do SSD).</summary>
    public string? Detalhe { get; init; }
    public DateTimeOffset CriadoEm { get; init; }
    public DateTimeOffset? EnviadoEm { get; set; }
    public int Tentativas { get; set; }
}
