namespace Triage.Verificacoes.Itens;

public sealed class FirewallLigado : IVerificacao
{
    public string Id => "firewall";
    public string Titulo => "Firewall";
    public string OQueE => "Se o firewall, que barra conexões não autorizadas vindas da rede, está ligado nos três perfis do Windows: Domínio, Particular e Público.";
    public string PorQueImporta => "Sem firewall, qualquer aparelho na mesma rede pode tentar entrar no PC. O perfil Público é o do Wi-Fi de café, aeroporto e faculdade: o mais exposto.";
    public string ComoResolver => "Abra Segurança do Windows > Firewall e proteção de rede e ligue o firewall em cada perfil. Se você usa o firewall de outro programa, confira nele se está ativo.";

    // O Windows informa o nome do perfil em inglês, em qualquer idioma
    private const string Publico = "Public";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.Firewall.FoiLido)
            return Avaliacao.Pulada(foto.Firewall.Pulo!.Value);
        var firewall = foto.Firewall.Valor!;
        var desligados = firewall.Perfis.Where(p => !p.Ligado).Select(p => Traduzir(p.Nome)).ToList();

        // Com firewall de outra marca ativo, o do Windows pode ficar desligado de propósito
        var terceiros = firewall.OutrosFirewalls?.Where(p => p.Ativo).Select(p => p.Nome).ToList() ?? [];
        if (terceiros.Count > 0)
            return new Avaliacao(Gravidade.Ok, $"protegido por {string.Join(", ", terceiros)}"
                + (desligados.Count > 0 ? $" (o do Windows está desligado em: {string.Join(", ", desligados)})" : ""));

        if (firewall.Perfis.Count == 0)
            return Avaliacao.Pulada(MotivoDoPulo.NaoSuportado, "o Windows não informou os perfis do firewall");
        var ligados = firewall.Perfis.Where(p => p.Ligado).Select(p => Traduzir(p.Nome)).ToList();
        if (desligados.Count == 0)
            return new Avaliacao(Gravidade.Ok, $"ligado em: {string.Join(", ", ligados)}");

        var gravidade = firewall.Perfis.Any(p => !p.Ligado && p.Nome.Equals(Publico, StringComparison.OrdinalIgnoreCase))
            ? Gravidade.Critico
            : Gravidade.Atencao;
        return new Avaliacao(gravidade, $"desligado em: {string.Join(", ", desligados)}"
            + (ligados.Count > 0 ? $" (ligado em: {string.Join(", ", ligados)})" : ""));
    }

    private static string Traduzir(string perfil) => perfil.ToLowerInvariant() switch
    {
        "domain" => "Domínio",
        "private" => "Particular",
        "public" => "Público",
        _ => perfil,
    };
}
