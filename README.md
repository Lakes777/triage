# Triage

**Triage · revisor de PCs com Windows.**

[![CI](https://github.com/Lakes777/triage/actions/workflows/ci.yml/badge.svg)](https://github.com/Lakes777/triage/actions/workflows/ci.yml)

> Em construção: fase 1 de 10 (esqueleto). O plano inteiro está em [docs/SPEC.md](docs/SPEC.md).

Um agente que roda no computador, confere o que costuma dar problema (disco gastando, espaço acabando,
bateria cansada, atualizações paradas, telas azuis, Defender desligado...) e gera um relatório com o que é,
por que importa e como resolver. As correções simples ele faz, **sempre pedindo confirmação**. Funciona
sozinho, sem internet e sem servidor.

Opcionalmente, cada PC se inscreve num servidor (ASP.NET Core + gRPC) que guarda o histórico, mostra a
frota num painel e avisa no Telegram quando algo piora.

## Como funciona

```
 PC com Windows                                     servidor (opcional)
 ┌──────────────────────────────┐                   ┌───────────────────────────────────┐
 │ triage.exe                   │                   │ ASP.NET Core                      │
 │  ├─ lê o Windows (WMI, eventos, disco, winget)   │  ├─ gRPC: inscrição e revisões    │
 │  ├─ verificações             │ ───── gRPC ─────> │  ├─ painel (Blazor)               │
 │  ├─ relatório HTML (offline) │  chave da máquina │  ├─ PostgreSQL (histórico)        │
 │  └─ correções (confirmação)  │                   │  └─ Telegram (quando piora)       │
 └──────────────────────────────┘                   └───────────────────────────────────┘
```

| Projeto | O que é |
|---|---|
| `src/Triage.Agente` | O `triage.exe` (comandos de terminal) |
| `src/Triage.Verificacoes` | As verificações; leem o sistema só por uma interface, então são testadas no Linux |
| `src/Triage.Relatorio` | Componentes Razor do relatório local e do painel |
| `src/Triage.Contratos` | O `.proto` do gRPC, usado pelo agente e pelo servidor |
| `src/Triage.Servidor` | Servidor gRPC + painel |

## Rodando

Precisa do [.NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet test                                   # testes (rodam no Linux)
dotnet run --project src/Triage.Agente -- --versao
dotnet run --project src/Triage.Servidor      # /saude (o gRPC pede HTTP/2: porta própria na fase 6)

# o .exe do Windows, um arquivo só, com o .NET dentro (o PC não precisa ter o .NET)
dotnet publish src/Triage.Agente -c Release -r win-x64 -o publicado
```

O CI publica o `.exe` num Windows, roda e guarda o arquivo com o SHA-256 em cada execução.

## Tecnologias

C# / .NET 10 · gRPC · ASP.NET Core · Blazor · PostgreSQL · xUnit
