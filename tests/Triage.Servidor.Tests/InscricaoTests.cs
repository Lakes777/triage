using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Triage.Contratos.Grpc;
using Triage.Servidor.Banco;

namespace Triage.Servidor.Tests;

public sealed class InscricaoTests(ServidorDeTeste servidor)
{
    private static CancellationToken Cancelar => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Codigo_valido_vira_maquina_com_chave_guardada_so_como_hash()
    {
        var codigo = await servidor.NoBanco((i, _) => i.GerarCodigo(Cancelar));

        var resposta = await servidor.Cliente().InscreverAsync(
            new InscreverPedido { Codigo = codigo, Apelido = "  notebook da sala  " }, cancellationToken: Cancelar);

        Assert.Equal(43, resposta.Chave.Length);
        var maquina = await servidor.NoBanco((_, b) => b.Maquinas.SingleAsync(m => m.Id == Guid.Parse(resposta.MaquinaId), Cancelar));
        Assert.Equal("notebook da sala", maquina.Apelido);
        Assert.Equal(Regras.Hash(resposta.Chave), maquina.ChaveHash);
        Assert.Null(maquina.RevogadaEm);
        var guardado = await servidor.NoBanco((_, b) => b.Codigos.SingleAsync(c => c.MaquinaId == maquina.Id, Cancelar));
        Assert.Equal(Regras.Hash(Regras.NormalizarCodigo(codigo)!), guardado.CodigoHash); // o código em si não está lá
    }

    [Fact]
    public async Task Codigo_digitado_em_minusculas_e_sem_tracos_vale()
    {
        var codigo = await servidor.NoBanco((i, _) => i.GerarCodigo(Cancelar));

        var resposta = await servidor.Cliente().InscreverAsync(
            new InscreverPedido { Codigo = codigo.Replace("-", " ").ToLowerInvariant(), Apelido = "pc" },
            cancellationToken: Cancelar);

        Assert.NotEmpty(resposta.Chave);
    }

    [Fact]
    public async Task Vinte_inscricoes_ao_mesmo_tempo_com_o_mesmo_codigo_criam_uma_maquina()
    {
        // P17 (2): o mesmo código mandado por 20 PCs (ou 20 cliques) no mesmo instante
        var codigo = await servidor.NoBanco((i, _) => i.GerarCodigo(Cancelar));
        var apelido = $"corrida-{Guid.NewGuid():N}"[..30];
        var cliente = servidor.Cliente();
        using var largada = new SemaphoreSlim(0);

        var tentativas = Enumerable.Range(0, 20).Select(async _ =>
        {
            await largada.WaitAsync(Cancelar);
            try
            {
                return (await cliente.InscreverAsync(new InscreverPedido { Codigo = codigo, Apelido = apelido },
                    cancellationToken: Cancelar)).MaquinaId;
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.InvalidArgument)
            {
                return null;
            }
        }).ToList();
        largada.Release(20);
        var resultados = await Task.WhenAll(tentativas);

        Assert.Single(resultados, r => r is not null);
        Assert.Equal(1, await servidor.NoBanco((_, b) => b.Maquinas.CountAsync(m => m.Apelido == apelido, Cancelar)));
    }

    [Fact]
    public async Task Codigo_usado_nao_vale_de_novo()
    {
        var codigo = await servidor.NoBanco((i, _) => i.GerarCodigo(Cancelar));
        await servidor.Cliente().InscreverAsync(new InscreverPedido { Codigo = codigo, Apelido = "pc" }, cancellationToken: Cancelar);

        var erro = await Assert.ThrowsAsync<RpcException>(() => servidor.Cliente().InscreverAsync(
            new InscreverPedido { Codigo = codigo, Apelido = "outro" }, cancellationToken: Cancelar).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, erro.StatusCode);
        Assert.Contains("já usado ou vencido", erro.Status.Detail);
    }

    [Fact]
    public async Task Codigo_vencido_nao_vale()
    {
        var codigo = "AAAA-BBBB-CCCC";
        await servidor.NoBanco(async (_, b) =>
        {
            b.Codigos.Add(new CodigoDeInscricao
            {
                Id = Guid.CreateVersion7(),
                CodigoHash = Regras.Hash(Regras.NormalizarCodigo(codigo)!),
                CriadoEm = DateTimeOffset.UtcNow.AddHours(-25),
                ExpiraEm = DateTimeOffset.UtcNow.AddHours(-1),
            });
            return await b.SaveChangesAsync(Cancelar);
        });

        var erro = await Assert.ThrowsAsync<RpcException>(() => servidor.Cliente().InscreverAsync(
            new InscreverPedido { Codigo = codigo, Apelido = "pc" }, cancellationToken: Cancelar).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, erro.StatusCode);
    }

    [Theory]
    [InlineData("AAAA-BBBB-CCC")] // curto
    [InlineData("IIII-LLLL-OOOO")] // letras fora do alfabeto
    [InlineData("")]
    [InlineData("ZZZZ-ZZZZ-ZZZZ")] // formato certo, mas ninguém gerou
    public async Task Codigo_que_nao_existe_tem_a_mesma_resposta(string codigo)
    {
        var erro = await Assert.ThrowsAsync<RpcException>(() => servidor.Cliente().InscreverAsync(
            new InscreverPedido { Codigo = codigo, Apelido = "pc" }, cancellationToken: Cancelar).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, erro.StatusCode);
        Assert.Equal("Código inválido, já usado ou vencido. Peça um novo ao administrador.", erro.Status.Detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("um apelido com mais de quarenta caracteres!!")]
    [InlineData("com\nquebra")]
    [InlineData("pc\u202Eexe.txt")] // inversão de direção: mostraria "txt.exe" no terminal
    [InlineData("pc\u200Binvisivel")] // largura zero
    public async Task Apelido_invalido_e_recusado_sem_gastar_o_codigo(string apelido)
    {
        var codigo = await servidor.NoBanco((i, _) => i.GerarCodigo(Cancelar));

        var erro = await Assert.ThrowsAsync<RpcException>(() => servidor.Cliente().InscreverAsync(
            new InscreverPedido { Codigo = codigo, Apelido = apelido }, cancellationToken: Cancelar).ResponseAsync);

        Assert.Contains("apelido", erro.Status.Detail);
        // O código continua valendo para uma tentativa certa
        var certo = await servidor.Cliente().InscreverAsync(new InscreverPedido { Codigo = codigo, Apelido = "pc" },
            cancellationToken: Cancelar);
        Assert.NotEmpty(certo.Chave);
    }

    [Fact]
    public async Task Inscricao_tem_limite_por_IP()
    {
        await using var fabrica = servidor.Com(configuracao: new() { ["Limites:InscricoesPorMinuto"] = "3" });
        var cliente = ServidorDeTeste.Cliente(fabrica);
        async Task<StatusCode> Tentar()
        {
            try
            {
                await cliente.InscreverAsync(new InscreverPedido { Codigo = "ZZZZ-ZZZZ-ZZZZ", Apelido = "pc" },
                    cancellationToken: Cancelar);
                return StatusCode.OK;
            }
            catch (RpcException e)
            {
                return e.StatusCode;
            }
        }

        var primeiras = new[] { await Tentar(), await Tentar(), await Tentar() };
        var quarta = await Tentar();

        Assert.All(primeiras, s => Assert.Equal(StatusCode.InvalidArgument, s));
        Assert.Equal(StatusCode.Unavailable, quarta); // HTTP 429: nem chegou a olhar o código
    }

    [Fact]
    public async Task Barra_no_fim_do_caminho_nao_escapa_do_limite()
    {
        // Achado do revisor: o roteamento aceita "/Inscrever/", e o limite antigo comparava o caminho como texto
        await using var fabrica = servidor.Com(configuracao: new() { ["Limites:InscricoesPorMinuto"] = "2" });
        var canal = GrpcChannel.ForAddress(fabrica.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = fabrica.Server.CreateHandler() });
        var comBarra = new Method<InscreverPedido, InscreverResposta>(MethodType.Unary, "triage.v1.Triagem", "Inscrever/",
            Marshallers.Create(p => p.ToByteArray(), InscreverPedido.Parser.ParseFrom),
            Marshallers.Create(r => r.ToByteArray(), InscreverResposta.Parser.ParseFrom));
        var invocador = canal.CreateCallInvoker();
        async Task<StatusCode> Tentar()
        {
            try
            {
                await invocador.AsyncUnaryCall(comBarra, null, new CallOptions(cancellationToken: Cancelar),
                    new InscreverPedido { Codigo = "ZZZZ-ZZZZ-ZZZZ", Apelido = "pc" });
                return StatusCode.OK;
            }
            catch (RpcException e)
            {
                return e.StatusCode;
            }
        }

        var codigos = new[] { await Tentar(), await Tentar(), await Tentar() };

        // As duas primeiras chegam à inscrição (o caminho com barra existe); a terceira é barrada pelo limite
        Assert.Equal([StatusCode.InvalidArgument, StatusCode.InvalidArgument, StatusCode.Unavailable], codigos);
    }

    [Fact]
    public void Codigo_novo_tem_12_caracteres_do_alfabeto_sem_letras_confusas()
    {
        var codigos = Enumerable.Range(0, 200).Select(_ => Regras.NovoCodigo()).ToList();

        Assert.All(codigos, c => Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", c));
        Assert.Equal(200, codigos.Distinct().Count());
    }
}
