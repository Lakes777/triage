using Triage.Agente;
using Triage.Verificacoes;

namespace Triage.Agente.Tests;

/// <summary>Um "Windows" numa pasta temporária: as correções só são testadas aqui, nunca no PC de verdade.</summary>
public sealed class AmbienteFalso : IAmbienteDeCorrecao, IDisposable
{
    public string Raiz { get; } = Directory.CreateTempSubdirectory("triage-correcoes-").FullName;
    public bool Administrador { get; set; }
    public DateTimeOffset Agora { get; } = DateTimeOffset.Now;
    public string TempDoUsuario { get; set; }
    public string TempDoWindows { get; }
    public string CacheDoWindowsUpdate { get; }

    public ConteudoDaLixeira Lixeira { get; set; } = new(0, 0);
    public bool ServicoRodando { get; set; } = true;
    public bool LixeiraQuebrada { get; set; }
    public bool LeituraDaLixeiraQuebrada { get; set; }
    public bool ServicoNaoPara { get; set; }
    public List<string> Chamadas { get; } = [];

    public AmbienteFalso()
    {
        TempDoUsuario = Directory.CreateDirectory(Path.Combine(Raiz, "Usuario", "AppData", "Local", "Temp")).FullName;
        TempDoWindows = Directory.CreateDirectory(Path.Combine(Raiz, "Windows", "Temp")).FullName;
        CacheDoWindowsUpdate = Directory.CreateDirectory(
            Path.Combine(Raiz, "Windows", "SoftwareDistribution", "Download")).FullName;
    }

    public ConteudoDaLixeira LerLixeira() =>
        LeituraDaLixeiraQuebrada ? throw new IOException(@"falhou em C:\Users\andre\lixeira") : Lixeira;

    public void EsvaziarLixeira()
    {
        Chamadas.Add("esvaziar lixeira");
        if (LixeiraQuebrada)
            throw new IOException("lixeira travada");
        Lixeira = new ConteudoDaLixeira(0, 0);
    }

    public bool WindowsUpdateRodando() => ServicoRodando;

    public void PararWindowsUpdate()
    {
        Chamadas.Add("parar wuauserv");
        if (ServicoNaoPara)
            throw new System.TimeoutException("o serviço não parou em 60 s");
        ServicoRodando = false;
    }

    public void IniciarWindowsUpdate()
    {
        Chamadas.Add("iniciar wuauserv");
        ServicoRodando = true;
    }

    /// <summary>Cria um arquivo com a idade pedida (antigo = mais de 24 h, entra na limpeza).</summary>
    public string Arquivo(string pasta, string nome, int bytes = 1000, bool antigo = true)
    {
        var caminho = Path.Combine(pasta, nome);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllBytes(caminho, new byte[bytes]);
        if (antigo)
            File.SetLastWriteTimeUtc(caminho, Agora.UtcDateTime.AddDays(-3));
        return caminho;
    }

    public void Dispose() => Directory.Delete(Raiz, recursive: true);
}

public sealed class AgendadorFalso : IAgendador
{
    public Dictionary<string, string> Tarefas { get; } = [];

    public void Criar(string nome, string xml) => Tarefas[nome] = xml;

    public bool Apagar(string nome) => Tarefas.Remove(nome);
}
