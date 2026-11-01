using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Triage.Servidor.Demo;

namespace Triage.Servidor.Tests;

public sealed class DemoTests(ServidorDeTeste servidor)
{
    private static CancellationToken Cancelar => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Semear_repoe_a_frota_e_nunca_toca_nas_maquinas_reais()
    {
        // O critério "semear recusa banco sem demo", na forma da P31 (demo e reais no mesmo banco): a semeadura só
        // apaga e cria máquinas demo = true. Contraprova: uma máquina real com revisão continua igual
        var real = await servidor.NovaMaquina("real que não pode sumir");
        var idReal = Guid.Parse(real.Maquina);
        await servidor.Cliente().EnviarRevisaoAsync(new Contratos.Grpc.EnviarRevisaoPedido
        {
            RevisaoId = Guid.CreateVersion7().ToString(),
            FeitaEm = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddMinutes(-1)),
            VersaoDoAgente = "teste",
            FotoJson = "{}",
            Resultados = { new Contratos.Grpc.ResultadoDoItem { Item = "bateria", Gravidade = Contratos.Grpc.Gravidade.Ok } },
        }, ServidorDeTeste.Credenciais(real.Maquina, real.Chave), cancellationToken: Cancelar);

        await servidor.NoBanco((_, b) => FrotaDeDemonstracao.Semear(b, DateTimeOffset.UtcNow, Cancelar));
        await servidor.NoBanco((_, b) => FrotaDeDemonstracao.Semear(b, DateTimeOffset.UtcNow, Cancelar)); // o cron de novo

        var demo = await servidor.NoBanco((_, b) => b.Maquinas.Where(m => m.Demo).Select(m => m.Id).ToListAsync(Cancelar));
        Assert.Equal(FrotaDeDemonstracao.Ids.Order(), demo.Order());
        var revisoesDemo = await servidor.NoBanco((_, b) => b.Revisoes.CountAsync(r => demo.Contains(r.MaquinaId), Cancelar));
        Assert.Equal(FrotaDeDemonstracao.Ids.Count * FrotaDeDemonstracao.Semanas, revisoesDemo);
        Assert.False(await servidor.NoBanco((_, b) => b.Maquinas.Where(m => m.Id == idReal).Select(m => m.Demo).SingleAsync(Cancelar)));
        Assert.Equal(1, await servidor.NoBanco((_, b) => b.Revisoes.CountAsync(r => r.MaquinaId == idReal, Cancelar)));
        Assert.Equal(1, await servidor.NoBanco((_, b) => b.Resultados.CountAsync(r => r.MaquinaId == idReal, Cancelar)));
    }

    [Fact]
    public async Task Cada_PC_da_demo_mostra_o_seu_problema_pelas_regras_de_verdade()
    {
        await servidor.NoBanco((_, b) => FrotaDeDemonstracao.Semear(b, DateTimeOffset.UtcNow, Cancelar));

        async Task<string> NaUltimaSemana(string apelido, string item) => await servidor.NoBanco((_, b) =>
            (from r in b.Resultados
             join v in b.Revisoes on new { r.MaquinaId, Id = r.RevisaoId } equals new { v.MaquinaId, v.Id }
             join m in b.Maquinas on r.MaquinaId equals m.Id
             where m.Demo && m.Apelido == apelido && r.Item == item
             orderby v.FeitaEm descending
             select r.Gravidade).FirstAsync(Cancelar));

        Assert.Equal("critico", await NaUltimaSemana("PC do caixa", "espaco-livre"));
        Assert.Equal("critico", await NaUltimaSemana("Notebook do escritório", "bateria"));
        Assert.Equal("critico", await NaUltimaSemana("PC do estoque", "telas-azuis"));
        Assert.NotEqual("ok", await NaUltimaSemana("Notebook do vendedor", "antivirus"));
        Assert.Equal("atencao", await NaUltimaSemana("Notebook da recepção", "disco-saude"));
        Assert.Equal("ok", await NaUltimaSemana("PC da gerência", "espaco-livre"));

        var desgaste = await servidor.NoBanco((_, b) => b.Avisos.CountAsync(
            a => a.MaquinaId == FrotaDeDemonstracao.Ids[0] && a.Item == "desgaste-ssd", Cancelar));
        Assert.True(desgaste >= 2, "o SSD da recepção dispara e precisa de avisos de desgaste no histórico");
    }

    [Fact]
    public async Task Maquina_da_demo_nunca_vai_para_o_Telegram()
    {
        // Na mesma classe das outras semeaduras: rodando em paralelo, outra semeadura desfaria o UPDATE abaixo
        var mensageiro = new MensageiroFalso();
        await using var fabrica = servidor.Com(s =>
        {
            s.AddSingleton<Avisos.IMensageiro>(mensageiro);
            s.AddScoped<Avisos.EnviadorDeAvisos>();
        });
        await servidor.NoBanco((_, b) => FrotaDeDemonstracao.Semear(b, DateTimeOffset.UtcNow, Cancelar));
        // Mesmo um aviso da demo que ficasse pendente
        var pendentes = await servidor.NoBanco((_, b) => b.Avisos.Where(a => FrotaDeDemonstracao.Ids.Contains(a.MaquinaId))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.EnviadoEm, (DateTimeOffset?)null).SetProperty(a => a.CriadoEm, DateTimeOffset.UtcNow), Cancelar));
        Assert.True(pendentes > 0);

        await using (var escopo = fabrica.Services.CreateAsyncScope())
            await escopo.ServiceProvider.GetRequiredService<Avisos.EnviadorDeAvisos>().EnviarPendentes(Cancelar);

        Assert.DoesNotContain(mensageiro.Mensagens, t => t.Contains("Notebook da recepção") || t.Contains("PC do caixa"));
    }

    [Fact]
    public async Task Semear_antes_do_meio_dia_nao_cria_revisao_no_futuro()
    {
        var agora = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(9), TimeSpan.Zero); // 6h em Brasília

        await servidor.NoBanco((_, b) => FrotaDeDemonstracao.Semear(b, agora, Cancelar));

        var maisRecente = await servidor.NoBanco((_, b) => b.Revisoes.Where(r => FrotaDeDemonstracao.Ids.Contains(r.MaquinaId))
            .MaxAsync(r => r.FeitaEm, Cancelar));
        Assert.True(maisRecente <= agora, $"revisão em {maisRecente:u}, depois de agora ({agora:u})");
    }

    [Fact]
    public async Task Duas_semeaduras_ao_mesmo_tempo_nao_quebram()
    {
        // O cron e um semear-demo manual juntos (ids fixos colidiriam sem a trava)
        await Task.WhenAll(
            servidor.NoBanco((_, b) => FrotaDeDemonstracao.Semear(b, DateTimeOffset.UtcNow, Cancelar)),
            servidor.NoBanco((_, b) => FrotaDeDemonstracao.Semear(b, DateTimeOffset.UtcNow, Cancelar)));

        Assert.Equal(FrotaDeDemonstracao.Ids.Count, await servidor.NoBanco((_, b) => b.Maquinas.CountAsync(m => m.Demo, Cancelar)));
    }

    [Fact]
    public async Task Comando_semear_demo()
    {
        var comando = ComandosDoAdministrador.Interpretar(["semear-demo"]);
        Assert.NotNull(comando);
        var saida = new StringWriter();
        await using var escopo = servidor.Services.CreateAsyncScope();

        var codigo = await comando(escopo.ServiceProvider, saida, new StringWriter());

        Assert.Equal(0, codigo);
        Assert.Contains("6 máquinas, 48 revisões", saida.ToString());
    }
}
