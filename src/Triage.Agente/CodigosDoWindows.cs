using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// Os valores crus que o Windows devolve, traduzidos. Fica fora do <see cref="LeitorDoWindows"/> (que só roda no
/// Windows) para ser testado no Linux com os números de verdade.
/// </summary>
public static class CodigosDoWindows
{
    // ConfigManagerErrorCode: 22 = desativado pelo usuário (de propósito); 45 = o aparelho não está conectado
    // agora (um pen drive que já foi usado). Nenhum dos dois é driver com defeito
    private const int Desativado = 22;
    private const int Desconectado = 45;

    /// <summary>
    /// O Gerenciador de Tarefas desliga um programa da inicialização sem apagá-lo: grava um valor binário em
    /// <c>Explorer\StartupApproved</c> com o mesmo nome. Primeiro byte ímpar (0x03, 0x01) = desligado; par (0x02,
    /// 0x06) ou sem valor = ligado.
    /// </summary>
    public static bool InicializacaoLigada(byte[]? aprovacao) => aprovacao is not { Length: > 0 } || aprovacao[0] % 2 == 0;

    /// <summary>
    /// Nome que vai para a foto. Alguns programas usam o caminho do .exe como nome do valor no registro: aí fica só
    /// a última parte, porque caminho pode ter o nome do usuário (P15).
    /// </summary>
    public static string SemCaminho(string nome)
    {
        var semBarraNoFim = nome.Trim().TrimEnd('\\', '/');
        var limpo = semBarraNoFim[(semBarraNoFim.LastIndexOfAny(['\\', '/']) + 1)..].Trim();
        return limpo.Length > 0 ? limpo : "sem nome";
    }

    /// <summary>Sem código (nulo) não é "código 0": quem não informa fica de fora em vez de virar um erro inventado.</summary>
    public static bool DriverComErro(int? codigo) => codigo is not (null or 0 or Desativado or Desconectado);

    /// <summary>
    /// productState da Central de Segurança (root\SecurityCenter2). A Microsoft não documenta este número: a leitura
    /// abaixo é a heurística conhecida, conferida na prática (o Defender ligado e em dia dá 397568 = 0x61100;
    /// desligado, 393472 = 0x60100). Bit 0x1000 = ligado; bit 0x10 = definições desatualizadas.
    /// </summary>
    public static ProdutoDeSeguranca ProdutoDaCentral(string? nome, uint estado) => new(
        string.IsNullOrWhiteSpace(nome) ? "sem nome" : nome.Trim(),
        Ativo: (estado & 0x1000) != 0,
        Atualizado: (estado & 0x10) == 0);
}
