namespace Triage.Verificacoes.Itens;

public sealed class ProgramasDesatualizados : IVerificacao
{
    // Mais que isso no resumo vira "e mais N" (a lista inteira sai no `winget upgrade`)
    private const int NomesNoResumo = 5;

    public string Id => "programas-desatualizados";
    public string Titulo => "Programas desatualizados";
    public string OQueE => "Programas instalados que já têm versão nova, segundo o winget (o instalador de programas do Windows).";
    public string PorQueImporta => "Versões novas corrigem falhas de segurança, principalmente em navegadores e programas que abrem arquivos da internet.";
    public string ComoResolver => "No Terminal, \"winget upgrade --all\" atualiza tudo de uma vez; \"winget upgrade --id Git.Git\" atualiza um só (o id aparece na lista do \"winget upgrade\"). Feche o programa antes de atualizar.";

    public Avaliacao Avaliar(FotoDoSistema foto, Limites limites)
    {
        if (!foto.ProgramasDesatualizados.FoiLido)
            return Avaliacao.Pulada(foto.ProgramasDesatualizados.Pulo!.Value);
        var programas = foto.ProgramasDesatualizados.Valor!;
        if (programas.Count == 0)
            return new Avaliacao(Gravidade.Ok, "todos os programas do winget estão na versão mais nova");

        var gravidade = programas.Count >= limites.ProgramasDesatualizadosAtencao ? Gravidade.Atencao : Gravidade.Ok;
        var nomes = programas.Take(NomesNoResumo).Select(p => $"{p.Nome} ({p.Versao} → {p.Disponivel})");
        var resto = programas.Count > NomesNoResumo ? $" e mais {programas.Count - NomesNoResumo}" : "";
        var quantos = programas.Count == 1 ? "1 programa com versão nova" : $"{programas.Count} programas com versão nova";
        return new Avaliacao(gravidade, $"{quantos}: {string.Join(", ", nomes)}{resto}");
    }
}
