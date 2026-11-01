using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// O que as correções tocam no Windows. Atrás de uma interface para os testes rodarem numa pasta falsa, com
/// lixeira e serviço falsos (SPEC: correção só é testada em pasta falsa).
/// </summary>
public interface IAmbienteDeCorrecao
{
    bool Administrador { get; }

    /// <summary>Lido uma vez por comando: o mesmo horário vale para o plano mostrado e para o que é apagado.</summary>
    DateTimeOffset Agora { get; }
    string TempDoUsuario { get; }
    string TempDoWindows { get; }
    string CacheDoWindowsUpdate { get; }

    ConteudoDaLixeira LerLixeira();
    void EsvaziarLixeira();

    /// <summary>Se o serviço do Windows Update está rodando agora (para religar depois só nesse caso).</summary>
    bool WindowsUpdateRodando();

    /// <summary>Para o serviço e espera parar; se já estava parando sozinho, só espera.</summary>
    void PararWindowsUpdate();

    void IniciarWindowsUpdate();
}
