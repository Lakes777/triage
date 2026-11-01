using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Triage.Verificacoes;

namespace Triage.Relatorio;

/// <summary>
/// Transforma a revisão num arquivo HTML que se basta: os componentes Razor renderizados fora de um servidor
/// (HtmlRenderer), com o CSS dentro e nenhum script ou endereço externo (P20, P22).
/// </summary>
public static class GeradorDeRelatorio
{
    /// <summary>
    /// Política de segurança da página: nada de fora, nenhum script, nenhum formulário. Só o estilo embutido.
    /// </summary>
    public const string Politica =
        "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'";

    public static async Task<string> GerarAsync(Revisao revisao, string versao)
    {
        // O codificador padrão troca todo acento por entidade (&#xFA;): continua seguro, mas o arquivo fica ilegível.
        // Este só escapa o que é perigoso no HTML (< > & aspas), como o Blazor faz no painel
        await using var servicos = new ServiceCollection()
            .AddSingleton(HtmlEncoder.Create(UnicodeRanges.All))
            .BuildServiceProvider();
        await using var renderizador = new HtmlRenderer(servicos, NullLoggerFactory.Instance);
        return await renderizador.Dispatcher.InvokeAsync(async () =>
        {
            var parametros = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PaginaDoRelatorio.Revisao)] = revisao,
                [nameof(PaginaDoRelatorio.Versao)] = versao,
            });
            var pagina = await renderizador.RenderComponentAsync<PaginaDoRelatorio>(parametros);
            return pagina.ToHtmlString();
        });
    }

    /// <summary>
    /// Para quem não é assíncrono (o terminal do agente): sem contexto de sincronização, não trava. No painel
    /// (fase 9), dentro de um componente, use <see cref="GerarAsync"/>: esperar aqui travaria o circuito.
    /// </summary>
    public static string Gerar(Revisao revisao, string versao) => GerarAsync(revisao, versao).GetAwaiter().GetResult();
}
