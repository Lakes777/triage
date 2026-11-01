namespace Triage.Relatorio;

/// <summary>
/// O CSS do relatório, embutido na página (o arquivo abre sem internet). Visual provisório: a identidade
/// própria vem na fase 9, pela /imagem-para-codigo. Gravidade nunca só pela cor: o selo traz o nome.
/// </summary>
internal static class Estilo
{
    public const string Css = """
        :root {
          color-scheme: light dark;
          --fundo: #f6f7f9; --cartao: #ffffff; --texto: #1c1f24; --suave: #5b6270; --borda: #dde1e7;
          --critico: #b42318; --critico-fundo: #fdecea; --atencao: #8a5a00; --atencao-fundo: #fff4d6;
          --ok: #1e7a3c; --ok-fundo: #e6f4ea; --pulado: #4a5160; --pulado-fundo: #eceef2;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --fundo: #15171b; --cartao: #1e2127; --texto: #e8eaee; --suave: #a3aab6; --borda: #323741;
            --critico: #ff8a80; --critico-fundo: #3a1d1b; --atencao: #f5c451; --atencao-fundo: #3a2f12;
            --ok: #7fd99a; --ok-fundo: #16301f; --pulado: #b8bfcb; --pulado-fundo: #2a2e36;
          }
        }
        * { box-sizing: border-box; }
        body {
          margin: 0; padding: 32px 16px 48px; background: var(--fundo); color: var(--texto);
          font: 16px/1.5 "Segoe UI", system-ui, sans-serif;
        }
        .topo, main, footer { max-width: 760px; margin: 0 auto; }
        .marca { margin: 0; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; color: var(--suave); font-size: 13px; }
        h1 { margin: 4px 0; font-size: 28px; }
        .quando { margin: 0 0 16px; color: var(--suave); }
        .aviso { padding: 10px 14px; border-radius: 8px; background: var(--atencao-fundo); color: var(--atencao); }
        .contagem { display: flex; flex-wrap: wrap; gap: 8px; padding: 0; margin: 16px 0 8px; list-style: none; }
        .contagem li { padding: 4px 12px; border-radius: 999px; background: var(--cartao); border: 1px solid var(--borda); }
        h2 { margin: 32px 0 12px; font-size: 18px; }
        .quantos { color: var(--suave); font-weight: 400; }
        .item { margin-bottom: 10px; background: var(--cartao); border: 1px solid var(--borda); border-left-width: 5px; border-radius: 8px; }
        .item details { padding: 12px 16px; }
        .item summary { cursor: pointer; }
        .linha { display: inline-grid; width: calc(100% - 1.5em); vertical-align: top; grid-template-columns: auto 1fr; gap: 2px 12px; align-items: baseline; }
        .item summary:focus-visible { outline: 2px solid var(--texto); outline-offset: 4px; border-radius: 4px; }
        .selo { font-size: 12px; font-weight: 700; padding: 2px 8px; border-radius: 999px; text-transform: uppercase; }
        .titulo { font-weight: 600; }
        .resumo { grid-column: 2; color: var(--suave); }
        dl { margin: 12px 0 0; }
        dt { font-weight: 600; margin-top: 8px; }
        dd { margin: 2px 0 0; }
        .g-critico { border-left-color: var(--critico); } .g-critico .selo { background: var(--critico-fundo); color: var(--critico); }
        .g-atencao { border-left-color: var(--atencao); } .g-atencao .selo { background: var(--atencao-fundo); color: var(--atencao); }
        .g-ok { border-left-color: var(--ok); } .g-ok .selo { background: var(--ok-fundo); color: var(--ok); }
        .g-pulado, .g-na { border-left-color: var(--pulado); }
        .g-pulado .selo, .g-na .selo { background: var(--pulado-fundo); color: var(--pulado); }
        footer { margin-top: 40px; color: var(--suave); font-size: 14px; }
        code { font-family: Consolas, ui-monospace, monospace; background: var(--cartao); border: 1px solid var(--borda); padding: 1px 5px; border-radius: 4px; }
        @media (max-width: 480px) { .linha { grid-template-columns: 1fr; } .resumo { grid-column: 1; } }
        """;
}
