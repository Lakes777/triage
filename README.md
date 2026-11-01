# Triage

**Triage · revisor de PCs com Windows.**

[![CI](https://github.com/Lakes777/triage/actions/workflows/ci.yml/badge.svg)](https://github.com/Lakes777/triage/actions/workflows/ci.yml)

> Em construção: fase 5 de 10 (15 verificações, relatório HTML e correções com confirmação). O plano inteiro está em [docs/SPEC.md](docs/SPEC.md).

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

## O que ele confere (até agora)

Saúde dos discos (e o desgaste dos SSDs, com admin) · espaço livre · arquivos temporários · cache do Windows
Update · lixeira · tempo sem reiniciar · saúde da bateria (só em notebook) · atualizações do Windows ·
programas desatualizados (`winget`) · programas na inicialização · drivers com erro · desligamentos
inesperados · telas azuis · antivírus · firewall. Cada item sai como **ok**,
**atenção**, **crítico**, **pulado** (com o motivo: sem admin, sem conexão...) ou **não se aplica**, e os
limites podem ser trocados num `limites.json` em `%LOCALAPPDATA%\Triage`.

```
> triage revisar
Triage · revisão de 29/10/2026 11:50
Rodando sem admin: alguns itens ficam de fora (rode como administrador para ver tudo).

[OK           ] Saúde dos discos: SAMSUNG MZVLQ256HBJD-00B (SSD NVMe, 238,5 GB): saudável (desgaste: precisa de admin); ...
[OK           ] Espaço livre: C: 120,3 GB livres de 222,7 GB (54%); D: 91,6 GB livres de 111,8 GB (81%)
[OK           ] Arquivos temporários: 551 MB em 91 arquivos (a pasta do Windows ficou de fora: precisa de admin)
[OK           ] Cache do Windows Update: 6 MB em 4 arquivos
[OK           ] Lixeira: 223 MB em 14 itens
[OK           ] Tempo sem reiniciar: sem reiniciar há 1 dia e 17 h (desde 27/10 18:46)
[OK           ] Saúde da bateria: 88% da capacidade original (38,1 de 43,1 Wh), 22 ciclos de carga

0 crítico · 0 atenção · 7 ok · 0 pulado

Relatório completo: C:\Users\...\AppData\Local\Triage\relatorios\revisao-2026-10-29-115024.html
```

O relatório completo abre no navegador: um arquivo HTML só, sem internet, com o que é, por que importa e como
resolver cada item (os problemas primeiro). Ele **não executa nada**: não tem script nem endereço externo (um
teste garante, e a página ainda pede ao navegador para bloquear os dois). As correções são feitas no terminal,
com confirmação. Os 20 relatórios mais recentes ficam guardados; `triage revisar --sem-abrir` gera sem abrir.

### Correções

```
> triage corrigir --seguras
Correções seguras que serão feitas:
  - Arquivos temporários: 412 MB em 63 arquivo(s) com mais de 24 h, nos temporários do usuário
  - Lixeira: 223 MB em 14 item(ns), apagados para sempre (confira antes se não quer nada de volta)
Confirma as 2 correções? Digite s e Enter para confirmar (qualquer outra coisa cancela): s
Arquivos temporários: liberou 412 MB em 63 arquivo(s).
Lixeira: liberou 223 MB em 14 item(ns).
```

Nada é apagado sem você digitar `s` (Enter vazio cancela, e um teste quebra se a pergunta sumir). Só saem
arquivos criados e modificados há mais de 24 h (um instalador aberto agora não perde nada), só de dentro das pastas
previstas, e links ou junções para outras pastas nunca são seguidos;
arquivo em uso fica e é contado. `triage corrigir temporarios`, `lixeira` ou `cache-windows-update` (este como
administrador: para o Windows Update durante a limpeza e religa depois) fazem uma de cada vez. Tudo fica anotado em
`%LOCALAPPDATA%\Triage\correcoes.jsonl` (o que fez e quanto liberou, sem nome de arquivo).

`triage agendar` põe uma revisão toda segunda às 12h no Agendador de Tarefas (roda ao ligar se o PC estava
desligado); ela só revisa e guarda o relatório, nunca corrige. `triage desagendar` tira.

As verificações nunca leem o Windows direto: recebem uma "foto" do sistema. No `.exe` a foto vem do Windows
(WMI, `powercfg`, API da lixeira); nos testes, de leituras gravadas de PCs de verdade com `triage gravar`
(só medidas, sem nome de usuário, arquivo ou pasta). Por isso as regras rodam e são testadas no Linux.

## Rodando

Precisa do [.NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet test                                   # testes (rodam no Linux)
dotnet run --project src/Triage.Agente -- revisar --leitura tests/dados/notebook-sem-admin.json
dotnet run --project src/Triage.Servidor      # /saude (o gRPC pede HTTP/2: porta própria na fase 6)

# o .exe do Windows, um arquivo só, com o .NET dentro (o PC não precisa ter o .NET)
dotnet publish src/Triage.Agente -c Release -r win-x64 -o publicado
```

O CI publica o `.exe` num Windows, roda a revisão de verdade e guarda o `.exe` (com o SHA-256) e o relatório
gerado lá em cada execução.

## Privacidade

O Triage não manda nada para lugar nenhum por conta própria. Ele só usa a internet para perguntar à Microsoft se há
atualizações (Windows Update e `winget`), sem enviar dados seus; sem internet, esses dois itens saem como
"pulado: sem conexão" e todo o resto funciona. O envio ao
servidor só acontece se você inscrever a máquina com um código (`triage inscrever`, fase 7), e mesmo assim só
vão medidas e nome/versão de programas desatualizados: nunca nome de arquivo, de usuário ou de pasta.
Nenhuma correção acontece sem você confirmar.

## Baixando o .exe

As versões ficam em [Releases](https://github.com/Lakes777/triage/releases), com o SHA-256 ao lado. O `.exe`
ainda **não é assinado**: o Windows pode avisar (SmartScreen: "Mais informações" > "Executar assim mesmo") ou,
com o Controle Inteligente de Aplicativos ligado, bloquear sem opção. A assinatura gratuita para código aberto
(SignPath Foundation) está sendo pedida.

## Tecnologias

C# / .NET 10 · gRPC · ASP.NET Core · Blazor · PostgreSQL · xUnit

## Licença

[MIT](LICENSE)
