namespace PrintServer.WebUi;

internal static class Styles
{
    public const string Css = """
        :root { --bg:#fff; --fg:#1b1f24; --muted:#6a737d; --line:#d8dee4; --card:#f6f8fa; --accent:#0969da; --ok:#1a7f37; --bad:#cf222e; --info:#0969da; }
        @media (prefers-color-scheme: dark) { :root { --bg:#0d1117; --fg:#e6edf3; --muted:#8b949e; --line:#30363d; --card:#161b22; --accent:#58a6ff; --ok:#3fb950; --bad:#f85149; --info:#58a6ff; } }
        * { box-sizing:border-box; }
        body { margin:0; background:var(--bg); color:var(--fg); font:15px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif; }
        a { color:var(--accent); text-decoration:none; } a:hover { text-decoration:underline; }
        .top { display:flex; flex-wrap:wrap; align-items:center; gap:1rem; padding:.6rem 1rem; border-bottom:1px solid var(--line); background:var(--card); }
        .brand { font-weight:700; color:var(--fg); } .top nav { display:flex; gap:1rem; flex:1; } .top nav a.active { font-weight:700; text-decoration:underline; }
        .who { display:flex; gap:.75rem; align-items:center; color:var(--muted); }
        .page { max-width:1200px; margin:0 auto; padding:1rem; }
        h1 { font-size:1.5rem; margin:.5rem 0 1rem; } h2 { font-size:1.15rem; margin:1.5rem 0 .5rem; }
        .cards { display:grid; grid-template-columns:repeat(auto-fit,minmax(130px,1fr)); gap:.75rem; }
        .card { display:flex; flex-direction:column; padding:.75rem 1rem; border:1px solid var(--line); border-radius:8px; background:var(--card); color:var(--fg); }
        .card .n { font-size:1.8rem; font-weight:700; } .card.ok .n { color:var(--ok); } .card.bad .n { color:var(--bad); } .card.info .n { color:var(--info); } .card.muted .n { color:var(--muted); }
        table { width:100%; border-collapse:collapse; } th,td { text-align:left; padding:.4rem .6rem; border-bottom:1px solid var(--line); vertical-align:top; } th { color:var(--muted); font-weight:600; }
        .badge { padding:.1rem .5rem; border-radius:999px; border:1px solid currentColor; font-size:.85rem; white-space:nowrap; }
        .badge.ok,.bad { color:var(--bad); } .badge.ok { color:var(--ok); } .badge.info { color:var(--info); } .badge.muted,.note { color:var(--muted); }
        .err { color:var(--bad); max-width:320px; overflow-wrap:anywhere; } .actions { white-space:nowrap; }
        .banner { padding:.5rem .75rem; border:1px solid var(--line); border-radius:6px; background:var(--card); } .banner.bad { border-color:var(--bad); color:var(--bad); }
        button { font:inherit; padding:.25rem .7rem; border:1px solid var(--line); border-radius:6px; background:var(--card); color:var(--fg); cursor:pointer; }
        button:disabled { opacity:.5; cursor:default; } button.danger { color:var(--bad); border-color:var(--bad); }
        .filters { display:flex; flex-wrap:wrap; gap:1rem; align-items:end; margin-bottom:1rem; } .filters label { display:flex; flex-direction:column; gap:.2rem; color:var(--muted); }
        select,input[type=search] { font:inherit; padding:.3rem .5rem; border:1px solid var(--line); border-radius:6px; background:var(--bg); color:var(--fg); }
        .pager { display:flex; gap:1rem; align-items:center; margin-top:1rem; }
        dl { display:grid; grid-template-columns:max-content 1fr; gap:.3rem 1rem; } dt { color:var(--muted); } dd { margin:0; overflow-wrap:anywhere; }
        code { background:var(--card); padding:.05rem .3rem; border-radius:4px; }
        """;
}
