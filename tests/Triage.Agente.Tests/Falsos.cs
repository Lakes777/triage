using Grpc.Core;
using Triage.Agente;
using Triage.Contratos.Grpc;
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

/// <summary>"Protege" invertendo os bytes: o arquivo gravado nunca é a chave em claro, e dá para conferir isso.</summary>
public sealed class CofreFalso : ICofre
{
    public byte[] Proteger(string segredo) => System.Text.Encoding.UTF8.GetBytes(segredo).Reverse().ToArray();
    public string Abrir(byte[] protegido) => System.Text.Encoding.UTF8.GetString(protegido.Reverse().ToArray());
}

/// <summary>Um servidor de mentira: guarda o que recebeu e responde o que o teste mandar.</summary>
public sealed class ServidorFalso : IServidorDoTriage
{
    public List<EnviarRevisaoPedido> Recebidos { get; } = [];
    public List<(string Maquina, string Chave)> Credenciais { get; } = [];
    public Queue<Envio> Respostas { get; } = new();
    public Envio Padrao { get; set; } = new(RespostaDoEnvio.Entregue);
    public RpcException? ErroNaInscricao { get; set; }
    public Exception? ErroNoEnvio { get; set; }

    /// <summary>Roda no meio do envio: simula outra execução mexendo na fila ao mesmo tempo.</summary>
    public Action? AoEnviar { get; set; }
    public List<Uri> Enderecos { get; } = [];

    public Task<(string MaquinaId, string Chave)> Inscrever(string codigo, string apelido, CancellationToken cancelar) =>
        ErroNaInscricao is { } erro
            ? throw erro
            : Task.FromResult(("0193f0c4-0000-7000-8000-000000000001", "chave-secreta-da-maquina"));

    public Task<Envio> Enviar(EnviarRevisaoPedido pedido, string maquinaId, string chave, CancellationToken cancelar)
    {
        if (ErroNoEnvio is { } erro)
            throw erro;
        AoEnviar?.Invoke();
        Credenciais.Add((maquinaId, chave));
        var resposta = Respostas.Count > 0 ? Respostas.Dequeue() : Padrao;
        if (resposta.Resposta == RespostaDoEnvio.Entregue)
            Recebidos.Add(pedido);
        return Task.FromResult(resposta);
    }
}

/// <summary>A DPAPI que não abre mais (senha da conta redefinida, perfil em outro PC) ou que nem grava.</summary>
public sealed class CofreQuebrado(bool tambemNaoGrava = false) : ICofre
{
    public byte[] Proteger(string segredo) =>
        tambemNaoGrava ? throw new System.Security.Cryptography.CryptographicException("DPAPI indisponível") : [1, 2, 3];

    public string Abrir(byte[] protegido) =>
        throw new System.Security.Cryptography.CryptographicException("Key not valid for use in specified state.");
}
