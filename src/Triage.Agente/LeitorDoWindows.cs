using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Xml.Linq;
using Triage.Verificacoes;

namespace Triage.Agente;

/// <summary>
/// Lê o Windows de verdade. Cada parte falha sozinha: o que não der para ler sai pulado com o motivo, e o resto
/// segue. Nada de caminho, nome de usuário ou número de série entra na foto (P15).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LeitorDoWindows(TimeProvider relogio) : ILeitorDoSistema
{
    private const string Armazenamento = @"root\Microsoft\Windows\Storage";

    public FotoDoSistema Ler()
    {
        var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new FotoDoSistema(
            relogio.GetLocalNow(),
            admin,
            Tentar(admin, LerVolumes),
            Tentar(admin, () => LerDiscos(admin)),
            LerPasta(Path.GetTempPath(), admin),
            LerPasta(Path.Combine(windows, "Temp"), admin),
            LerPasta(Path.Combine(windows, "SoftwareDistribution", "Download"), admin),
            Tentar(admin, LerLixeira),
            Tentar(admin, LerUltimaInicializacao),
            Tentar(admin, LerBateria));
    }

    private static Lido<T> Tentar<T>(bool admin, Func<T> ler)
    {
        try
        {
            return Lido.Ok(ler());
        }
        catch (Exception erro) when (SemPermissao(erro))
        {
            return Lido.Pulado<T>(admin ? MotivoDoPulo.SemPermissao : MotivoDoPulo.SemAdmin);
        }
        catch (Exception)
        {
            return Lido.Pulado<T>(MotivoDoPulo.Erro);
        }
    }

    private static bool SemPermissao(Exception erro) => erro is UnauthorizedAccessException
        || erro is ManagementException { ErrorCode: ManagementStatus.AccessDenied };

    private static IReadOnlyList<Volume> LerVolumes()
    {
        var doSistema = Path.GetPathRoot(Environment.SystemDirectory);
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new Volume(d.Name.TrimEnd('\\'), d.TotalSize, d.TotalFreeSpace,
                string.Equals(d.Name, doSistema, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static IReadOnlyList<Disco> LerDiscos(bool admin)
    {
        var (desgastes, semDesgaste) = LerDesgastes(admin);
        using var busca = new ManagementObjectSearcher(Armazenamento,
            "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk");
        using var discos = busca.Get();
        return discos.Cast<ManagementObject>().Select(d =>
        {
            var id = Convert.ToString(d["DeviceId"]) ?? "";
            int? desgaste = desgastes.TryGetValue(id, out var valor) ? valor : null;
            return new Disco(
                (Convert.ToString(d["FriendlyName"]) ?? "disco").Trim(),
                // MediaType 3 = HD, 4 = SSD; BusType 17 = NVMe
                Convert.ToInt32(d["MediaType"]) switch
                {
                    3 => TipoDeDisco.Hd,
                    4 when Convert.ToInt32(d["BusType"]) == 17 => TipoDeDisco.Nvme,
                    4 => TipoDeDisco.Ssd,
                    _ => TipoDeDisco.Desconhecido,
                },
                Convert.ToInt64(d["Size"]),
                // HealthStatus 0 = saudável, 1 = alerta, 2 = ruim, 5 = desconhecida
                d["HealthStatus"] is null ? SaudeDoDisco.Desconhecida : Convert.ToInt32(d["HealthStatus"]) switch
                {
                    0 => SaudeDoDisco.Saudavel,
                    1 => SaudeDoDisco.Alerta,
                    2 => SaudeDoDisco.Ruim,
                    _ => SaudeDoDisco.Desconhecida,
                },
                desgaste,
                desgaste is null ? semDesgaste ?? MotivoDoPulo.NaoSuportado : null);
        }).ToList();
    }

    // O contador de confiabilidade (desgaste) só abre para administrador
    private static (Dictionary<string, int> Desgastes, MotivoDoPulo? Motivo) LerDesgastes(bool admin)
    {
        try
        {
            using var busca = new ManagementObjectSearcher(Armazenamento,
                "SELECT DeviceId, Wear FROM MSFT_StorageReliabilityCounter");
            using var contadores = busca.Get();
            var desgastes = new Dictionary<string, int>();
            foreach (var c in contadores.Cast<ManagementObject>())
                if (c["Wear"] is not null && Convert.ToString(c["DeviceId"]) is string id)
                    desgastes[id] = Convert.ToInt32(c["Wear"]);
            return (desgastes, null);
        }
        catch (Exception erro) when (erro is ManagementException or UnauthorizedAccessException)
        {
            return ([], admin ? MotivoDoPulo.NaoSuportado : MotivoDoPulo.SemAdmin);
        }
    }

    private static Lido<Pasta> LerPasta(string caminho, bool admin)
    {
        if (!Directory.Exists(caminho))
            return Lido.Ok(new Pasta(0, 0));
        try
        {
            // Sem permissão na raiz, a pasta é "não lida", não "vazia" (o EnumerateFiles abaixo engoliria o erro)
            using var entradas = Directory.EnumerateFileSystemEntries(caminho).GetEnumerator();
            entradas.MoveNext();
        }
        catch (UnauthorizedAccessException)
        {
            return Lido.Pulado<Pasta>(admin ? MotivoDoPulo.SemPermissao : MotivoDoPulo.SemAdmin);
        }
        catch (IOException)
        {
            return Lido.Pulado<Pasta>(MotivoDoPulo.Erro);
        }

        var opcoes = new System.IO.EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // O padrão pula ocultos e de sistema, que também ocupam espaço; atalhos de pasta não são seguidos
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        long bytes = 0;
        var arquivos = 0;
        try
        {
            foreach (var arquivo in new DirectoryInfo(caminho).EnumerateFiles("*", opcoes))
            {
                bytes += arquivo.Length;
                arquivos++;
            }
        }
        catch (IOException)
        {
            return Lido.Pulado<Pasta>(MotivoDoPulo.Erro);
        }
        return Lido.Ok(new Pasta(bytes, arquivos));
    }

    private static ConteudoDaLixeira LerLixeira()
    {
        var info = new InfoDaLixeira { Tamanho = Marshal.SizeOf<InfoDaLixeira>() };
        Marshal.ThrowExceptionForHR(SHQueryRecycleBinW(null, ref info));
        return new ConteudoDaLixeira(info.Bytes, info.Itens);
    }

    // SHQUERYRBINFO; no x64 o alinhamento natural bate (o shellapi.h só empacota em 1 byte no 32 bits)
    [StructLayout(LayoutKind.Sequential)]
    private struct InfoDaLixeira
    {
        public int Tamanho;
        public long Bytes;
        public long Itens;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHQueryRecycleBinW(string? raiz, ref InfoDaLixeira info);

    private static DateTimeOffset LerUltimaInicializacao()
    {
        using var busca = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
        using var sistemas = busca.Get();
        var bruto = sistemas.Cast<ManagementObject>().Select(s => Convert.ToString(s["LastBootUpTime"])).First();
        return new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(bruto));
    }

    private static Bateria? LerBateria()
    {
        using (var busca = new ManagementObjectSearcher("SELECT Name FROM Win32_Battery"))
        using (var baterias = busca.Get())
            if (baterias.Count == 0)
                return null;

        // A capacidade original (BatteryStaticData) só sai por WMI para administrador; o relatório do powercfg
        // traz a mesma informação para qualquer usuário
        var arquivo = Path.Combine(Path.GetTempPath(), $"triage-bateria-{Guid.NewGuid():N}.xml");
        try
        {
            // Caminho completo: só o nome deixaria o Windows procurar primeiro na pasta atual, e um powercfg.exe
            // qualquer em Downloads rodaria com os direitos de quem chamou o Triage
            var programa = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
            using var powercfg = Process.Start(new ProcessStartInfo(programa, ["/batteryreport", "/xml", "/output", arquivo])
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("powercfg não abriu");
            // Lidas em paralelo: esperando a saída antes do prazo, um powercfg travado travaria a revisão junto
            _ = powercfg.StandardOutput.ReadToEndAsync();
            _ = powercfg.StandardError.ReadToEndAsync();
            if (!powercfg.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                powercfg.Kill();
                throw new TimeoutException("powercfg não terminou em 30 s");
            }
            if (powercfg.ExitCode != 0)
                throw new InvalidOperationException("powercfg falhou");

            var baterias = XDocument.Load(arquivo).Descendants().Where(e => e.Name.LocalName == "Battery").ToList();
            int Somar(string campo) => baterias.Sum(b => int.TryParse(Filho(b, campo), out var v) ? v : 0);
            int? ciclos = baterias.Select(b => int.TryParse(Filho(b, "CycleCount"), out var c) ? c : (int?)null).Max();
            return new Bateria(Somar("DesignCapacity"), Somar("FullChargeCapacity"), ciclos);
        }
        finally
        {
            File.Delete(arquivo);
        }
    }

    private static string? Filho(XElement elemento, string nome) =>
        elemento.Elements().FirstOrDefault(e => e.Name.LocalName == nome)?.Value;
}
