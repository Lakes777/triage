using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Triage.Servidor.Banco;
using Triage.Verificacoes;

namespace Triage.Servidor.Demo;

/// <summary>
/// A frota inventada da demonstração (P13): 6 PCs, 8 semanas de revisões, cada um com um problema. As gravidades
/// saem do Motor de verdade (as mesmas regras do agente), não de números escritos à mão. Só toca em máquinas com
/// demo = true: as reais, no mesmo banco (P31), nunca são apagadas nem mudadas.
/// </summary>
public static class FrotaDeDemonstracao
{
    public const int Semanas = 8;
    private const long Gb = 1L << 30;
    private const long Mb = 1L << 20;

    /// <summary>Ids fixos: o link de uma máquina da demo continua o mesmo depois de semear de novo (cron diário).</summary>
    private static readonly (Guid Id, string Apelido, Func<int, FotoDoSistema, FotoDoSistema> Semana)[] Frota =
    [
        (Guid.Parse("0193f000-0000-7000-8000-000000000001"), "Notebook da recepção", SsdGastando),
        (Guid.Parse("0193f000-0000-7000-8000-000000000002"), "PC do caixa", DiscoEnchendo),
        (Guid.Parse("0193f000-0000-7000-8000-000000000003"), "Notebook do escritório", BateriaCaindo),
        (Guid.Parse("0193f000-0000-7000-8000-000000000004"), "PC do estoque", TelasAzuis),
        (Guid.Parse("0193f000-0000-7000-8000-000000000005"), "Notebook do vendedor", DefenderDesligado),
        (Guid.Parse("0193f000-0000-7000-8000-000000000006"), "PC da gerência", (_, f) => f),
    ];

    public static IReadOnlyList<Guid> Ids => Frota.Select(f => f.Id).ToList();

    /// <summary>Apaga a frota antiga e põe a nova, com as datas contadas a partir de agora. Devolve quantas revisões.</summary>
    public static async Task<int> Semear(TriageContexto banco, DateTimeOffset agora, CancellationToken cancelar = default)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(cancelar);
        // O cron e um semear-demo manual ao mesmo tempo: o segundo espera o primeiro (os ids são fixos e colidiriam)
        await banco.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7770008)", cancelar);
        var demo = banco.Maquinas.Where(m => m.Demo).Select(m => m.Id);
        await banco.Avisos.Where(a => demo.Contains(a.MaquinaId)).ExecuteDeleteAsync(cancelar);
        await banco.Resultados.Where(r => demo.Contains(r.MaquinaId)).ExecuteDeleteAsync(cancelar);
        await banco.Revisoes.Where(r => demo.Contains(r.MaquinaId)).ExecuteDeleteAsync(cancelar);
        await banco.Codigos.Where(c => c.MaquinaId != null && demo.Contains(c.MaquinaId.Value)).ExecuteDeleteAsync(cancelar);
        await banco.Maquinas.Where(m => m.Demo).ExecuteDeleteAsync(cancelar);

        // A revisão mais recente é a última vez que deu 15h UTC (meio-dia em Brasília): nunca no futuro
        var ultima = new DateTimeOffset(agora.UtcDateTime.Date.AddHours(15), TimeSpan.Zero);
        if (ultima > agora)
            ultima = ultima.AddDays(-1);
        var revisoes = 0;
        foreach (var (id, apelido, semana) in Frota)
        {
            banco.Maquinas.Add(new Maquina
            {
                Id = id,
                Apelido = apelido,
                // Uma chave que ninguém tem: a demo nunca recebe envio de verdade
                ChaveHash = RandomNumberGenerator.GetBytes(32),
                InscritaEm = agora.AddDays(-7 * Semanas - 3),
                Demo = true,
            });
            string? fotoAnterior = null;
            var anterior = new Dictionary<string, string>();
            for (var s = 0; s < Semanas; s++)
            {
                // Uma por semana, da mais antiga até a mais recente
                var feitaEm = ultima.AddDays(-7 * (Semanas - 1 - s));
                var foto = semana(s, Saudavel(feitaEm));
                var revisao = Motor.Padrao().Revisar(foto, Limites.Padrao);
                var json = Gravacao.ParaJson(foto);
                var revisaoId = Guid.CreateVersion7(feitaEm);
                banco.Revisoes.Add(new RevisaoGravada
                {
                    MaquinaId = id,
                    Id = revisaoId,
                    FeitaEm = feitaEm,
                    RecebidaEm = feitaEm.AddSeconds(30),
                    VersaoDoAgente = "0.1.0+demo",
                    Administrador = true,
                    ConteudoHash = SHA256.HashData(Encoding.UTF8.GetBytes(json)),
                    Foto = json,
                });
                var atual = revisao.Resultados.ToDictionary(r => r.Item, r => Nome(r.Avaliacao.Gravidade));
                banco.Resultados.AddRange(revisao.Resultados.Select(r => new ResultadoGravado
                {
                    MaquinaId = id,
                    RevisaoId = revisaoId,
                    Item = r.Item,
                    Gravidade = atual[r.Item],
                    Motivo = r.Avaliacao.Motivo is { } m ? Nome(m) : null,
                }));
                // As mesmas regras dos avisos de verdade, para o painel mostrar o histórico de pioras; o Telegram
                // nunca manda aviso de máquina da demo
                foreach (var p in Historico.Pioras(anterior, atual).Concat(Historico.DesgasteSubindo(fotoAnterior, json))
                             .DistinctBy(p => p.Item))
                {
                    banco.Avisos.Add(new Aviso
                    {
                        Id = Guid.CreateVersion7(feitaEm),
                        MaquinaId = id,
                        RevisaoId = revisaoId,
                        Item = p.Item,
                        De = p.De,
                        Para = p.Para,
                        Detalhe = p.Detalhe,
                        CriadoEm = feitaEm,
                        EnviadoEm = feitaEm,
                    });
                }
                foreach (var (item, gravidade) in atual.Where(a => Historico.Comparavel(a.Value)))
                    anterior[item] = gravidade;
                fotoAnterior = json;
                revisoes++;
            }
        }
        await banco.SaveChangesAsync(cancelar);
        await transacao.CommitAsync(cancelar);
        return revisoes;
    }

    private static string Nome(Gravidade g) => g switch
    {
        Gravidade.Ok => "ok",
        Gravidade.Atencao => "atencao",
        Gravidade.Critico => "critico",
        Gravidade.Pulado => "pulado",
        _ => "nao_se_aplica",
    };

    // Os mesmos nomes que o Envios grava
    private static string Nome(MotivoDoPulo m) => m switch
    {
        MotivoDoPulo.SemAdmin => "sem_admin",
        MotivoDoPulo.SemConexao => "sem_conexao",
        MotivoDoPulo.SemPermissao => "sem_permissao",
        MotivoDoPulo.NaoSuportado => "nao_suportado",
        _ => "erro",
    };

    /// <summary>Um PC sem nada de errado; cada cenário muda uma coisa por semana.</summary>
    private static FotoDoSistema Saudavel(DateTimeOffset quando) => new(
        quando,
        Administrador: true,
        Lido.Ok<IReadOnlyList<Volume>>([new Volume("C:", 476 * Gb, 210 * Gb, DoSistema: true)]),
        Lido.Ok<IReadOnlyList<Disco>>([new Disco("NVMe SSD 512GB", TipoDeDisco.Nvme, 512 * Gb, SaudeDoDisco.Saudavel, Desgaste: 12)]),
        Lido.Ok(new Pasta(380 * Mb, 412)),
        Lido.Ok(new Pasta(120 * Mb, 96)),
        Lido.Ok(new Pasta(40 * Mb, 6)),
        Lido.Ok(new ConteudoDaLixeira(150 * Mb, 23)),
        Lido.Ok(quando.AddDays(-2)),
        Lido.Ok<Bateria?>(null))
    {
        Atualizacoes = Lido.Ok(new AtualizacoesPendentes(0, 0)),
        ReinicioPendente = Lido.Ok(false),
        UltimaAtualizacaoInstalada = Lido.Ok<DateTimeOffset?>(quando.AddDays(-6)),
        ProgramasDesatualizados = Lido.Ok<IReadOnlyList<ProgramaDesatualizado>>(
            [new ProgramaDesatualizado("7-Zip", "7zip.7zip", "24.08", "24.09")]),
        Inicializacao = Lido.Ok<IReadOnlyList<ProgramaNaInicializacao>>(
        [
            new ProgramaNaInicializacao("OneDrive", OrigemDaInicializacao.RegistroDoUsuario),
            new ProgramaNaInicializacao("SecurityHealth", OrigemDaInicializacao.RegistroDoSistema),
        ]),
        DriversComErro = Lido.Ok<IReadOnlyList<DispositivoComErro>>([]),
        Eventos = Lido.Ok(new EventosDoSistema(30, 0, 0)),
        Antivirus = Lido.Ok(new Antivirus([new ProdutoDeSeguranca("Microsoft Defender Antivirus", true, true)], true, 1)),
        Firewall = Lido.Ok(new Firewall(
            [new PerfilDoFirewall("Domínio", true), new PerfilDoFirewall("Particular", true), new PerfilDoFirewall("Público", true)],
            null)),
    };

    private static Lido<Bateria?> BateriaCom(int porcento) =>
        Lido.Ok<Bateria?>(new Bateria(45_000, 45_000 * porcento / 100, 300 + porcento));

    // Desgaste quieto por semanas e depois disparando (avisos de "subindo rápido" e de atenção)
    private static FotoDoSistema SsdGastando(int s, FotoDoSistema f) => f with
    {
        Discos = Lido.Ok<IReadOnlyList<Disco>>([new Disco("SSD SATA 240GB", TipoDeDisco.Ssd, 240 * Gb,
            SaudeDoDisco.Saudavel, Desgaste: new[] { 61, 61, 62, 62, 66, 70, 75, 81 }[s])]),
        Bateria = BateriaCom(91),
    };

    private static FotoDoSistema DiscoEnchendo(int s, FotoDoSistema f) => f with
    {
        Volumes = Lido.Ok<IReadOnlyList<Volume>>([new Volume("C:", 238 * Gb,
            (long)(238 * Gb * new[] { 0.31, 0.26, 0.21, 0.17, 0.13, 0.09, 0.06, 0.04 }[s]), DoSistema: true)]),
        TempDoUsuario = Lido.Ok(new Pasta((long)(s * 0.6 * Gb), 3000 + s * 900)),
    };

    private static FotoDoSistema BateriaCaindo(int s, FotoDoSistema f) => f with
    {
        Bateria = BateriaCom(new[] { 86, 84, 81, 78, 74, 69, 63, 58 }[s]),
    };

    private static FotoDoSistema TelasAzuis(int s, FotoDoSistema f) => f with
    {
        Eventos = Lido.Ok(new EventosDoSistema(30, s >= 5 ? 1 : 0, new[] { 0, 0, 0, 0, 1, 1, 2, 3 }[s])),
        DriversComErro = Lido.Ok<IReadOnlyList<DispositivoComErro>>(s >= 4 ? [new DispositivoComErro("Display", 43)] : []),
    };

    private static FotoDoSistema DefenderDesligado(int s, FotoDoSistema f) => f with
    {
        Antivirus = Lido.Ok(s < 6
            ? new Antivirus([new ProdutoDeSeguranca("Microsoft Defender Antivirus", true, true)], true, 1)
            : new Antivirus([new ProdutoDeSeguranca("Microsoft Defender Antivirus", false, false)], false, 9 + s)),
        Bateria = BateriaCom(88),
    };
}
