using System.Net;
using System.Text.RegularExpressions;

namespace Triage.Relatorio.Tests;

/// <summary>
/// O que o relatório não pode ter (P20): nada que busque algo fora do arquivo nem nada que execute ação.
/// O relatório hoje não tem nenhum link, então a regra é rígida: qualquer endereço que não seja âncora
/// (#item-...) já é problema, porque um link ms-msdt:, search-ms: ou file:// abre programa no Windows com um
/// clique, e a política de segurança da página não barra clique nem meta refresh. Devolve o que achou; vazio = ok.
/// </summary>
public static partial class HtmlSeguro
{
    public static IReadOnlyList<string> Problemas(string html)
    {
        var achados = new List<string>();
        void Procurar(Regex regra, string texto, string nome)
        {
            foreach (Match m in regra.Matches(texto))
                achados.Add($"{nome}: {m.Value}");
        }
        Procurar(Script(), html, "script");
        Procurar(EnderecoExterno(), html, "endereço externo");
        Procurar(AtributoDeEndereco(), html, "endereço que não é âncora");
        Procurar(AtributoDeEvento(), html, "atributo de evento");
        Procurar(EtiquetaQueBuscaOuEnvia(), html, "etiqueta que busca ou envia");
        Procurar(MetaRefresh(), html, "meta refresh");
        Procurar(CssQueBusca(), html, "CSS que busca arquivo");
        // Entidade (jav&#x61;script) e tab no meio (java	script) ainda funcionam no navegador
        var decodificado = EspacoOuControle().Replace(WebUtility.HtmlDecode(html), "");
        Procurar(JavaScriptNoEndereco(), decodificado, "javascript: no endereço");
        return achados;
    }

    [GeneratedRegex(@"<\s*script", RegexOptions.IgnoreCase)]
    private static partial Regex Script();

    [GeneratedRegex(@"(?:https?|ftp|wss?|file)://", RegexOptions.IgnoreCase)]
    private static partial Regex EnderecoExterno();

    // Dentro de uma etiqueta, qualquer atributo de endereço cujo valor não comece com #
    [GeneratedRegex(@"<[^>]*[\s/""'](?:href|xlink:href|src|srcset|action|formaction|data|poster|background|ping)\s*=\s*(?![""']?#)", RegexOptions.IgnoreCase)]
    private static partial Regex AtributoDeEndereco();

    // A barra também separa atributo: <svg/onload=...>
    [GeneratedRegex(@"<[^>]*[\s/""']on[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex AtributoDeEvento();

    [GeneratedRegex(@"<\s*(?:iframe|frame|object|embed|form|input|button|link|img|image|video|audio|source|base|svg|math)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EtiquetaQueBuscaOuEnvia();

    [GeneratedRegex(@"<\s*meta\b[^>]*refresh", RegexOptions.IgnoreCase)]
    private static partial Regex MetaRefresh();

    // url(), \75rl( (u escapado), image-set() e @import
    [GeneratedRegex(@"url\s*\(|\\0*75\s*rl|image-set\s*\(|@import", RegexOptions.IgnoreCase)]
    private static partial Regex CssQueBusca();

    [GeneratedRegex(@"[\s\x00-\x1f]")]
    private static partial Regex EspacoOuControle();

    [GeneratedRegex(@"javascript:|vbscript:", RegexOptions.IgnoreCase)]
    private static partial Regex JavaScriptNoEndereco();
}
