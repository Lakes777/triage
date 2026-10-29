namespace Triage.Verificacoes;

public enum Gravidade
{
    Ok,
    Atencao,
    Critico,
    /// <summary>Não deu para ler (sem admin, sem conexão...). Nunca vira "ok" (P7, P25).</summary>
    Pulado,
    /// <summary>O item não existe neste PC, como a bateria num desktop.</summary>
    NaoSeAplica,
}

public enum MotivoDoPulo
{
    SemAdmin,
    SemConexao,
    SemPermissao,
    NaoSuportado,
    Erro,
}
