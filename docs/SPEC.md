# Triage · especificação

> Decidido com o André numa entrevista (/grill-me) em 28 e 29/10/2026. Este arquivo é o contrato do projeto:
> cada fase começa lendo-o, e qualquer decisão que mudar no caminho muda aqui primeiro (com a data).
> Os números entre parênteses (P1...P31) são as perguntas da entrevista que geraram cada decisão
> (registro completo em `docs/ENTREVISTA.md`).

## 1. O que é

Um **revisor de PCs com Windows**: um agente que roda no computador, confere tudo que costuma dar problema
(disco gastando, espaço acabando, bateria cansada, atualizações paradas, telas azuis, Defender desligado...),
gera um relatório com "o que é, por que importa e como resolver" e aplica as correções simples **só com
confirmação**. O agente funciona sozinho, sem internet e sem servidor (P2, P25).

Opcionalmente, cada máquina se inscreve num **servidor** (ASP.NET Core + gRPC) que guarda o histórico da
frota, mostra tudo num painel web e avisa no Telegram quando algo **piora**. É um mini RMM (as ferramentas
que as empresas usam para cuidar dos PCs dos funcionários): o agente prova C# perto do Windows, o servidor
prova back-end (P1, P9, P10). Uso real: os PCs do André e da família; nos conflitos, decide o portfólio (P1).

O que o projeto precisa provar (para o recrutador e na entrevista) (P17, P25):

1. As verificações são testadas **sem um Windows real**: o agente lê o sistema por uma interface, e os
   testes usam leituras gravadas de PCs de verdade; o CI também roda o `.exe` num Windows.
2. O agente faz tudo **sem rede e sem servidor**: diagnóstico, relatório e correções; o que depende de
   internet sai "pulado: sem conexão", nunca "ok".
3. Nenhuma correção acontece sem confirmação, e toda correção fica registrada (o que fez, quanto liberou).
4. Um código de inscrição usado duas vezes ao mesmo tempo gera **uma** máquina; uma chave revogada é
   recusada **mesmo no meio de um envio**.
5. O mesmo relatório enviado duas vezes (fila offline, rede caindo) é gravado **uma** vez, e cada piora
   gera **um** aviso só.

## 2. Fora do escopo (de propósito)

- Linux e Mac: só Windows 10 e 11 (P3).
- Correção sem confirmação, correção disparada pelo painel ou pelo relatório HTML (P4, P20, P21).
- Limpador de registro, "otimizador", antivírus próprio (P4, P21).
- Serviço do Windows rodando o tempo todo: o agente roda no clique ou pelo Agendador de Tarefas (P11).
- Painel offline (P25). Assinatura de código do `.exe` (P14).
- Verificações da "segunda lista" da P8 (Downloads grandes, duplicados, RAM abaixo da velocidade,
  temperatura, rede, pontos de restauração, sfc/DISM, BitLocker, SMBv1, acesso remoto, DNS, Wi-Fi): entram
  depois do núcleo, uma por vez, se sobrar fôlego.
- Primeira fase extra se sobrar fôlego: **programa com janela** (WinUI 3) para o agente (P6, P21).

## 3. Restrições

- **Custo zero.** O servidor roda na VM ARM da Oracle (1 núcleo, ~3,2 GB disponíveis de 5,9 GB em 29/10/2026),
  junto com Beacon, Pursuit, Cadence, Fiel e Foil. Meta: servidor com teto de ~256 MB (medir na fase 10).
- **Postgres do Beacon (P12):** banco `triage` com usuário próprio, dono só do seu banco, **sem superpoder**
  e com `revoke connect ... from public`. Rede `beacon_interna` com apelido único `triage-*`.
- **Desenvolvimento no WSL (8 GB):** tudo que não toca o Windows roda e é testado no WSL; suítes um projeto
  por vez. **O WSL não lê WMI nem o log de eventos do Windows:** a leitura real só é testada no CI
  (`windows-latest`) e no PC dele, rodando o `.exe` pelo Windows.
- **O Windows não tem .NET instalado:** o agente sai como `.exe` único autocontido (`win-x64`) (P14).
- **Privacidade (P15):** ao servidor só vão medidas e nome/versão de programas desatualizados; nunca nome de
  arquivo, de usuário ou de pasta; o PC aparece por apelido. As leituras gravadas que vão para o repositório
  (público) seguem a mesma regra.

## 4. Tecnologia

| Parte | Escolha |
|---|---|
| Linguagem | C# / .NET 10 (SDK 10.0.401 no WSL) no agente e no servidor (P5, P9) |
| Agente | Aplicativo de terminal, `.exe` único autocontido `win-x64`; WMI (`System.Management`), log de eventos, `winget`, Agendador de Tarefas (P5, P6, P11) |
| Contratos | Biblioteca compartilhada + `.proto` (P9, P10) |
| Agente -> servidor | gRPC (`Grpc.AspNetCore` / `Grpc.Net.Client`) com chave por máquina (P10) |
| Servidor | ASP.NET Core 10 |
| Painel | Blazor renderizado no servidor (P22) |
| Relatório local | Componentes Razor renderizados para HTML estático (`HtmlRenderer`), os mesmos do painel (P22) |
| Banco | PostgreSQL 17 (o do Beacon), EF Core + Npgsql, migrações do EF |
| Testes | xUnit v3, Testcontainers (Postgres), bUnit nos componentes |
| Avisos | Bot do Telegram que já existe (Vigil/Sidekick) (P19) |
| Deploy | Docker (imagem ARM montada no PC), Caddy do Beacon, `/publicar-vm`; `.exe` no GitHub Releases com SHA-256 (P14, P23) |

> 29/10/2026: a P10 dizia "painel em REST". Com o Blazor no servidor (P22) o painel chama os serviços no mesmo
> processo e **não existe API REST pública** na v1. O gRPC é só do agente.

Novidades para ele: gRPC, Blazor, `HtmlRenderer`, APIs do Windows (WMI, log de eventos, DPAPI), `.exe`
autocontido com Releases.

Descartado: Rust (P5; menos maduro para WMI e Agendador); serviço do Windows (P11); PowerShell como
projeto (o agente pode chamar comandos, mas o projeto é C#); REST para o agente (gRPC é a novidade, P10).

## 5. Arquitetura

```
 PC com Windows                                     VM ARM (triage.andrelagos.dev)
 ┌──────────────────────────────┐                   ┌───────────────────────────────────┐
 │ triage.exe                   │                   │ triage-servidor (ASP.NET Core)    │
 │  ├─ leitor do Windows ─> WMI, eventos,           │  ├─ gRPC: Inscrever, EnviarRevisao│
 │  │   disco, winget            │   gRPC (opcional)│  ├─ painel Blazor (navegador)     │
 │  ├─ verificações (puras)     │ ────────────────> │  ├─ Postgres do Beacon (triage)   │
 │  ├─ relatório HTML (offline) │  chave da máquina │  └─ Telegram (pioras)             │
 │  ├─ correções (confirmação)  │                   └───────────────────────────────────┘
 │  └─ fila local de envios     │
 └──────────────────────────────┘
```

Projetos da solução:

- `Triage.Contratos`: `.proto`, gravidades, identificadores dos itens. Sem dependência do Windows.
- `Triage.Verificacoes`: o motor e cada verificação como classe própria; lê o sistema **só** pela interface
  `ILeitorDoSistema`. Roda e é testado no Linux.
- `Triage.Relatorio`: biblioteca de componentes Razor, usada pelo agente (HTML estático) e pelo painel.
- `Triage.Agente`: o `.exe`. Leitor do Windows, correções, registro, fila, envio, comandos de terminal.
- `Triage.Servidor`: gRPC, painel, banco, avisos.
- `tests/`: um projeto de teste por projeto; leituras gravadas em `tests/dados/*.json`.

Comandos do agente (nomes finais na fase 1): `triage revisar` (gera o relatório e abre no navegador),
`triage corrigir <item>`, `triage corrigir --seguras`, `triage inscrever <código> <apelido>`,
`triage enviar` (esvazia a fila), `triage agendar` / `triage desagendar`.

Arquivos locais do agente em `%LOCALAPPDATA%\Triage\`: relatórios, `correcoes.jsonl` (registro), `fila/`
(revisões esperando envio), `chave` (protegida com DPAPI do usuário), `limites.json` opcional.

Banco (rascunho, fecha na fase 6):

- `maquinas`: id, apelido, `chave_hash` (SHA-256), inscrita_em, revogada_em, `demo` (bool).
- `codigos_inscricao`: `codigo_hash`, criado_em, expira_em, usado_em, maquina_id.
- `revisoes`: id **gerado pelo agente** (UUID v7), maquina_id, feita_em, recebida_em, versão do agente,
  `conteudo_hash`; único (maquina_id, id).
- `resultados`: revisao_id, item, gravidade, medida (jsonb).
- `avisos`: maquina_id, item, de, para, revisao_id, enviado_em; único por transição.

## 6. Regras de negócio

- **Gravidade (P18):** `ok`, `atencao`, `critico`, `pulado` (com motivo: sem admin, sem conexão, não
  suportado), `nao_se_aplica` (ex.: bateria num desktop). Limites num arquivo de padrões embutido, que o
  `limites.json` local pode sobrescrever. Cada número tem a fonte anotada no código (conferir na fase 2;
  ponto de partida: bateria < 80% atenção / < 60% crítico; espaço livre < 15% atenção / < 5% crítico).
  O relatório mostra os críticos primeiro.
- **Pulado nunca é ok (P7, P25):** sem admin ou sem rede, o item sai `pulado` com o motivo. Contraprova:
  um leitor que simula "sem rede" faz o teste falhar se o item virar `ok`.
- **Correções (P4, P20, P26):** cada correção pergunta no terminal antes de agir; o HTML só mostra o
  comando. `--seguras` aplica de uma vez as correções marcadas como **reversíveis e de baixo risco** (lista
  fechada, fixada por teste: temporários do usuário, lixeira; o resto entra só com motivo escrito aqui), mostra
  a lista e pede **uma** confirmação. Correção só apaga dentro das pastas previstas; arquivo em uso é pulado e
  contado. Todo resultado vai para `correcoes.jsonl`. Contraprova: sem a pergunta, o teste quebra.
- **Inscrição (P10):** o código é de uso único, curto prazo (24 h) e guardado só como hash. Usar o código é um
  `UPDATE ... WHERE usado_em IS NULL AND expira_em > now()` que precisa afetar 1 linha; a chave da máquina é
  aleatória (32 bytes), guardada como SHA-256 e comparada em tempo constante (como a chave de integração do
  Beacon). Teste: 20 inscrições simultâneas com o mesmo código = 1 máquina; contraprova sem a condição.
- **Revogação no meio do envio (P17):** gravar uma revisão e revogar a máquina disputam a mesma linha de
  `maquinas` (o envio trava para leitura, a revogação para escrita), então a revisão grava antes ou é recusada,
  nunca depois. Contraprova: sem a trava, o teste de corrida grava revisão de máquina revogada.
- **Envio repetido (P17, P25):** o id da revisão nasce no agente. Mesmo id e mesmo conteúdo = sucesso sem
  gravar de novo; mesmo id com conteúdo diferente = erro. O agente só tira da fila depois da confirmação.
- **Pioras fora de ordem:** a fila offline pode entregar revisões antigas depois de novas. A comparação usa
  `feita_em` (quando o agente revisou), não `recebida_em`, e uma revisão mais velha que a última não gera aviso.
- **Avisos (P19):** um aviso por transição para pior (`ok -> atencao`, `atencao -> critico`...) ou desgaste
  do SSD subindo rápido (regra exata na fase 8); melhoras só no resumo. Único no banco: repetir o envio não
  repete o aviso.
- **Privacidade (P15):** o contrato gRPC não tem campo para caminho, usuário ou nome de arquivo; um teste
  confere o `.proto` e uma revisão real gravada.
- **Painel (P31):** o visitante sem login vê **só a frota de demonstração** (`demo = true`) e não muda nada;
  o administrador (uma conta, criada por comando na VM, como no Beacon) vê as máquinas reais, gera códigos e
  revoga chaves. Contraprova: o teste que pede uma máquina real sem login recebe 404.

## 7. Integrações

- **Postgres e Caddy do Beacon** (P12, P23), como Pursuit, Cadence, Fiel e Foil.
- **Telegram:** o bot que já avisa pelo Vigil/Sidekick, com o link da máquina no painel (P19).
- **Vigil:** monitora `https://triage.andrelagos.dev/saude` (P23).

## 8. Demonstração

- Em `triage.andrelagos.dev`, sem cadastro: uma **frota inventada** de 5 a 6 PCs com semanas de histórico e
  problemas variados (SSD gastando, bateria caindo, disco enchendo, tela azul, Defender desligado) (P13).
- A frota é semeada por comando e reposta por um cron diário na VM (como as demos do Pursuit e do Beacon).
- O visitante só olha (P31). O `.exe` está no GitHub Releases com SHA-256, e o README explica o aviso do
  SmartScreen (P14): quem baixa usa o agente sozinho, sem servidor; enviar ao servidor exige um código
  gerado pelo administrador (P13).
- Limites: inscrição por IP, envio por máquina, tamanho máximo da revisão.

## 9. Fases e critérios de aceite

Cada fase: testes verdes no CI, revisor antes do commit, contraprovas nas regras importantes,
README atualizado. Uma sessão nova por fase, começando por este arquivo.

| Fase | Entrega | Pronto quando |
|---|---|---|
| 1. Esqueleto | Solução com os 5 projetos e testes, `.proto` mínimo, CI (testes no Linux; `windows-latest` gera o `.exe` e roda `triage --versao`), repositório público | CI verde nos dois; o `.exe` baixado do CI abre no notebook dele |
| 2. Motor + 1ª leva | `ILeitorDoSistema` (Windows e gravado), gravidades, limites, `triage revisar` em texto; saúde do disco, espaço livre, temporários, cache do Windows Update, lixeira, último reinício, bateria | Rodando no notebook, sem rede e sem admin; leitura gravada do notebook (sem dados pessoais) nos testes; mudar um limite muda a gravidade; bateria `nao_se_aplica` numa leitura de desktop |
| 3. 2ª leva | Atualizações pendentes, inicialização pesada, drivers com erro, desligamentos inesperados, telas azuis (30 dias), Defender, firewall, programas desatualizados (`winget`) | ~15 itens no notebook; sem admin ou sem rede = `pulado` com motivo (contraprova) |
| 4. Relatório | Componentes Razor em `Triage.Relatorio`, HTML estático pelo `HtmlRenderer`, abre no navegador | Funciona no `.exe` autocontido (prova da P22); teste falha se o HTML tiver qualquer endereço externo ou script que execute ação |
| 5. Correções | `corrigir <item>`, `--seguras`, registro, `agendar` semanal | Testes numa pasta falsa: nada sem confirmação (contraprova); `--seguras` só com a lista fixada; primeiro uso real no notebook só nos temporários |
| 6. Servidor | ASP.NET Core, banco, `Inscrever` e `EnviarRevisao` por gRPC, comando de administrador (gerar código, revogar) | 20 inscrições simultâneas = 1 máquina; revogação no meio do envio (contraprova); chave errada recusada |
| 7. Envio | `triage inscrever`, fila offline, `triage enviar`, envio ao fim do `revisar` se inscrito | Sem rede a revisão fica na fila e sobe depois; mesma revisão 2x = 1 gravação; conteúdo diferente com o mesmo id = erro |
| 8. Histórico e avisos | Comparação com a revisão anterior, pioras, Telegram, frota de demonstração semeada | 1 piora = 1 aviso, mesmo repetindo o envio; revisão antiga chegando depois não avisa; semear recusa banco sem `demo` |
| 9. Interface | Painel Blazor (frota, máquina, revisão, inscrições, login do administrador) e identidade própria pela `/imagem-para-codigo`, aplicada ao painel e ao relatório local | Visitante só vê a demo (contraprova 404); acessível por teclado; GIF e prints no README |
| 10. Publicação | `triage.andrelagos.dev` via `/publicar-vm` (só manual), gRPC pelo Caddy, Releases com SHA-256, cron da demo | No ar com HTTPS; `.exe` do Releases envia ao servidor de verdade; os outros sites da VM continuam respondendo; Vigil monitorando |

> 29/10/2026 (fase 1, decidido ao fazer):
> - Testes em `tests/Triage.<Projeto>.Tests` (padrão do Beacon). `Verificacoes` e `Relatorio` ganham projeto de teste
>   nas fases 2 e 4: o xUnit v3 sem nenhum teste encerra com erro e quebraria o CI.
> - Versão única `0.1.0` no `Directory.Build.props` para agente e servidor; o `--versao` mostra o commit curto
>   (`0.1.0+9c3e94b`). gRPC 2.85, `.proto` em `triage.v1` com só `Versao` por enquanto.
> - O agente força a saída em UTF-8 (o console do Windows nasce em 850/1252 e "versão" saía quebrado). Símbolos de
>   depuração embutidos (`DebugType embedded`): o `.exe` sai sozinho, ~38 MB com compressão, sem trimming.
> - **Para a fase 6 (revisor):** sem HTTPS o Kestrel só fala HTTP/1.1, e o gRPC exige HTTP/2 (o teste passa porque o
>   `TestServer` não usa o Kestrel). Duas portas: uma `Http1` para `/saude` e o painel (WebSocket do Blazor), outra
>   `Http2` para o gRPC, com o Caddy em `h2c://`. E a imagem Docker é montada sem `.git`: o servidor diz `0.1.0` e o
>   `.exe` `0.1.0+abc1234`, então comparar versões só pela parte antes do `+`.
> - **Para a fase 2 (P11, "roda no clique"):** hoje, sem argumento, o agente mostra a ajuda e a janela do duplo clique
>   fecha na hora. Na fase 2, sem argumento = `revisar` e esperar Enter antes de fechar.

> 29/10/2026 (fase 2, decidido ao fazer):
> - **A leitura é uma foto só** (`FotoDoSistema`, lida de uma vez por `ILeitorDoSistema.Ler()`), com cada parte num
>   `Lido<T>` (valor ou motivo do pulo). Vira JSON no `triage gravar` e nos testes (`tests/dados/`). Comando novo:
>   `triage revisar --leitura <arq>` revisa uma gravação, o que permite rodar o agente inteiro no WSL.
> - `Avaliacao` recusa pulado sem motivo e motivo sem pulado; o motor transforma exceção numa verificação em pulado
>   (`Erro`) sem derrubar as outras. Contraprovas feitas: bateria sem leitura, verificação com defeito e disco sem
>   saúde viram "ok" e os testes quebram.
> - **Sem admin, medido no notebook (29/10):** desgaste do SSD (`MSFT_StorageReliabilityCounter`) e
>   `C:\Windows\Temp` ficam de fora e aparecem no resumo; a capacidade original da bateria não sai por WMI
>   (`BatteryStaticData`) e vem do `powercfg /batteryreport /xml`, que funciona para qualquer usuário.
>   O CI (runner administrador) confere que volumes, discos, `C:\Windows\Temp`, cache e reinício foram lidos; o
>   desgaste real e o casamento de `DeviceId` entre os dois WMIs só se provam rodando como admin num PC de verdade.
> - O item segue "ok" quando lê a parte principal e só um detalhe ficou de fora (disco sem o desgaste, temporários
>   sem a pasta do Windows): o detalhe vai no resumo. Só vira pulado quando não sobra nada para avaliar.
> - Último reinício pelo `LastBootUpTime` (a inicialização rápida faz "Desligar" não contar, e isso é dito ao usuário).
> - Sem argumento = revisar e esperar Enter (duplo clique, P11). `limites.json` com erro para a revisão com aviso.
> - Revisão leva ~4 s no notebook. Números finais dos limites: ver `Limites.cs` (fonte em cada um).

Boas para subagentes (em worktree):

- **Fases 2 e 3:** cada verificação é uma classe sem dependência das outras, lendo só pela
  `ILeitorDoSistema`. Dá para dar 3 ou 4 verificações para cada subagente sem conflito de arquivo.
- **Fases 4-5 (agente) e 6 (servidor) em paralelo:** só se encontram nos contratos, que existem desde a fase 1.
- **Fase 9:** as telas do painel são independentes (como no Cadence); o relatório local pode ser outro subagente.

Depois da última fase: currículo, portfólio, perfil e guia (`/atualizar-os-quatro`).

## 10. Riscos conhecidos

- **Razor no `.exe` autocontido:** o corte de código não usado (trimming) costuma quebrar o que depende de
  reflexão. Por isso o relatório vem cedo (fase 4); se não der, publicar sem trimming ou relatório em modelo
  próprio (P22).
- **gRPC pela Cloudflare:** com o proxy ligado, o gRPC só passa com a opção gRPC ativada no painel da
  Cloudflare. Plano B: gRPC-Web ou o subdomínio sem proxy. Testar um "olá" já na fase 6, se ele quiser.
- **Leitura real do Windows:** só no CI e no PC dele (o WSL não lê WMI); o runner do CI não tem bateria nem
  SMART, então lá esses itens saem `nao_se_aplica`/`pulado`.
- **Defender e SmartScreen:** um `.exe` sem assinatura que apaga arquivos pode ser bloqueado. Descobrir na
  fase 1 (primeiro `.exe`) e documentar no README.
  > 29/10/2026: **CONFIRMADO, e pior que o SmartScreen.** O Controle Inteligente de Aplicativos (Smart App Control,
  > ligado no notebook dele) passou a bloquear o `.exe` da fase 2 sem botão "executar assim mesmo" (Code Integrity
  > 3077/3033). Decide pela reputação de cada arquivo novo sem assinatura na nuvem, então libera um build e bloqueia
  > o seguinte. Vem ligado em instalações novas do Windows 11: atinge o uso real na família (P1). Decisão dele
  > (muda a P14): **assinar pela SignPath Foundation** (grátis para código aberto) e, até lá, testar pelo CI e pelas
  > leituras gravadas. Feito: licença MIT (58e09c2), seções de privacidade e download no README. Falta: (1) uma
  > release no GitHub (a SignPath só assina o que já foi publicado; precisa da autorização dele); (2) ele pedir em
  > signpath.org/apply com a conta dele, 2FA ligado no GitHub; (3) depois de aceito: seção "Code signing policy" no
  > README (crédito SignPath.io + SignPath Foundation, papéis do time, link da privacidade), nome e versão do produto
  > no artefato, aprovação manual a cada release, assinatura no CI. Plano B na fase 10, se recusarem: certificado de
  > código aberto da Certum (pago, quebra o custo zero; decisão dele).
- **Correção apagar algo real:** testes só em pasta falsa; lista de pastas fechada; primeiro uso real só nos
  temporários.
- **Vazar a frota real no painel público:** regra da P31 com contraprova na fase 9.
- **Cortes, nesta ordem, se não couber (P30):** Telegram; agendamento semanal; relatório com os mesmos
  componentes do painel (vira modelo próprio); itens da 2ª leva. Nunca cortar as provas da seção 1.
- **Prioridade:** se os documentos do Subras chegarem, este projeto pausa na fase em que estiver.
