using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Win32;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// O grupo "rede" da fase 3: Windows Update e winget. A busca online e o winget precisam de internet; o histórico
/// e o reinício pendente funcionam sem rede.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class LeitorDoWindows
{
    // Categorias do Windows Update (iguais em todo Windows). As definições do antivírus saem várias vezes por dia
    // e esconderiam um Windows parado há meses
    private const string CategoriaDefinicoes = "e0789628-ce08-4437-be74-2495b842f43b";
    private const string CategoriaSeguranca = "0fa1201d-4330-4fa8-8ae9-b877473b6441";
    // Mesmo pacote no histórico quando a entrada vem sem categoria
    private const string KbDasDefinicoes = "KB2267602";
    // Ferramenta de remoção de malware e plataforma do Defender: instalam todo mês mesmo com a atualização cumulativa
    // falhando, e esconderiam um Windows parado há meses
    private static readonly string[] KbsQueNaoContam = [KbDasDefinicoes, "KB890830", "KB4052623"];

    private static readonly TimeSpan PrazoDaBusca = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PrazoDoHistorico = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PrazoDoWinget = TimeSpan.FromSeconds(120);

    private static Lido<AtualizacoesPendentes> LerAtualizacoesPendentes(bool admin) =>
        // Prazo estourado na busca online = rede lenta ou bloqueada
        NoPrazo<AtualizacoesPendentes>(admin, PrazoDaBusca, MotivoDoPulo.SemConexao, () =>
        {
            var buscador = NovaSessao().CreateUpdateSearcher();
            var resultado = buscador.Search("IsInstalled=0 and IsHidden=0");
            var atualizacoes = resultado.Updates;
            int pendentes = 0, deSeguranca = 0;
            for (var i = 0; i < (int)atualizacoes.Count; i++)
            {
                HashSet<string> categorias = Categorias(atualizacoes.Item(i).Categories);
                if (categorias.Contains(CategoriaDefinicoes))
                    continue;
                pendentes++;
                if (categorias.Contains(CategoriaSeguranca))
                    deSeguranca++;
            }
            return new AtualizacoesPendentes(pendentes, deSeguranca);
        });

    private static Lido<DateTimeOffset?> LerUltimaAtualizacaoInstalada(bool admin) =>
        // O histórico é local: prazo estourado aqui é o serviço travado, não a rede
        NoPrazo<DateTimeOffset?>(admin, PrazoDoHistorico, MotivoDoPulo.Erro, () =>
        {
            var buscador = NovaSessao().CreateUpdateSearcher();
            int total = buscador.GetTotalHistoryCount();
            if (total == 0)
                return null;
            var historico = buscador.QueryHistory(0, total);
            DateTimeOffset? ultima = null;
            for (var i = 0; i < (int)historico.Count; i++)
            {
                var entrada = historico.Item(i);
                // Operation 1 = instalação (2 = remoção); ResultCode 2 = sucesso, 3 = sucesso com erros
                if ((int)entrada.Operation != 1 || (int)entrada.ResultCode is not (2 or 3))
                    continue;
                HashSet<string> categorias = Categorias(entrada.Categories);
                string titulo = Convert.ToString((object)entrada.Title) ?? "";
                if (categorias.Contains(CategoriaDefinicoes) || KbsQueNaoContam.Any(kb => titulo.Contains(kb, StringComparison.OrdinalIgnoreCase)))
                    continue;
                // A data vem em UTC, sem dizer
                var data = new DateTimeOffset(DateTime.SpecifyKind((DateTime)entrada.Date, DateTimeKind.Utc));
                if (ultima is null || data > ultima)
                    ultima = data;
            }
            return ultima;
        });

    private static Lido<bool> LerReinicioPendente(bool admin) => Tentar(admin, () =>
    {
        string[] chaves =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        ];
        try
        {
            return chaves.Any(caminho =>
            {
                using var chave = Registry.LocalMachine.OpenSubKey(caminho);
                return chave is not null;
            });
        }
        // O registro avisa falta de permissão com SecurityException; o Tentar entende UnauthorizedAccessException
        catch (SecurityException erro)
        {
            throw new UnauthorizedAccessException(erro.Message, erro);
        }
    });

    private static Lido<IReadOnlyList<ProgramaDesatualizado>> LerProgramasDesatualizados(bool admin)
    {
        // Caminho completo, como o powercfg: só "winget" deixaria um winget.exe qualquer na pasta atual rodar no lugar
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(winget))
            return Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(MotivoDoPulo.NaoSuportado);
        try
        {
            using var processo = Process.Start(new ProcessStartInfo(winget, ["upgrade", "--accept-source-agreements", "--disable-interactivity"])
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }) ?? throw new InvalidOperationException("winget não abriu");
            // Lidas em paralelo: esperando a saída antes do prazo, um winget travado travaria a revisão junto
            var saida = processo.StandardOutput.ReadToEndAsync();
            _ = processo.StandardError.ReadToEndAsync();
            if (!processo.WaitForExit(PrazoDoWinget))
            {
                try
                {
                    processo.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Terminou entre o prazo e o Kill
                }
                return Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(MotivoDoPulo.SemConexao);
            }
            if (!saida.Wait(TimeSpan.FromSeconds(10)))
                return Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(MotivoDoPulo.Erro);
            return SaidaDoWinget.Interpretar(saida.Result, processo.ExitCode);
        }
        catch (Exception erro) when (SemPermissao(erro))
        {
            return Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(admin ? MotivoDoPulo.SemPermissao : MotivoDoPulo.SemAdmin);
        }
        catch (Exception)
        {
            return Lido.Pulado<IReadOnlyList<ProgramaDesatualizado>>(MotivoDoPulo.Erro);
        }
    }

    private static dynamic NovaSessao() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!)!;

    private static HashSet<string> Categorias(dynamic categorias)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < (int)categorias.Count; i++)
            ids.Add(Convert.ToString((object)categorias.Item(i).CategoryID) ?? "");
        return ids;
    }

    // O Windows Update não tem prazo próprio (uma busca pode levar minutos): roda à parte e é abandonada no prazo
    private static Lido<T> NoPrazo<T>(bool admin, TimeSpan prazo, MotivoDoPulo noPrazo, Func<T> ler)
    {
        var leitura = Task.Run(ler);
        try
        {
            return leitura.Wait(prazo) ? Lido.Ok(leitura.Result) : Lido.Pulado<T>(noPrazo);
        }
        catch (AggregateException erro)
        {
            return Lido.Pulado<T>(ErroDoWindowsUpdate.Motivo(erro, admin));
        }
    }
}
