using System.Net;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Triage.Contratos.Grpc;
using Triage.Servidor.Avisos;
using Triage.Servidor.Banco;
using Triage.Verificacoes;
using Gravidade = Triage.Contratos.Grpc.Gravidade;
using MotivoDoPulo = Triage.Contratos.Grpc.MotivoDoPulo;

namespace Triage.Servidor.Tests;

public sealed class AvisoTests(ServidorDeTeste servidor)
{
    private static CancellationToken Cancelar => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Base = DateTimeOffset.UtcNow.AddDays(-20);

    private static string Foto(int? desgaste) => Gravacao.ParaJson(new FotoDoSistema(DateTimeOffset.UtcNow, true,
        Lido.Ok<IReadOnlyList<Volume>>([]),
        Lido.Ok<IReadOnlyList<Disco>>([new Disco("NVMe 512", TipoDeDisco.Nvme, 512, SaudeDoDisco.Saudavel, desgaste)]),
        Lido.Ok(new Pasta(0, 0)), Lido.Ok(new Pasta(0, 0)), Lido.Ok(new Pasta(0, 0)),
        Lido.Ok(new ConteudoDaLixeira(0, 0)), Lido.Ok(DateTimeOffset.UtcNow), Lido.Ok<Bateria?>(null)));

    private static EnviarRevisaoPedido Revisao(int dia, Gravidade espaco, Gravidade bateria = Gravidade.Ok, int? desgaste = 10,
        bool admin = true) => new()
        {
            RevisaoId = Guid.CreateVersion7(Base.AddDays(dia)).ToString(),
            FeitaEm = Timestamp.FromDateTimeOffset(Base.AddDays(dia)),
            VersaoDoAgente = "0.1.0+teste",
            Administrador = admin,
            FotoJson = Foto(desgaste),
            Resultados =
        {
            new ResultadoDoItem { Item = "espaco-livre", Gravidade = espaco,
                Motivo = espaco == Gravidade.Pulado ? MotivoDoPulo.MotivoSemPermissao : MotivoDoPulo.MotivoNenhum },
            new ResultadoDoItem { Item = "bateria", Gravidade = bateria },
        },
        };

    private async Task Enviar((string Maquina, string Chave) m, EnviarRevisaoPedido pedido) =>
        await servidor.Cliente().EnviarRevisaoAsync(pedido, ServidorDeTeste.Credenciais(m.Maquina, m.Chave),
            cancellationToken: Cancelar);

    private Task<List<Aviso>> Avisos(string maquina)
    {
        var id = Guid.Parse(maquina);
        return servidor.NoBanco((_, b) => b.Avisos.Where(a => a.MaquinaId == id).OrderBy(a => a.CriadoEm).ThenBy(a => a.Item).ToListAsync(Cancelar));
    }

    [Fact]
    public async Task Uma_piora_e_um_aviso_mesmo_repetindo_o_envio()
    {
        // Critério da fase 8 e P17 (5)
        var m = await servidor.NovaMaquina();
        await Enviar(m, Revisao(0, Gravidade.Ok));
        var piorou = Revisao(7, Gravidade.Atencao);

        await Enviar(m, piorou);
        await Enviar(m, piorou.Clone());
        await Enviar(m, Revisao(14, Gravidade.Atencao)); // continua em atenção: não é mudança

        var aviso = Assert.Single(await Avisos(m.Maquina));
        Assert.Equal(("espaco-livre", "ok", "atencao"), (aviso.Item, aviso.De, aviso.Para));
        Assert.Equal(Guid.Parse(piorou.RevisaoId), aviso.RevisaoId);
    }

    [Fact]
    public async Task Revisao_antiga_chegando_depois_nao_avisa()
    {
        // A fila offline entregou fora de ordem: a revisão do dia 3 chega depois da do dia 7
        var m = await servidor.NovaMaquina();
        await Enviar(m, Revisao(0, Gravidade.Ok));
        await Enviar(m, Revisao(7, Gravidade.Ok));

        await Enviar(m, Revisao(3, Gravidade.Critico));

        Assert.Empty(await Avisos(m.Maquina));
    }

    [Fact]
    public async Task Piora_depois_de_um_pulado_compara_com_o_ultimo_que_leu()
    {
        var m = await servidor.NovaMaquina();
        await Enviar(m, Revisao(0, Gravidade.Ok));
        await Enviar(m, Revisao(7, Gravidade.Pulado));

        await Enviar(m, Revisao(14, Gravidade.Critico));

        var aviso = Assert.Single(await Avisos(m.Maquina));
        Assert.Equal(("ok", "critico"), (aviso.De, aviso.Para));
    }

    [Fact]
    public async Task Primeira_revisao_e_melhoras_nao_avisam()
    {
        var m = await servidor.NovaMaquina();
        await Enviar(m, Revisao(0, Gravidade.Critico, Gravidade.Critico));

        await Enviar(m, Revisao(7, Gravidade.Ok, Gravidade.Atencao));

        Assert.Empty(await Avisos(m.Maquina));
    }

    [Fact]
    public async Task Desgaste_compara_com_a_ultima_revisao_de_admin()
    {
        // Achado do revisor: a semanal roda sem admin (sem desgaste); comparar com ela nunca avisaria
        var m = await servidor.NovaMaquina();
        await Enviar(m, Revisao(0, Gravidade.Ok, desgaste: 40));
        await Enviar(m, Revisao(7, Gravidade.Ok, desgaste: null, admin: false));

        await Enviar(m, Revisao(14, Gravidade.Ok, desgaste: 45));

        var aviso = Assert.Single(await Avisos(m.Maquina));
        Assert.Equal(("desgaste-ssd", "40%", "45%"), (aviso.Item, aviso.De, aviso.Para));
    }

    [Fact]
    public async Task Desgaste_de_mais_de_30_dias_atras_nao_e_rapido()
    {
        var m = await servidor.NovaMaquina();
        await Enviar(m, Revisao(-40, Gravidade.Ok, desgaste: 40));

        await Enviar(m, Revisao(0, Gravidade.Ok, desgaste: 45));

        Assert.Empty(await Avisos(m.Maquina));
    }

    [Fact]
    public async Task Duas_revisoes_com_o_mesmo_horario_avisam_uma_vez()
    {
        var m = await servidor.NovaMaquina();
        await Enviar(m, Revisao(0, Gravidade.Ok));
        var a = Revisao(7, Gravidade.Critico);
        var b = Revisao(7, Gravidade.Critico, Gravidade.Atencao); // outro id e outro conteúdo, mesmo feita_em

        await Enviar(m, a);
        await Enviar(m, b);

        Assert.Single(await Avisos(m.Maquina), x => x.Item == "espaco-livre");
    }

    [Fact]
    public async Task Duas_pioras_na_mesma_revisao_sao_dois_avisos()
    {
        var m = await servidor.NovaMaquina();
        await Enviar(m, Revisao(0, Gravidade.Ok, Gravidade.Ok, desgaste: 40));

        await Enviar(m, Revisao(7, Gravidade.Atencao, Gravidade.Critico, desgaste: 45));

        var avisos = await Avisos(m.Maquina);
        Assert.Equal(["bateria", "desgaste-ssd", "espaco-livre"], avisos.Select(a => a.Item).Order(StringComparer.Ordinal));
        var ssd = avisos.Single(a => a.Item == "desgaste-ssd");
        Assert.Equal(("40%", "45%", "NVMe 512"), (ssd.De, ssd.Para, ssd.Detalhe));
    }
}

/// <summary>O "Telegram" dos testes: guarda as mensagens, ou falha quando mandado.</summary>
public sealed class MensageiroFalso : IMensageiro
{
    private readonly List<string> mensagens = [];
    private int tentativas;
    public bool Falhar { get; set; }
    public int Tentativas => tentativas;

    public IReadOnlyList<string> Mensagens
    {
        get
        {
            lock (mensagens)
                return mensagens.ToList();
        }
    }

    public async Task Enviar(string texto, CancellationToken cancelar)
    {
        Interlocked.Increment(ref tentativas);
        await Task.Delay(20, cancelar);
        if (Falhar)
            throw new HttpRequestException("Telegram fora do ar");
        lock (mensagens)
            mensagens.Add(texto);
    }
}

public sealed class EnviadorDeAvisosTests(ServidorDeTeste servidor)
{
    private static CancellationToken Cancelar => TestContext.Current.CancellationToken;

    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Fabrica, MensageiroFalso Mensageiro) ComTelegram()
    {
        var mensageiro = new MensageiroFalso();
        var fabrica = servidor.Com(s =>
        {
            s.AddSingleton<IMensageiro>(mensageiro);
            s.AddScoped<EnviadorDeAvisos>();
        }, new() { ["Painel:Endereco"] = "https://triage.exemplo/" });
        return (fabrica, mensageiro);
    }

    private static async Task<int> Rodada(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> fabrica)
    {
        await using var escopo = fabrica.Services.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<EnviadorDeAvisos>().EnviarPendentes(Cancelar);
    }

    /// <summary>Uma máquina com um aviso pendente de ok para crítico no espaço livre.</summary>
    private async Task<(string Apelido, Guid Maquina, Guid Aviso)> MaquinaComAviso(int diasAtras = 1)
    {
        var apelido = $"pc-{Guid.NewGuid():N}"[..20];
        var (maquina, _) = await servidor.NovaMaquina(apelido);
        var id = Guid.Parse(maquina);
        var aviso = Guid.CreateVersion7();
        await servidor.NoBanco(async (_, b) =>
        {
            var revisao = Guid.CreateVersion7();
            b.Revisoes.Add(new RevisaoGravada
            {
                MaquinaId = id,
                Id = revisao,
                FeitaEm = DateTimeOffset.UtcNow.AddDays(-diasAtras),
                RecebidaEm = DateTimeOffset.UtcNow,
                VersaoDoAgente = "teste",
                ConteudoHash = [1],
                Foto = "{}",
            });
            b.Avisos.Add(new Aviso
            {
                Id = aviso,
                MaquinaId = id,
                RevisaoId = revisao,
                Item = "espaco-livre",
                De = "ok",
                Para = "critico",
                CriadoEm = DateTimeOffset.UtcNow.AddDays(-diasAtras),
            });
            return await b.SaveChangesAsync(Cancelar);
        });
        return (apelido, id, aviso);
    }

    [Fact]
    public async Task Aviso_vai_uma_vez_com_apelido_item_e_link()
    {
        var (fabrica, mensageiro) = ComTelegram();
        await using var _ = fabrica;
        var (apelido, maquina, aviso) = await MaquinaComAviso();

        await Rodada(fabrica);
        await Rodada(fabrica);

        var texto = Assert.Single(mensageiro.Mensagens, t => t.Contains(apelido));
        Assert.Contains($"Triage: {apelido} piorou em Espaço livre: OK -> Crítico", texto);
        Assert.EndsWith($"https://triage.exemplo/maquinas/{maquina}", texto);
        var enviado = await servidor.NoBanco((_, b) => b.Avisos.Where(a => a.Id == aviso).Select(a => a.EnviadoEm).SingleAsync(Cancelar));
        Assert.NotNull(enviado);
    }

    /// <summary>Segura cada rodada depois de listar até as duas terem listado: as duas veem o mesmo aviso pendente.</summary>
    private sealed class RodadasJuntas : PontosDeTeste
    {
        private readonly Barrier encontro = new(2);

        public override Task DepoisDeListarAvisos() =>
            Task.Run(() => encontro.SignalAndWait(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Duas_rodadas_ao_mesmo_tempo_mandam_uma_vez()
    {
        // As duas listam o aviso antes de qualquer uma marcar; só o UPDATE condicional decide quem manda
        // (contraprova: sem "enviado_em IS NULL" no UPDATE, as duas mandam)
        var mensageiro = new MensageiroFalso();
        await using var fabrica = servidor.Com(s =>
        {
            s.AddSingleton<IMensageiro>(mensageiro);
            s.AddScoped<EnviadorDeAvisos>();
            s.AddSingleton<PontosDeTeste>(new RodadasJuntas());
        });
        var (apelido, _, _) = await MaquinaComAviso();

        await Task.WhenAll(Rodada(fabrica), Rodada(fabrica));

        Assert.Single(mensageiro.Mensagens, t => t.Contains(apelido));
    }

    [Fact]
    public async Task Telegram_fora_desfaz_a_marca_e_a_proxima_rodada_manda()
    {
        var (fabrica, mensageiro) = ComTelegram();
        await using var _ = fabrica;
        var (apelido, _, aviso) = await MaquinaComAviso();
        mensageiro.Falhar = true;

        await Rodada(fabrica);
        var depoisDaFalha = await servidor.NoBanco((_, b) => b.Avisos.Where(a => a.Id == aviso)
            .Select(a => new { a.EnviadoEm, a.Tentativas }).SingleAsync(Cancelar));
        mensageiro.Falhar = false;
        await Rodada(fabrica);

        Assert.Null(depoisDaFalha.EnviadoEm);
        Assert.True(depoisDaFalha.Tentativas >= 1);
        Assert.Single(mensageiro.Mensagens, t => t.Contains(apelido));
    }

    [Fact]
    public async Task Telegram_fora_para_a_rodada_na_primeira_falha()
    {
        var (fabrica, mensageiro) = ComTelegram();
        await using var _ = fabrica;
        await MaquinaComAviso();
        await MaquinaComAviso();
        mensageiro.Falhar = true;

        await Rodada(fabrica);

        Assert.Equal(1, mensageiro.Tentativas); // os outros dariam o mesmo, cada um esperando até 10 s
    }

    [Fact]
    public void Texto_do_aviso_usa_a_hora_de_Brasilia()
    {
        var aviso = new Aviso
        {
            Id = Guid.NewGuid(),
            MaquinaId = Guid.Empty,
            RevisaoId = Guid.Empty,
            Item = "bateria",
            De = "atencao",
            Para = "critico",
        };

        var texto = EnviadorDeAvisos.Texto("pc", aviso, new DateTimeOffset(2026, 11, 1, 17, 30, 0, TimeSpan.Zero), null);

        Assert.Equal("Triage: pc piorou em Saúde da bateria: Atenção -> Crítico (revisão de 01/11 14:30).", texto);
    }

    private sealed class LogCaptado : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Linhas { get; } = new();
        public ILogger CreateLogger(string categoria) => new Registrador(this, categoria);
        public void Dispose() { }

        private sealed class Registrador(LogCaptado dono, string categoria) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel nivel) => true;
            public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? erro, Func<TState, Exception?, string> formatar) =>
                dono.Linhas.Enqueue($"{categoria}: {formatar(estado, erro)} {erro}");
        }
    }

    [Fact]
    public async Task Token_do_Telegram_nao_aparece_em_nenhum_log()
    {
        // Achado do revisor: o HttpClient escreve no log a URL de cada pedido, e a do Telegram leva o token
        const string token = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw1";
        var log = new LogCaptado();
        await using var fabrica = servidor.Com(s =>
        {
            s.AddSingleton<ILoggerProvider>(log);
            s.AddHttpClient("telegram").ConfigurePrimaryHttpMessageHandler(() => new TelegramFalso(HttpStatusCode.OK));
            // Sem a rodada automática: ela pegaria avisos pendentes dos outros testes
            foreach (var rodada in s.Where(d => d.ImplementationType == typeof(RodadaDeAvisos)).ToList())
                s.Remove(rodada);
        }, new() { ["Telegram:Token"] = token, ["Telegram:Chat"] = "42" });
        await using var escopo = fabrica.Services.CreateAsyncScope();

        await escopo.ServiceProvider.GetRequiredService<IMensageiro>().Enviar("oi", Cancelar);

        Assert.Contains(log.Linhas, l => l.Contains("System.Net.Http") || l.Contains("Microsoft")); // o log estava ligado
        Assert.DoesNotContain(log.Linhas, l => l.Contains("AAHdq"));
    }

    [Fact]
    public async Task Aviso_velho_nao_vai()
    {
        // Ligar o Telegram depois não despeja semanas de avisos antigos
        var (fabrica, mensageiro) = ComTelegram();
        await using var _ = fabrica;
        var (apelido, _, _) = await MaquinaComAviso(diasAtras: 10);

        await Rodada(fabrica);

        Assert.DoesNotContain(mensageiro.Mensagens, t => t.Contains(apelido));
    }

    [Theory]
    [InlineData("123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw1", true)]
    [InlineData("123456789:curto", false)]
    [InlineData("nao-e-token", false)]
    [InlineData(null, false)]
    public void Token_so_com_o_formato_do_BotFather(string? token, bool valido)
    {
        Assert.Equal(valido, MensageiroDoTelegram.TokenValido(token));
    }

    private sealed class TelegramFalso(HttpStatusCode codigo) : HttpMessageHandler
    {
        public HttpRequestMessage? Pedido { get; private set; }
        public string Corpo { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelar)
        {
            Pedido = pedido;
            Corpo = await pedido.Content!.ReadAsStringAsync(cancelar);
            return new HttpResponseMessage(codigo);
        }
    }

    [Fact]
    public async Task Mensageiro_chama_a_API_do_Telegram_e_nao_vaza_o_token_no_erro()
    {
        const string token = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw1";
        var ok = new TelegramFalso(HttpStatusCode.OK);
        await new MensageiroDoTelegram(new HttpClient(ok), token, "42").Enviar("oi", Cancelar);

        Assert.Equal($"https://api.telegram.org/bot{token}/sendMessage", ok.Pedido!.RequestUri!.ToString());
        using var corpo = JsonDocument.Parse(ok.Corpo);
        Assert.Equal(("42", "oi"), (corpo.RootElement.GetProperty("chat_id").GetString(), corpo.RootElement.GetProperty("text").GetString()));

        var erro = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new MensageiroDoTelegram(new HttpClient(new TelegramFalso(HttpStatusCode.Unauthorized)), token, "42").Enviar("oi", Cancelar));
        Assert.DoesNotContain("AAHdq", erro.Message);
    }
}
