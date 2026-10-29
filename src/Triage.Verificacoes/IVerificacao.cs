namespace Triage.Verificacoes;

/// <summary>Uma verificação: lê só a foto, nunca o sistema (assim é testada no Linux).</summary>
public interface IVerificacao
{
    string Id { get; }
    string Titulo { get; }
    string OQueE { get; }
    string PorQueImporta { get; }
    string ComoResolver { get; }

    Avaliacao Avaliar(FotoDoSistema foto, Limites limites);
}
