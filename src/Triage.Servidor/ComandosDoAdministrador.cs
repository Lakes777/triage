namespace Triage.Servidor;

/// <summary>
/// Comandos rodados na VM (dotnet Triage.Servidor.dll &lt;comando&gt;, ou dotnet run -- &lt;comando&gt;) em vez de
/// subir o servidor. Não há rota pública para nada disso: quem gera código e revoga é quem tem acesso à VM
/// (P10; o painel da fase 9 ganha os mesmos botões, só para o administrador logado).
/// </summary>
public static class ComandosDoAdministrador
{
    public const string Uso = """
        Comandos do administrador (em vez de subir o servidor):
          gerar-codigo          código de inscrição de uso único, vale 24 h
          maquinas              lista as máquinas inscritas
          revogar <maquina-id>  corta a chave da máquina (ela para de conseguir enviar)
        """;

    /// <summary>Nulo = não é comando, sobe o servidor. Senão, a ação a rodar (ou o uso, se veio errado).</summary>
    public static Func<Inscricoes, TextWriter, TextWriter, Task<int>>? Interpretar(string[] args) => args switch
    {
        ["gerar-codigo"] => GerarCodigo,
        ["maquinas"] => Listar,
        ["revogar", var id] => (i, s, e) => Revogar(i, s, e, id),
        ["gerar-codigo" or "maquinas" or "revogar", ..] => (_, _, e) => UsoErrado(e),
        _ => null,
    };

    private static async Task<int> GerarCodigo(Inscricoes inscricoes, TextWriter saida, TextWriter erros)
    {
        var codigo = await inscricoes.GerarCodigo();
        saida.WriteLine($"Código de inscrição: {codigo}");
        saida.WriteLine($"Vale por {Regras.ValidadeDoCodigo.TotalHours:0} h e uma vez só. No PC: triage inscrever {codigo} <apelido>");
        return 0;
    }

    private static async Task<int> Listar(Inscricoes inscricoes, TextWriter saida, TextWriter erros)
    {
        var maquinas = await inscricoes.Listar();
        if (maquinas.Count == 0)
            saida.WriteLine("Nenhuma máquina inscrita.");
        foreach (var m in maquinas)
        {
            // O banco guarda em UTC, e a VM roda em UTC: a hora sai com o fuso escrito, para não confundir
            var estado = m.RevogadaEm is { } quando ? $"revogada em {quando:yyyy-MM-dd HH:mm} UTC" : "ativa";
            saida.WriteLine($"{m.Id}  {m.Apelido,-40}  inscrita em {m.InscritaEm:yyyy-MM-dd HH:mm} UTC  {estado}");
        }
        return 0;
    }

    private static async Task<int> Revogar(Inscricoes inscricoes, TextWriter saida, TextWriter erros, string texto)
    {
        if (!Guid.TryParse(texto, out var id))
        {
            erros.WriteLine($"Id de máquina inválido: {texto} (veja os ids com o comando maquinas).");
            return 2;
        }
        if (!await inscricoes.Revogar(id))
        {
            erros.WriteLine("Nenhuma máquina ativa com esse id (não existe ou já estava revogada).");
            return 1;
        }
        saida.WriteLine("Máquina revogada: os próximos envios dela são recusados.");
        return 0;
    }

    private static Task<int> UsoErrado(TextWriter erros)
    {
        erros.WriteLine(Uso);
        return Task.FromResult(2);
    }
}
