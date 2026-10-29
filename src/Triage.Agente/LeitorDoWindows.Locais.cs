using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// Fase 3, o que se lê sem internet: inicialização, drivers, log de eventos, antivírus e firewall. Nada disso
/// precisa de admin (conferido no notebook dele em 29/10).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class LeitorDoWindows
{
    private const string CentralDeSeguranca = @"root\SecurityCenter2";
    private const string Defender = @"root\Microsoft\Windows\Defender";
    private const string FirewallDoWindows = @"root\StandardCimv2";
    private const string Run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Aprovacoes = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const int DiasDeEventos = 30;

    // O mesmo que a aba "Aplicativos de inicialização" do Gerenciador de Tarefas: registro e pastas Inicializar.
    // Só o nome entra na foto, nunca o comando (que tem o caminho do programa)
    private static Lido<IReadOnlyList<ProgramaNaInicializacao>> LerInicializacao(bool admin) =>
        Tentar<IReadOnlyList<ProgramaNaInicializacao>>(admin, () =>
        [
            .. DoRegistro(Registry.CurrentUser, Run, "Run", OrigemDaInicializacao.RegistroDoUsuario),
            .. DoRegistro(Registry.LocalMachine, Run, "Run", OrigemDaInicializacao.RegistroDoSistema),
            // Programas de 32 bits; o Gerenciador de Tarefas guarda o "desligado" deles em Run32
            .. DoRegistro(Registry.LocalMachine, Run32, "Run32", OrigemDaInicializacao.RegistroDoSistema),
            .. DaPasta(Environment.SpecialFolder.Startup, Registry.CurrentUser, OrigemDaInicializacao.PastaDoUsuario),
            .. DaPasta(Environment.SpecialFolder.CommonStartup, Registry.LocalMachine, OrigemDaInicializacao.PastaDoSistema),
        ]);

    private static List<ProgramaNaInicializacao> DoRegistro(RegistryKey raiz, string caminho, string aprovacao,
        OrigemDaInicializacao origem)
    {
        using var chave = raiz.OpenSubKey(caminho);
        if (chave is null)
            return [];
        using var aprovacoes = raiz.OpenSubKey($@"{Aprovacoes}\{aprovacao}");
        return chave.GetValueNames()
            // O valor sem nome ("(Padrão)") não é programa
            .Where(nome => nome.Length > 0 && CodigosDoWindows.InicializacaoLigada(aprovacoes?.GetValue(nome) as byte[]))
            .Select(nome => new ProgramaNaInicializacao(CodigosDoWindows.SemCaminho(nome), origem))
            .ToList();
    }

    private const string NomeDeAtalho = "atalho na pasta Inicializar";

    private static List<ProgramaNaInicializacao> DaPasta(Environment.SpecialFolder especial, RegistryKey raiz,
        OrigemDaInicializacao origem)
    {
        var pasta = Environment.GetFolderPath(especial);
        if (pasta.Length == 0 || !Directory.Exists(pasta))
            return [];
        using var aprovacoes = raiz.OpenSubKey($@"{Aprovacoes}\StartupFolder");
        // Na pasta, o "desligado" é gravado com o nome do arquivo inteiro ("Programa.lnk")
        return Directory.GetFiles(pasta)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(arquivo => !arquivo.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                && CodigosDoWindows.InicializacaoLigada(aprovacoes?.GetValue(arquivo) as byte[]))
            // O nome do atalho é nome de arquivo (pode ser "Planilha do André"): fica só a contagem (P15)
            .Select(_ => new ProgramaNaInicializacao(NomeDeAtalho, origem))
            .ToList();
    }

    private static Lido<IReadOnlyList<DispositivoComErro>> LerDriversComErro(bool admin) =>
        Tentar<IReadOnlyList<DispositivoComErro>>(admin, () =>
        {
            using var busca = new ManagementObjectSearcher(
                "SELECT PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            using var dispositivos = busca.Get();
            var comErro = new List<DispositivoComErro>();
            foreach (var d in dispositivos.Cast<ManagementObject>())
            {
                // Convert.ToInt32(null) daria 0: o nulo é tratado antes de converter
                if (d["ConfigManagerErrorCode"] is { } bruto && Convert.ToInt32(bruto) is var codigo
                    && CodigosDoWindows.DriverComErro(codigo))
                    // Sem o Name: aparelho pareado tem o nome que o dono deu (P15)
                    comErro.Add(new DispositivoComErro(Texto(d["PNPClass"], "sem classe"), codigo));
            }
            return comErro;
        });

    private static string Texto(object? valor, string padrao) =>
        Convert.ToString(valor, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } texto ? texto : padrao;

    // "Nenhum evento" é contagem 0, não erro; o log do Sistema abre para qualquer usuário
    private static Lido<EventosDoSistema> LerEventos(bool admin, TimeProvider relogio) => Tentar(admin, () =>
    {
        var desde = relogio.GetUtcNow().AddDays(-DiasDeEventos);
        return new EventosDoSistema(DiasDeEventos,
            // Kernel-Power 41: o Windows voltou sem ter desligado direito (energia, botão segurado, travamento). Depois de
            // uma tela azul ele também grava o 41, com o BugcheckCode: esses ficam só nas telas azuis
            ContarEventos("Microsoft-Windows-Kernel-Power", 41, desde, "EventData[Data[@Name='BugcheckCode']='0']"),
            // BugCheck 1001: o Windows reiniciou depois de uma tela azul
            ContarEventos("Microsoft-Windows-WER-SystemErrorReporting", 1001, desde));
    });

    private static int ContarEventos(string provedor, int id, DateTimeOffset desde, string? alemDisso = null)
    {
        // Data fixa pelo relógio (não "últimos N ms" do próprio Windows), para a janela bater com a hora da foto
        var inicio = desde.UtcDateTime.ToString("yyyy-MM-dd'T'HH':'mm':'ss'.'fff'Z'", CultureInfo.InvariantCulture);
        var filtro = $"*[System[Provider[@Name='{provedor}'] and (EventID={id}) and TimeCreated[@SystemTime>='{inicio}']]]"
            + (alemDisso is null ? "" : $" and *[{alemDisso}]");
        using var leitor = new EventLogReader(new EventLogQuery("System", PathType.LogName, filtro));
        var total = 0;
        for (var evento = leitor.ReadEvent(); evento is not null; evento = leitor.ReadEvent())
        {
            evento.Dispose();
            total++;
        }
        return total;
    }

    private sealed record EstadoDoDefender(bool? TempoReal, int? IdadeDasDefinicoes);

    private static Lido<Antivirus> LerAntivirus(bool admin)
    {
        var central = Tentar(admin, () => LerProdutosDaCentral("AntiVirusProduct"));
        var defender = Tentar(admin, LerDefender);
        // Sem nenhuma das duas fontes não sobra nada para avaliar
        if (central.Valor is null && !defender.FoiLido)
            return Lido.Pulado<Antivirus>(central.Pulo ?? defender.Pulo!.Value);
        // A Central falhou (não é que não exista) e o Defender está desligado: pode haver outro antivírus ativo que só
        // ela mostraria, então "nenhum antivírus" seria chute
        if (!central.FoiLido && defender.Valor?.TempoReal != true)
            return Lido.Pulado<Antivirus>(central.Pulo!.Value);
        return Lido.Ok(new Antivirus(central.Valor, defender.Valor?.TempoReal, defender.Valor?.IdadeDasDefinicoes));
    }

    private static EstadoDoDefender LerDefender()
    {
        using var busca = new ManagementObjectSearcher(Defender,
            "SELECT RealTimeProtectionEnabled, AntivirusSignatureAge FROM MSFT_MpComputerStatus");
        using var estados = busca.Get();
        var estado = estados.Cast<ManagementObject>().First();
        return new EstadoDoDefender(
            estado["RealTimeProtectionEnabled"] as bool?,
            estado["AntivirusSignatureAge"] is { } idade ? Convert.ToInt32(idade) : null);
    }

    // Antivírus e firewalls registrados na Central de Segurança. Nulo = ela não existe (Windows Server)
    private static IReadOnlyList<ProdutoDeSeguranca>? LerProdutosDaCentral(string classe)
    {
        try
        {
            using var busca = new ManagementObjectSearcher(CentralDeSeguranca, $"SELECT displayName, productState FROM {classe}");
            using var produtos = busca.Get();
            var lista = new List<ProdutoDeSeguranca>();
            foreach (var p in produtos.Cast<ManagementObject>())
                // Sem estado não dá para dizer se está ligado: fica de fora em vez de virar "desligado" (0)
                if (p["productState"] is { } estado)
                    lista.Add(CodigosDoWindows.ProdutoDaCentral(Convert.ToString(p["displayName"]), Convert.ToUInt32(estado)));
            return lista;
        }
        catch (ManagementException erro) when (erro.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass)
        {
            return null;
        }
    }

    private static Lido<Firewall> LerFirewall(bool admin)
    {
        var perfis = Tentar(admin, LerPerfisDoFirewall);
        if (!perfis.FoiLido)
            return Lido.Pulado<Firewall>(perfis.Pulo!.Value);
        // Falhar aqui só tira os firewalls de terceiros: o do Windows já foi lido
        var outros = Tentar(admin, () => LerProdutosDaCentral("FirewallProduct"));
        return Lido.Ok(new Firewall(perfis.Valor!, outros.Valor));
    }

    // Name vem em inglês, igual em qualquer idioma do Windows (Domain, Private, Public): a verificação traduz.
    // Enabled é UInt16: 1 = ligado
    private static IReadOnlyList<PerfilDoFirewall> LerPerfisDoFirewall()
    {
        using var busca = new ManagementObjectSearcher(FirewallDoWindows, "SELECT Name, Enabled FROM MSFT_NetFirewallProfile");
        using var perfis = busca.Get();
        return perfis.Cast<ManagementObject>()
            // Perfil sem o campo fica de fora em vez de virar "desligado" (0)
            .Where(p => p["Enabled"] is not null)
            .Select(p => new PerfilDoFirewall(Texto(p["Name"], "sem nome"), Convert.ToInt32(p["Enabled"]) == 1))
            .ToList();
    }
}
