# Triage · entrevista (/nova-ideia, 28/10/2026) — CONCLUÍDA (SPEC em docs/SPEC.md)

Ideia dele (28/10): script que revisa automaticamente tudo que pode estar errado num PC e gera um relatório
com instruções do que fazer e como, com opções; detecta notebook (bateria) x desktop; faz limpezas (temp etc.);
"interface você decide"; pediu mais itens nessa lógica.

Desafio feito: utilitário local não é back-end (currículo v18) e não tem demo no ar -> virou AGENTE + SERVIDOR (mini RMM).

## Fatos levantados (28/10)
- Linguagens já usadas: Python, TS/React, Go, C#, NestJS, Kotlin, Java; nunca: Rust, PowerShell (como projeto).
- VM ARM: 1 núcleo, ~3,2 GB livres de 5,9 GB (Foil, Fiel, Beacon, Cadence, Pursuit). Cota Oracle: +1 OCPU/6 GB possível.
- .NET 10.0.401 no WSL (~/.local/dotnet); Windows SEM .NET (exe self-contained resolve).
- Notebook: tem bateria; SSDs Samsung MZVLQ256 (NVMe) e HP S700 120 GB, ambos Healthy.
- Integrações: bot do Telegram (Vigil/Sidekick), Vigil, Beacon (link curto), Tidy (duplicados).
- Subras chegando = Triage pausa na fase em que estiver.

## Rodada 1 (respondida: sim a tudo, menos P5)
- P1 Para quem: portfólio + uso real (PCs dele e família); portfólio decide nos conflitos.
- P2 Formato: agente no PC + servidor com histórico e painel web; agente funciona sozinho/offline com relatório local.
- P3 Só Windows 10/11.
- P4 Só diagnostica; cada correção com confirmação, uma por uma, com registro (o que fez, quanto liberou). Sem limpador de registro/"otimizador".
- P5 Agente em **C#** (ele: "se C# for a melhor opção quero usar"; é a melhor técnica: WMI, log de eventos, Agendador, winget nativos). Rust descartado.
- P6 Sem programa com janela: agente por clique/terminal, relatório HTML no navegador; painel web no servidor; janela só fase extra.
- P7 Roda com e sem admin; marca o que pulou.
- P8 Verificações v1 (~15, com "o que é, por que importa, como resolver" + opções): saúde do disco (SMART/desgaste), espaço livre, temp + cache do Windows Update, lixeira, bateria (só notebook), atualizações pendentes, tempo desde o último reinício, inicialização pesada, drivers com erro, desligamentos inesperados/telas azuis 30 dias, Defender + firewall, programas desatualizados (winget).
  Depois (fases seguintes): Downloads grandes, duplicados (lógica do Tidy), RAM abaixo da velocidade, uso de RAM, temperatura, link de rede 100 Mbps, pontos de restauração, sfc/DISM (opcional, lento), BitLocker, SMBv1, acesso remoto aberto, DNS, Wi-Fi, latência.

## Rodada 2 (respondida: sim a todas)
- P9 Servidor C# ASP.NET Core + biblioteca de contratos compartilhada agente/servidor.
- P10 Novidades: gRPC agente->servidor + código de inscrição de uso único que vira chave própria da máquina, revogável no painel. Painel em REST.
- P11 Roda no clique + opcional semanal pelo Agendador de Tarefas (não serviço do Windows).
- P12 Postgres do Beacon, banco e usuário próprios, sem superusuário.
- P13 Demo: frota inventada (5-6 PCs, semanas de histórico, problemas variados), visitante só olha; .exe no GitHub Releases; envio ao servidor só com código gerado por ele.
- P14 .exe sem assinatura; README explica o aviso do SmartScreen + SHA-256 por versão.
- P15 Ao servidor só medidas + nome/versão de programas desatualizados; nada de nome de arquivo/usuário/pastas; PC por apelido. Relatório local pode mostrar mais.
- P16 Nome: **Triage**.

## Rodada 3 (APROVADA por ele em 29/10, todas as recomendações; P24 = sem data)
- P17 O que precisa provar: (1) verificações testadas sem Windows real (leitura atrás de interface + dados gravados do notebook dele; CI também roda o .exe num windows-latest); (2) código de inscrição usado 2x ao mesmo tempo = 1 máquina; chave revogada recusada mesmo no meio do envio (com contraprova); (3) mesmo relatório enviado 2x = gravado 1x; (4) nenhuma correção sem confirmação, toda correção com registro; (5) piora avisada 1x só.
- P18 Gravidade: ok / atenção / crítico / pulado / não se aplica; limites num arquivo com padrões (exemplos: bateria <80% atenção, <60% crítico; espaço <15% atenção, <5% crítico — números a conferir com fontes); críticos primeiro.
- P19 Telegram: avisa quando piora de estado ou desgaste do SSD sobe rápido; 1x por mudança; melhoras só no resumo; mesmo bot do Vigil/Sidekick com link da revisão.
- P20 Correções: HTML não executa nada (sem servidor local: qualquer site poderia acionar); agente pergunta no terminal item por item; HTML mostra o comando (`triage corrigir temp`).
- P21 Fora do escopo: Linux/Mac, correção sem confirmação, limpador de registro, antivírus próprio, executar correção pelo painel. Fase extra: programa com janela (WinUI 3).
- P22 Painel em Blazor renderizado no servidor (novo p/ ele) e o agente gera o relatório local com os mesmos componentes (provar que dá; senão modelo próprio); identidade pela /imagem-para-codigo.
- P23 triage.andrelagos.dev na VM ARM, Caddy do Beacon, Vigil vigiando; gRPC pelo Caddy provado na fase de publicação.
- P24 Prazo: (sem data, uma fase por sessão intercalando com /aula-java; perguntar se há data de candidatura a estágio).

## Decisões extras (29/10, respondidas)
- P25 Sem servidor: o agente funciona do começo ao fim SEM nunca ter sido inscrito (diagnóstico, relatório HTML, correções); inscrição é opcional, só para mandar relatórios. Relatório HTML autocontido (CSS/fontes/ícones embutidos, nada de CDN). Sem rede: relatório vai para fila local e sobe quando voltar (vale a P17 (3), 2x = 1x). Itens que dependem de internet (Windows Update, drivers, winget, DISM /RestoreHealth) saem como "pulado: sem conexão", nunca "ok". Teste obrigatório: agente sem rede faz tudo.
- P26 `triage corrigir --seguras`: aplica de uma vez só as correções reversíveis e de baixo risco (ex.: temporários, lixeira), mostra a lista antes e pede UMA confirmação; correções arriscadas continuam item por item. Continua valendo: nenhuma correção sem confirmação.
- Painel web (Blazor Server) e Telegram precisam de internet: aceito, painel offline fora da 1ª versão.

## Rodada 4 (29/10): ele pensou em tirar o painel web, viu o custo (sem demo clicável) e decidiu manter: APROVADA como proposta.
Proposta (recomendações minhas):
- P27 Fases (uma sessão cada): 1 esqueleto + CI (Linux testes; windows-latest gera o .exe e roda) + repo público; 2 motor de verificações (leitor atrás de interface + dados gravados do notebook) + 1ª leva offline/sem admin (espaço, temp, lixeira, reinício, bateria, disco); 3 2ª leva (atualizações, inicialização, drivers, desligamentos/telas azuis, Defender/firewall, winget) + "pulado" sem admin/sem rede; 4 relatório HTML autocontido com componentes Razor (prova da P22 cedo); 5 correções com confirmação + --seguras + registro + agendamento semanal; 6 servidor: Postgres, inscrição de uso único, chave revogável, gRPC; 7 envio + fila offline + 2x=1x; 8 histórico, pioras, Telegram 1x, frota inventada da demo; 9 interface (painel Blazor + relatório) pela /imagem-para-codigo; 10 publicação (/publicar-vm, Releases com SHA-256, Vigil). Depois /atualizar-os-quatro.
- P28 Subagentes: verificações da fase 2/3 (uma classe por verificação, sem dependência); fases 4-5 (agente) x 6 (servidor) em paralelo; telas da fase 9.
- P29 Riscos: Razor/HtmlRenderer no .exe self-contained com trimming (plano B: modelo próprio); WMI não roda no WSL (dados gravados + CI Windows + PC dele); gRPC pelo proxy da Cloudflare (precisa da opção gRPC ligada; plano B: gRPC-Web ou DNS sem proxy); correção apagar coisa real (testes em pasta falsa, 1º uso real só temp); Defender marcar .exe sem assinatura que apaga arquivos; Subras pausa.
- P30 Cortes, nesta ordem: Telegram; agendamento semanal; relatório com os mesmos componentes do painel (vira modelo próprio); itens da 2ª leva. Nunca cortar: agente offline, confirmação, inscrição concorrente, 2x=1x (são as provas da P17).
- P31 (nova, surgiu ao escrever o SPEC; APROVADA 29/10): painel com 1 conta de administrador criada por comando na VM (como o Beacon); visitante sem login vê só a frota de demonstração (demo=true), sem mudar nada; mesma instância do servidor (economiza memória), com contraprova de que máquina real sem login = 404.
