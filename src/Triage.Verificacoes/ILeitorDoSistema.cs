namespace Triage.Verificacoes;

/// <summary>De onde vem a foto do PC: o Windows de verdade (no agente) ou uma leitura gravada (nos testes).</summary>
public interface ILeitorDoSistema
{
    FotoDoSistema Ler();
}
