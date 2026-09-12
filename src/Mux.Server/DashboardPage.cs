namespace Mux.Server
{
    using System;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// Renders the self-contained single-page dashboard served at <c>/dashboard</c>. All CSS/JS/logo assets
    /// are inlined so the page has no external dependencies. The configured API key is injected so the page
    /// can call the (loopback) API on the user's behalf.
    /// </summary>
    public static class DashboardPage
    {
        /// <summary>
        /// Render the dashboard HTML with the API key, version, and logo assets injected.
        /// </summary>
        /// <param name="apiKey">The local API key to embed (may be null/empty in no-auth mode).</param>
        /// <param name="version">Product version.</param>
        /// <returns>A complete HTML document.</returns>
        public static string Render(string? apiKey, string version)
        {
            string logoDark = LoadLogoDataUri("Mux.Server.logo-white.png");
            string logoLight = LoadLogoDataUri("Mux.Server.logo-black.png");
            string logoGrey = LoadLogoDataUri("Mux.Server.logo-grey.png");

            return Template
                .Replace("__MUX_API_KEY__", JsString(apiKey ?? string.Empty))
                .Replace("__MUX_VERSION__", HtmlEscape(version))
                .Replace("__LOGO_DARK__", logoDark)
                .Replace("__LOGO_LIGHT__", logoLight)
                .Replace("__LOGO_GREY__", logoGrey);
        }

        private static string LoadLogoDataUri(string resourceName)
        {
            try
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null) return string.Empty;
                using (stream)
                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string JsString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "\\u003c");
        }

        private static string HtmlEscape(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private const string Template = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>mux dashboard</title>
<link rel="icon" href="__LOGO_GREY__">
<style>
:root{
  --bg:#f6f7f8; --panel:#ffffff; --panel-2:#eef2f5; --line:#d7dde3;
  --text:#1f252b; --muted:#65717d; --accent:#167a5b; --accent-2:#0f6248;
  --danger:#c2413a; --sidebar:#20242a; --sidebar-text:#e7edf2; --sidebar-muted:#8b97a3;
  --radius:8px; font-family:Inter,ui-sans-serif,system-ui,-apple-system,Segoe UI,Roboto,sans-serif;
}
:root[data-theme="dark"]{
  --bg:#101418; --panel:#171d23; --panel-2:#202832; --line:#303a45;
  --text:#eef3f8; --muted:#9aa7b4; --accent:#39a37f; --accent-2:#2b8567;
  --danger:#f26b61; --sidebar:#0c0f13; --sidebar-text:#e7edf2; --sidebar-muted:#7d8894;
}
*{box-sizing:border-box}
html,body{height:100%;margin:0}
body{background:var(--bg);color:var(--text);font-size:14px;line-height:1.5}
.app{display:grid;grid-template-columns:236px minmax(0,1fr);height:100vh}
.sidebar{background:var(--sidebar);color:var(--sidebar-text);display:flex;flex-direction:column;overflow-y:auto}
.brand{display:flex;align-items:center;gap:10px;padding:16px 18px;font-weight:700;font-size:18px}
.brand img{width:26px;height:26px;border-radius:5px;background:#fff;padding:2px}
.nav-group{padding:4px 10px}
.nav-label{color:var(--sidebar-muted);font-size:11px;text-transform:uppercase;letter-spacing:.06em;padding:8px 10px 4px}
.nav-item{display:flex;align-items:center;gap:10px;padding:5px 12px;border-radius:6px;color:var(--sidebar-text);cursor:pointer;font-size:14px;user-select:none}
.nav-item:hover{background:rgba(255,255,255,.06)}
.nav-item.active{background:var(--accent);color:#fff}
.sidebar-foot{margin-top:auto;padding:12px 18px;color:var(--sidebar-muted);font-size:12px}
.main{display:flex;flex-direction:column;min-width:0;height:100vh}
.topbar{height:56px;flex:none;display:flex;align-items:center;justify-content:space-between;padding:0 20px;border-bottom:1px solid var(--line);background:var(--panel)}
.topbar h1{font-size:16px;margin:0;font-weight:600}
.topbar .right{display:flex;align-items:center;gap:14px;color:var(--muted);font-size:13px}
.iconbtn{background:none;border:1px solid var(--line);color:var(--text);border-radius:6px;padding:6px 10px;cursor:pointer;font-size:13px}
.iconbtn:hover{border-color:var(--accent);color:var(--accent)}
.iconbtn.icononly{display:inline-flex;align-items:center;justify-content:center;width:34px;height:34px;padding:0}
.iconbtn.icononly svg{display:block}
.view{flex:1;min-height:0;overflow:auto;display:none}
.view.active{display:flex;flex-direction:column}
/* chat */
.chatwrap{flex:1;min-height:0;display:grid;grid-template-columns:248px minmax(0,1fr)}
.convos{display:flex;flex-direction:column;min-height:0;border-right:1px solid var(--line);background:var(--panel)}
.convos-head{height:64px;box-sizing:border-box;padding:0 12px;display:flex;align-items:center;gap:8px;border-bottom:1px solid var(--line)}
.convos-head .btn{flex:1;height:36px;display:inline-flex;align-items:center;justify-content:center;padding:0 10px;box-sizing:border-box}
.convos-head .btn.icon{flex:0 0 auto;width:40px}
.convo-list{flex:1;overflow-y:auto;padding:6px}
.convo-empty{color:var(--muted);font-size:12px;padding:14px 10px;text-align:center}
.convo-item{display:flex;align-items:center;gap:6px;padding:8px 10px;border-radius:6px;cursor:pointer;color:var(--text)}
.convo-item:hover{background:var(--hover,rgba(127,127,127,.10))}
.convo-item.active{background:var(--accent);color:#fff}
.convo-item .ct{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px}
.convo-item .convo-menu{display:none;background:none;border:none;cursor:pointer;color:inherit;font-size:16px;line-height:1;padding:2px 6px;border-radius:4px;flex:none}
.convo-item:hover .convo-menu,.convo-item.active .convo-menu{display:inline-flex}
.convo-item .convo-menu:hover{background:rgba(127,127,127,.25)}
.pickhead{display:flex;gap:8px;margin-bottom:10px}
.picklist{max-height:min(50vh,360px);overflow-y:auto;display:flex;flex-direction:column;gap:2px}
.pickrow{display:flex;align-items:center;gap:8px;padding:6px 8px;border-radius:6px;cursor:pointer}
.pickrow:hover{background:rgba(127,127,127,.12)}
.pickrow span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.chat-title{color:var(--muted);font-size:13px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:40vw}
@media(max-width:820px){.chatwrap{grid-template-columns:1fr}.convos{display:none}}
.chat{display:grid;grid-template-rows:auto 1fr auto;height:100%;min-height:0}
.chat-toolbar{height:64px;box-sizing:border-box;display:flex;align-items:center;gap:12px;padding:0 20px;border-bottom:1px solid var(--line);background:var(--panel)}
.chat-toolbar label{font-size:12px;color:var(--muted);margin-right:6px}
select,input,textarea{background:var(--panel);color:var(--text);border:1px solid var(--line);border-radius:6px;padding:8px 10px;font-size:14px;font-family:inherit}
select:focus,input:focus,textarea:focus{outline:none;border-color:var(--accent)}
.grow{flex:1}
.btn{background:var(--accent);color:#fff;border:none;border-radius:6px;padding:8px 14px;cursor:pointer;font-size:14px;font-weight:500}
.btn:hover{background:var(--accent-2)}
.btn.secondary{background:transparent;color:var(--text);border:1px solid var(--line)}
.messages{overflow-y:auto;padding:24px max(20px,7vw);display:flex;flex-direction:column;gap:16px}
.msg{display:flex;flex-direction:column}
.msg.user{align-items:flex-end}
.bubble{max-width:min(760px,100%);padding:12px 15px;border-radius:8px;background:var(--panel-2);white-space:normal;word-wrap:break-word}
.msg.assistant .bubble{position:relative}
.msgcopy{position:absolute;bottom:6px;right:6px;background:var(--panel);border:1px solid var(--line);border-radius:5px;color:var(--muted);cursor:pointer;font-size:12px;line-height:1;padding:3px 6px;opacity:0;transition:opacity .12s}
.msg.assistant .bubble:hover .msgcopy{opacity:1}
.msgcopy:hover{color:var(--accent);border-color:var(--accent)}
.msgcopy.ok{opacity:1;color:var(--accent);border-color:var(--accent)}
.msg.user .bubble{background:color-mix(in srgb,var(--accent) 18%,var(--panel))}
.bubble pre{background:#2f343a;color:#f6f8fa;border:1px solid #4b5563;border-radius:6px;padding:12px;overflow:auto;font-size:13px}
.bubble code{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:13px}
.bubble :not(pre)>code{background:color-mix(in srgb,var(--text) 8%,transparent);border:1px solid var(--line);border-radius:5px;padding:1px 5px}
.bubble p{margin:.4em 0}
.bubble p:first-child{margin-top:0}.bubble p:last-child{margin-bottom:0}
.bubble h1,.bubble h2,.bubble h3,.bubble h4,.bubble h5,.bubble h6{margin:.6em 0 .3em;line-height:1.25}
.bubble h1{font-size:1.35em}.bubble h2{font-size:1.2em}.bubble h3{font-size:1.08em}
.bubble ul,.bubble ol{margin:.4em 0;padding-left:1.5em}
.bubble li{margin:.15em 0}
.bubble li>ul,.bubble li>ol{margin:.15em 0}
.bubble blockquote{margin:.5em 0;padding:.1em .9em;border-left:3px solid var(--accent);color:var(--muted)}
.bubble a{color:var(--accent);text-decoration:underline}
.bubble em{font-style:italic}
.bubble strong{font-weight:600}
.bubble>*:first-child{margin-top:0}.bubble>*:last-child{margin-bottom:0}
/* per-turn stats info affordance */
.meta{display:flex;align-items:center;gap:8px}
.statinfo{position:relative;display:inline-flex;align-items:center;justify-content:center;width:15px;height:15px;border-radius:50%;border:1px solid var(--line);color:var(--muted);font-size:10px;font-style:normal;font-weight:700;cursor:default;user-select:none}
.statinfo:hover{border-color:var(--accent);color:var(--accent)}
.stattip{position:absolute;bottom:130%;left:0;z-index:40;min-width:200px;background:var(--panel);color:var(--text);border:1px solid var(--line);border-radius:8px;box-shadow:0 6px 24px rgba(0,0,0,.18);padding:10px 12px;font-size:12px;font-weight:400;opacity:0;pointer-events:none;transform:translateY(4px);transition:.15s;white-space:nowrap}
.statinfo:hover .stattip{opacity:1;transform:none}
.stattip .r{display:flex;justify-content:space-between;gap:18px;padding:2px 0}
.stattip .r span:first-child{color:var(--muted)}
.stattip .r span:last-child{font-variant-numeric:tabular-nums}
.think{color:var(--muted);font-size:12px;line-height:1.5;white-space:pre-wrap;border-left:2px solid var(--border);padding:2px 0 2px 8px;margin:0 0 8px 0;max-height:240px;overflow:auto}
.model-status{font-size:12px;color:var(--muted)}
.model-status.ready{color:var(--accent)}
.model-status.warn{color:#bf8700}
.model-status.err{color:#e5534b}
.thinking{display:flex;gap:5px;padding:4px 0}
.thinking span{width:7px;height:7px;border-radius:50%;background:var(--accent);animation:pulse 1.2s infinite}
.thinking span:nth-child(2){animation-delay:.2s}
.thinking span:nth-child(3){animation-delay:.4s}
@keyframes pulse{0%,60%,100%{opacity:.3}30%{opacity:1}}
.meta{font-size:11px;color:var(--muted);margin-top:4px}
.composer{border-top:1px solid var(--line);background:var(--panel);padding:14px 20px;display:flex;gap:10px;align-items:flex-end}
.composer textarea{flex:1;resize:none;max-height:180px;min-height:44px}
.composer .send{width:44px;height:44px;border-radius:50%;flex:none;display:flex;align-items:center;justify-content:center;font-size:18px;padding:0}
.disclaimer{font-size:11px;color:var(--muted);text-align:center;padding:0 20px 10px}
/* forms / cards */
.pad{padding:24px 28px;max-width:900px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:var(--radius);padding:18px 20px;margin-bottom:18px}
.card h2{font-size:15px;margin:0 0 4px}
.card .hint{color:var(--muted);font-size:12px;margin:0 0 14px}
.field{display:grid;grid-template-columns:230px 1fr;gap:12px;align-items:center;padding:7px 0}
.field label{font-size:13px}
.field .sub{color:var(--muted);font-size:11px}
.row{display:flex;gap:10px;align-items:center;flex-wrap:wrap}
.toast{position:fixed;bottom:20px;right:20px;background:var(--accent);color:#fff;padding:12px 18px;border-radius:8px;opacity:0;transform:translateY(8px);transition:.25s;pointer-events:none;z-index:50}
.toast.show{opacity:1;transform:none}
.toast.err{background:var(--danger)}
table.info{width:100%;border-collapse:collapse;font-size:13px}
table.info td{padding:6px 8px;border-bottom:1px solid var(--line)}
table.info td:first-child{color:var(--muted);width:180px}
.pill{display:inline-block;padding:2px 9px;border-radius:999px;font-size:12px;background:color-mix(in srgb,var(--accent) 18%,transparent);color:var(--accent)}
.empty{color:var(--muted);padding:40px;text-align:center}
/* full-width config pages */
.padw{padding:22px 26px}
.pagehead{display:flex;align-items:center;justify-content:space-between;margin-bottom:16px;gap:12px;flex-wrap:wrap}
.pagehead .desc{color:var(--muted);font-size:13px;flex:1;min-width:0}
/* data tables */
table.grid{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--line);border-radius:10px;overflow:hidden}
table.grid th{text-align:left;font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);font-weight:600;padding:11px 14px;border-bottom:1px solid var(--line);background:var(--panel-2)}
table.grid td{padding:11px 14px;border-bottom:1px solid var(--line);font-size:13px;vertical-align:middle}
table.grid tr:last-child td{border-bottom:none}
table.grid tbody tr:hover td{background:var(--panel-2)}
table.grid td.mono{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:12px;color:var(--muted)}
.rowactions{display:flex;gap:2px;justify-content:flex-end}
.iconact{background:none;border:1px solid transparent;border-radius:6px;padding:6px;cursor:pointer;color:var(--muted);display:inline-flex;align-items:center;justify-content:center;line-height:0}
.iconact:hover{color:var(--accent);border-color:var(--line);background:var(--panel-2)}
.iconact.danger:hover{color:var(--danger)}
.iconact svg{width:16px;height:16px;display:block;pointer-events:none}
.tag{display:inline-block;padding:1px 8px;border-radius:999px;font-size:11px;background:var(--panel-2);border:1px solid var(--line);color:var(--muted)}
.tag.on{background:color-mix(in srgb,var(--accent) 16%,transparent);border-color:transparent;color:var(--accent)}
.tag.off{opacity:.7}
/* modal */
.modal-overlay{position:fixed;inset:0;background:rgba(6,9,12,.55);display:none;align-items:center;justify-content:center;z-index:100;padding:22px}
.modal-overlay.show{display:flex}
.modal{background:var(--panel);border:1px solid var(--line);border-radius:14px;width:100%;max-width:560px;max-height:88vh;display:flex;flex-direction:column;box-shadow:0 24px 70px rgba(0,0,0,.4)}
.modal.wide{max-width:760px}
.modal-head{display:flex;align-items:center;justify-content:space-between;padding:16px 20px;border-bottom:1px solid var(--line)}
.modal-head h3{margin:0;font-size:15px;font-weight:600}
.modal-x{background:none;border:none;color:var(--muted);font-size:20px;cursor:pointer;line-height:1;padding:0 4px}
.modal-x:hover{color:var(--text)}
.modal-body{padding:16px 20px;overflow:auto}
.modal-body .field{grid-template-columns:190px 1fr}
.modal-foot{padding:14px 20px;border-top:1px solid var(--line);display:flex;justify-content:flex-end;gap:10px}
.btn.danger{background:var(--danger)}
@media(max-width:820px){.modal-body .field{grid-template-columns:1fr}}
@media(max-width:820px){.app{grid-template-columns:1fr}.sidebar{display:none}.field{grid-template-columns:1fr}}
/* ================= UX polish ================= */
:root{--shadow:0 1px 2px rgba(16,24,32,.05),0 3px 14px rgba(16,24,32,.05)}
:root[data-theme="dark"]{--shadow:0 1px 2px rgba(0,0,0,.3),0 6px 22px rgba(0,0,0,.35)}
body{-webkit-font-smoothing:antialiased;text-rendering:optimizeLegibility}
.brand{font-size:17px}.brand img{box-shadow:0 1px 3px rgba(0,0,0,.25)}
.nav-item{margin:0;transition:background .12s,color .12s;border:none;background:none;width:100%;font-family:inherit;font-size:14px;text-align:left}
.nav-item:focus-visible{outline:2px solid var(--accent);outline-offset:-2px}
.nav-item.active{background:rgba(255,255,255,.09);color:#fff;position:relative}
.nav-item.active::before{content:"";position:absolute;left:-10px;top:4px;bottom:4px;width:3px;border-radius:0 3px 3px 0;background:var(--accent)}
.topbar h1{font-size:17px}
.btn{border-radius:7px;box-shadow:var(--shadow);transition:background .12s,transform .05s,box-shadow .12s;font-weight:550}
.btn:active{transform:translateY(1px)}
.btn.secondary{box-shadow:none}
.btn.secondary:hover{border-color:var(--accent);color:var(--accent);background:color-mix(in srgb,var(--accent) 8%,transparent)}
select,input,textarea{transition:border-color .12s,box-shadow .12s}
select:focus,input:focus,textarea:focus{box-shadow:0 0 0 3px color-mix(in srgb,var(--accent) 22%,transparent)}
.padw{padding:22px 26px 40px}
.pagehead{align-items:flex-end;padding-bottom:14px;border-bottom:1px solid var(--line)}
.pagehead .desc{font-size:12.5px;line-height:1.55}
table.grid{box-shadow:var(--shadow)}
table.grid td{padding:12px 14px}
table.grid td:first-child{font-weight:550;color:var(--text)}
table.grid tbody tr{transition:background .1s}
.rowclick tbody tr{cursor:pointer}
.rowactions{cursor:default}
th.actcell,td.actcell{width:44px;text-align:right}
.rowmenu-btn{background:none;border:1px solid transparent;border-radius:7px;width:30px;height:30px;cursor:pointer;color:var(--muted);font-size:18px;line-height:1;display:inline-flex;align-items:center;justify-content:center}
.rowmenu-btn:hover,.rowmenu-btn.open{color:var(--text);border-color:var(--line);background:var(--panel-2)}
.ctxmenu{position:fixed;z-index:200;min-width:168px;max-width:260px;background:var(--panel);border:1px solid var(--line);border-radius:10px;box-shadow:0 14px 44px rgba(0,0,0,.3);padding:6px;display:flex;flex-direction:column}
.ctxitem{background:none;border:none;text-align:left;padding:8px 12px;border-radius:7px;cursor:pointer;color:var(--text);font-size:13px;white-space:nowrap}
.ctxitem:hover{background:var(--panel-2)}
.ctxitem.danger{color:var(--danger)}
.ctxitem.danger:hover{background:color-mix(in srgb,var(--danger) 12%,transparent)}
.ctxsep{height:1px;background:var(--line);margin:5px 6px}
.rowactions{gap:4px;align-items:center}
.iconact{width:30px;height:30px;padding:0;transition:.12s}
.iconact:hover{background:color-mix(in srgb,var(--accent) 10%,transparent)}
.iconact.danger:hover{background:color-mix(in srgb,var(--danger) 10%,transparent)}
.iconact.on{color:var(--accent)}
.minibtn{font-size:11px;font-weight:650;letter-spacing:.02em;padding:6px 9px;border-radius:6px;border:1px solid var(--line);background:var(--panel);color:var(--muted);cursor:pointer;line-height:1}
.minibtn:hover{border-color:var(--accent);color:var(--accent);background:color-mix(in srgb,var(--accent) 8%,transparent)}
.empty{padding:52px 20px;color:var(--muted);display:flex;flex-direction:column;align-items:center;gap:12px;text-align:center}
.empty .eicon{font-size:30px;opacity:.55;line-height:1}
.form-section{font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);font-weight:700;margin:18px 0 4px;padding-bottom:7px;border-bottom:1px solid var(--line)}
.form-section:first-child{margin-top:2px}
.modal-overlay{backdrop-filter:blur(3px)}
.modal-head h3{font-size:15px;letter-spacing:-.01em}
.modal-body .field{grid-template-columns:170px 1fr;align-items:center;padding:8px 0}
.modal-body .field.tall{align-items:start}
.modal-body .field.tall label{padding-top:8px}
.modal-body textarea{width:100%;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:12.5px}
.modal-body input[type=checkbox]{width:18px;height:18px;justify-self:start;accent-color:var(--accent)}
.chips{display:flex;flex-wrap:wrap;gap:7px}
.chip{display:inline-flex;align-items:center;gap:7px;border:1px solid var(--line);border-radius:999px;padding:6px 12px;font-size:12.5px;cursor:pointer;user-select:none;color:var(--muted);background:var(--panel);transition:.12s}
.chip:hover{border-color:var(--accent);color:var(--text)}
.chip input[type=checkbox]{width:15px;height:15px;margin:0;accent-color:var(--accent);cursor:pointer}
.chip:has(input:checked){background:color-mix(in srgb,var(--accent) 15%,transparent);border-color:transparent;color:var(--text);font-weight:600}
.tag{font-weight:600;padding:2px 9px}
.meta{display:flex;align-items:center;gap:8px;font-size:11px;color:var(--muted);margin-top:5px}
.msg .bubble{box-shadow:var(--shadow)}
.composer textarea{border-radius:10px}
.composer .send{box-shadow:var(--shadow)}
.toast{box-shadow:0 10px 34px rgba(0,0,0,.28);font-size:13px;font-weight:550;border-radius:9px}
.stattip{box-shadow:0 10px 30px rgba(0,0,0,.22)}
.modal.xl{max-width:1140px;min-height:70vh;max-height:92vh}
.modal.xl .modal-body{flex:1 1 auto}
.modal.xl .modal-body .field{grid-template-columns:190px 1fr}
/* topbar badges (health + version + uptime + host share one badge style) */
.topbar .right{gap:8px}
.badge{display:inline-flex;align-items:center;gap:6px;font-size:12px;font-weight:600;line-height:1;padding:5px 11px;border-radius:999px;background:var(--panel-2);border:1px solid var(--line);color:var(--muted);white-space:nowrap;font-family:inherit}
.badge:empty{display:none}
.badge.mono{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-weight:550}
.badge.status::before{content:"";width:7px;height:7px;border-radius:50%;background:var(--muted)}
.badge.status.ok{color:var(--accent)}.badge.status.ok::before{background:var(--accent)}
.badge.status.err{color:var(--danger)}.badge.status.err::before{background:var(--danger)}
/* sortable + filterable grid */
table.grid th.sortable{cursor:pointer;user-select:none;white-space:nowrap}
table.grid th.sortable:hover{color:var(--text)}
.sarrow{color:var(--accent);font-size:10px;display:inline-block;min-width:9px}
table.grid tr.frow th{padding:6px 8px;background:var(--panel-2);border-bottom:1px solid var(--line)}
.fbox{width:100%;padding:5px 8px;font-size:12px;border:1px solid var(--line);border-radius:6px;background:var(--panel);color:var(--text)}
.fbox:focus{outline:none;border-color:var(--accent);box-shadow:0 0 0 2px color-mix(in srgb,var(--accent) 20%,transparent)}
td.norows{padding:26px;text-align:center;color:var(--muted)}
/* horizontal scroll for tables on narrow screens; respect reduced motion */
@media(prefers-reduced-motion:reduce){*{animation:none!important;transition:none!important}}
/* home / overview */
.kpis{display:grid;grid-template-columns:repeat(auto-fill,minmax(158px,1fr));gap:12px;margin-bottom:20px}
.useg{display:inline-flex;border:1px solid var(--line);border-radius:7px;overflow:hidden}
.useg button{border:none;background:var(--panel);color:var(--text);padding:6px 13px;font-size:13px;cursor:pointer;font-family:inherit}
.useg button+button{border-left:1px solid var(--line)}
.useg button.active{background:var(--accent);color:#fff}
.usebar{display:flex;flex-wrap:wrap;gap:10px 12px;align-items:center;margin:0 0 18px}
.usebar>label{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.05em;margin-left:8px}
.usebar>label:first-child{margin-left:0}
.usebar select{min-width:132px}
.chartpanel{border:1px solid var(--line);border-radius:12px;background:var(--panel);overflow:hidden}
.ustabs{display:flex;gap:2px;border-bottom:1px solid var(--line);padding:8px 10px 0;flex-wrap:wrap;background:var(--panel-2)}
.ustabs button{border:none;background:none;color:var(--muted);padding:9px 16px;font-size:13.5px;cursor:pointer;font-family:inherit;border-radius:7px 7px 0 0;border-bottom:2px solid transparent;margin-bottom:-1px}
.ustabs button:hover{color:var(--text)}
.ustabs button.active{color:var(--text);border-bottom-color:var(--accent);font-weight:600}
.chartbody{padding:16px 18px 12px}
.uframe{display:grid;grid-template-columns:54px 1fr;grid-template-rows:382px auto;column-gap:8px}
.uy{grid-column:1;grid-row:1;display:flex;flex-direction:column;justify-content:space-between;align-items:flex-end;font-size:10.5px;color:var(--muted);font-family:monospace;line-height:1;padding:1px 0}
.uplot{grid-column:2;grid-row:1;position:relative;border-left:1px solid var(--line);border-bottom:1px solid var(--line)}
.uplot svg{position:absolute;inset:0;width:100%;height:100%;display:block}
.uplot .ugrid{stroke:var(--line);stroke-width:1;opacity:.45;vector-effect:non-scaling-stroke}
.uplot .ubar{opacity:.9}
.uplot .ubar:hover{opacity:1}
.uplot .utip{position:absolute;z-index:6;pointer-events:none;left:0;top:0;background:var(--panel-2);border:1px solid var(--line);border-radius:8px;padding:7px 9px;font-size:11.5px;line-height:1.5;color:var(--text);box-shadow:0 6px 22px rgba(0,0,0,.38);white-space:nowrap;opacity:0;transition:opacity .08s;max-width:75%}
.uplot .utip.on{opacity:1}
.uplot .utip .tt{color:var(--muted);font-weight:600;margin-bottom:4px}
.uplot .utip .tr{display:flex;align-items:center;justify-content:space-between;gap:14px}
.uplot .utip .tk{display:inline-flex;align-items:center;gap:6px}
.uplot .utip i{width:9px;height:9px;border-radius:2px;display:inline-block;flex:none}
.uplot .utip b{font-variant-numeric:tabular-nums;font-weight:700}
.uplot .utip .tr.tot{margin-top:3px;padding-top:3px;border-top:1px solid var(--line)}
.uplot .ucwick,.uplot .uccap{stroke-width:1.3;vector-effect:non-scaling-stroke;opacity:.55}
.uplot .ucbox{opacity:.55}
.uplot .ucbox:hover{opacity:.8}
.uplot .ucavg{stroke:var(--text);stroke-width:1.6;vector-effect:non-scaling-stroke;opacity:.85}
.uplot .ucp99{stroke:#dc2626;stroke-width:1.3;vector-effect:non-scaling-stroke;opacity:.9}
.chartlegend i.line{width:14px;height:3px;border-radius:2px}
.chartlegend i.avgl{background:var(--text);opacity:.85}
.ux{grid-column:2;grid-row:2;position:relative;height:15px;margin-top:6px}
.ux span{position:absolute;transform:translateX(-50%);font-size:10.5px;color:var(--muted);white-space:nowrap}
.chartempty{height:415px;display:flex;align-items:center;justify-content:center}
.chartlegend{display:flex;gap:16px;flex-wrap:wrap;font-size:11.5px;margin:13px 2px 0;color:var(--muted)}
.chartlegend span{display:inline-flex;align-items:center;gap:6px}
.chartlegend i{width:10px;height:10px;border-radius:2px;display:inline-block}
.chartnote{font-size:11px;color:var(--muted);margin:9px 2px 0}
.ubadge{display:inline-block;padding:1px 8px;border-radius:10px;font-size:11px;font-weight:600}
.ubadge.ok{background:color-mix(in srgb,var(--accent) 18%,transparent);color:var(--accent)}
.ubadge.err{background:color-mix(in srgb,var(--danger) 18%,transparent);color:var(--danger)}
.udetail{display:flex;flex-direction:column;gap:15px}
.udetail-hd{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-top:-2px}
.udetail-hd .mdl{font-family:monospace;font-size:15px;font-weight:600;color:var(--text)}
.udetail-hd .when{font-size:12px;color:var(--muted)}
.udetail-sec{border:1px solid var(--line);border-radius:9px;overflow:hidden}
.udetail-sec h5{margin:0;padding:8px 12px;font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);background:var(--panel-2);border-bottom:1px solid var(--line);font-weight:600}
.udetail-kv{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:1px;background:var(--line)}
.udetail-kv>div{background:var(--panel);padding:8px 12px}
.udetail-kv .k{font-size:11px;color:var(--muted);margin-bottom:3px}
.udetail-kv .v{font-family:monospace;font-size:13px;color:var(--text);word-break:break-word}
.kpi{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:14px 16px;cursor:pointer;text-align:left;box-shadow:var(--shadow);transition:transform .1s,border-color .12s;display:flex;flex-direction:column;gap:5px;font-family:inherit}
.kpi:hover{border-color:var(--accent);transform:translateY(-1px)}
.kpi .kv{font-size:24px;font-weight:700;font-variant-numeric:tabular-nums;line-height:1}
.kpi .kl{font-size:12px;color:var(--muted);display:flex;align-items:center;gap:6px}
.homegrid{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:16px}
@media(max-width:900px){.homegrid{grid-template-columns:1fr}}
.notice{border:1px solid var(--line);border-left-width:3px;border-radius:8px;padding:10px 14px;margin-bottom:10px;font-size:13px;display:flex;gap:9px;align-items:flex-start}
.notice .ni{font-size:14px;line-height:1.3}
.notice.info{border-left-color:var(--info)}.notice.info .ni{color:var(--info)}
.notice.warning{border-left-color:var(--warning)}.notice.warning .ni{color:var(--warning)}
.notice.success{border-left-color:var(--success)}.notice.success .ni{color:var(--success)}
.quickacts{display:flex;gap:10px;flex-wrap:wrap}
.recent-item{display:flex;align-items:center;gap:12px;padding:9px 2px;border-bottom:1px solid var(--line)}
.recent-item:last-child{border-bottom:none}
.recent-item .rt{flex:1;min-width:0}
.recent-item .rtitle{font-weight:550;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.recent-item .rmeta{font-size:12px;color:var(--muted)}
.dep .depname{font-size:16px;font-weight:600}
.dep .depmeta{color:var(--muted);font-size:13px;margin-top:2px}
/* semantic status tokens */
:root{--warning:#b5820a;--info:#2f6fb0;--success:var(--accent)}
:root[data-theme="dark"]{--warning:#e0a83a;--info:#5aa0e0}
/* loading spinner + error */
.spinner{width:22px;height:22px;border:2.5px solid var(--line);border-top-color:var(--accent);border-radius:50%;animation:spin .7s linear infinite}
@keyframes spin{to{transform:rotate(360deg)}}
.tablewrap{overflow-x:auto;border-radius:10px}
/* table toolbar + pagination + column picker */
.gridbar{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:10px;font-size:12.5px;color:var(--muted)}
.gridbar .count{font-variant-numeric:tabular-nums}
.gridbar .spacer{flex:1}
.gridbar select{padding:4px 6px;font-size:12px}
.pager{display:inline-flex;align-items:center;gap:3px}
.pgbtn{background:var(--panel);border:1px solid var(--line);border-radius:6px;min-width:28px;height:28px;cursor:pointer;color:var(--text);font-size:13px;display:inline-flex;align-items:center;justify-content:center}
.pgbtn:hover:not(:disabled){border-color:var(--accent);color:var(--accent)}
.pgbtn:disabled{opacity:.4;cursor:default}
.colbtn{background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:5px 10px;font-size:12px;cursor:pointer;color:var(--muted)}
.colbtn:hover{border-color:var(--accent);color:var(--accent)}
.ctxitem.chk::before{content:"✓";display:inline-block;width:14px;margin-right:7px;color:transparent}
.ctxitem.chk.on::before{color:var(--accent)}
/* copy control */
.copybtn{background:none;border:1px solid var(--line);border-radius:6px;padding:3px 8px;font-size:11px;font-weight:600;cursor:pointer;color:var(--muted);display:inline-flex;align-items:center;gap:5px;line-height:1}
.copybtn:hover{border-color:var(--accent);color:var(--accent)}
.copybtn.ok{color:var(--accent);border-color:var(--accent)}
.copybtn.icon{padding:3px 8px;font-size:13px;line-height:1}
.langsel{background:var(--panel-2);border:1px solid var(--line);border-radius:6px;padding:4px 8px;font-size:12px;font-weight:600;color:var(--muted);cursor:pointer;font-family:inherit;line-height:1;max-width:150px}
.langsel:hover{border-color:var(--accent);color:var(--text)}
.langsel:focus{outline:2px solid var(--accent);outline-offset:1px}
/* RTL (Arabic): mirror the accent bar, tip callouts, and menu anchoring */
[dir="rtl"] .nav-item.active::before{left:auto;right:-10px;border-radius:3px 0 0 3px}
[dir="rtl"] .stattip,[dir="rtl"] .tooltip{direction:rtl}
[dir="rtl"] .disclaimer,[dir="rtl"] .hint,[dir="rtl"] .desc{text-align:right}
/* mobile drawer navigation */
.hamburger{display:none;background:none;border:1px solid var(--line);border-radius:7px;width:34px;height:34px;cursor:pointer;color:var(--text);font-size:17px;align-items:center;justify-content:center}
.navscrim{display:none}
@media(max-width:820px){
  .hamburger{display:inline-flex;align-items:center;justify-content:center}
  .sidebar{display:flex;position:fixed;left:0;top:0;bottom:0;width:252px;z-index:120;transform:translateX(-100%);transition:transform .2s;box-shadow:0 0 40px rgba(0,0,0,.45)}
  .app.navopen .sidebar{transform:none}
  .navscrim{display:block;position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:110;opacity:0;pointer-events:none;transition:opacity .2s}
  .app.navopen .navscrim{opacity:1;pointer-events:auto}
  .padw{padding:16px 14px 40px}.pad{padding:16px 14px}
}
</style>
</head>
<body>
<div class="app" id="app">
  <div class="navscrim" id="navscrim"></div>
  <aside class="sidebar">
    <div class="brand"><img id="brandLogo" src="__LOGO_LIGHT__" alt="mux"><span>mux</span></div>
    <nav class="nav-group" aria-label="Home">
      <button class="nav-item active" data-view="home" title="Overview: a summary of your configuration, server health, and recent sessions">🏠 <span data-i18n="nav.home">Home</span></button>
      <button class="nav-item" data-view="chat" title="Chat with any configured model (a plain, tool-free conversation)">💬 <span data-i18n="nav.chat">Chat</span></button>
    </nav>
    <nav class="nav-group" aria-label="Configuration">
      <div class="nav-label" data-i18n="grp.config">Configuration</div>
      <button class="nav-item" data-view="endpoints" title="Manage model endpoints: the local and cloud LLMs mux can talk to">🔌 <span data-i18n="nav.endpoints">Endpoints</span></button>
      <button class="nav-item" data-view="mcp" title="Manage MCP tool servers that expose extra tools to the agent">🧩 <span data-i18n="nav.mcp">MCP Servers</span></button>
      <button class="nav-item" data-view="prompts" title="Manage system-prompt profiles that shape the agent's behavior">📝 <span data-i18n="nav.prompts">Prompts</span></button>
      <button class="nav-item" data-view="subagents" title="Manage named subagents the model can delegate scoped tasks to">🤖 <span data-i18n="nav.subagents">Subagents</span></button>
      <button class="nav-item" data-view="hooks" title="Manage event hooks that run out-of-process on session events">🪝 <span data-i18n="nav.hooks">Hooks</span></button>
      <button class="nav-item" data-view="commands" title="Manage custom /slash commands backed by external programs">⚡ <span data-i18n="nav.commands">Commands</span></button>
      <button class="nav-item" data-view="keybindings" title="Override the keyboard shortcuts for mux commands">⌨️ <span data-i18n="nav.keybindings">Keybindings</span></button>
      <button class="nav-item" data-view="skills" title="Manage skills: reusable Markdown-plus-code capabilities">🛠️ <span data-i18n="nav.skills">Skills</span></button>
    </nav>
    <nav class="nav-group" aria-label="Observability">
      <div class="nav-label" data-i18n="grp.observability">Observability</div>
      <button class="nav-item" data-view="usage" title="Usage analytics: token spend, cost, latency, and time-to-first-token over time">📊 <span data-i18n="nav.usage">Usage</span></button>
      <button class="nav-item" data-view="pricing" title="Edit per-model pricing used to derive usage cost">💲 <span data-i18n="nav.pricing">Pricing</span></button>
    </nav>
    <nav class="nav-group" aria-label="System">
      <div class="nav-label" data-i18n="grp.system">System</div>
      <button class="nav-item" data-view="sessions" title="Browse, preview, export, and delete saved chat sessions">🗂️ <span data-i18n="nav.sessions">Sessions</span></button>
      <button class="nav-item" data-view="settings" title="Edit global mux settings and the REST server configuration">⚙️ <span data-i18n="nav.settings">Settings</span></button>
    </nav>
    <div class="sidebar-foot">mux v__MUX_VERSION__</div>
  </aside>
  <main class="main">
    <header class="topbar">
      <button class="hamburger" id="hamburger" title="Show navigation" aria-label="Show navigation">☰</button>
      <h1 id="viewTitle">Chat</h1>
      <div class="right">
        <span class="badge status" id="statusPill" title="Live health of the mux REST server, polled every few seconds">…</span>
        <span class="badge" id="badgeVersion" title="The version of mux running this server"></span>
        <span class="badge" id="badgeUptime" title="How long this server process has been running"></span>
        <span class="badge mono" id="serverUrl" title="The host and port this dashboard is served from"></span>
        <button class="copybtn icon" id="copyUrlBtn" title="Copy the server URL to the clipboard">⧉</button>
        <select class="langsel" id="langSel" aria-label="Language" title="Choose the dashboard display language">
          <option value="en">English</option>
          <option value="es">Español</option>
          <option value="pt">Português</option>
          <option value="fr">Français</option>
          <option value="it">Italiano</option>
          <option value="de">Deutsch</option>
          <option value="zh">中文</option>
          <option value="ar">العربية</option>
          <option value="ru">Русский</option>
          <option value="ms">Bahasa Melayu</option>
          <option value="hi">हिन्दी</option>
        </select>
        <a class="iconbtn icononly" href="https://github.com/jchristn/Mux" target="_blank" rel="noopener" title="GitHub" aria-label="GitHub">
          <svg viewBox="0 0 16 16" width="16" height="16" fill="currentColor" aria-hidden="true"><path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0016 8c0-4.42-3.58-8-8-8z"/></svg>
        </a>
        <a class="iconbtn icononly" href="https://discord.gg/tRAN8HgvK5" target="_blank" rel="noopener" title="Join the mux Discord" aria-label="Join the mux Discord">
          <svg viewBox="0 0 16 16" width="16" height="16" fill="currentColor" aria-hidden="true"><path d="M13.545 2.907a13.23 13.23 0 0 0-3.257-1.011.05.05 0 0 0-.052.025c-.141.25-.297.577-.406.833a12.19 12.19 0 0 0-3.658 0 8.26 8.26 0 0 0-.412-.833.05.05 0 0 0-.052-.025c-1.125.194-2.22.534-3.257 1.011a.04.04 0 0 0-.021.018C.356 6.024-.213 9.047.066 12.032c.001.014.01.028.021.037a13.28 13.28 0 0 0 3.995 2.02.05.05 0 0 0 .056-.019c.308-.42.582-.863.818-1.329a.05.05 0 0 0-.028-.069 8.75 8.75 0 0 1-1.248-.595.05.05 0 0 1-.005-.084c.084-.063.168-.129.248-.195a.05.05 0 0 1 .051-.007c2.619 1.196 5.454 1.196 8.041 0a.05.05 0 0 1 .053.006c.08.066.164.133.248.196a.05.05 0 0 1-.004.084c-.399.233-.813.43-1.249.594a.05.05 0 0 0-.027.07c.24.465.515.909.817 1.329a.05.05 0 0 0 .056.019 13.24 13.24 0 0 0 4.001-2.02.05.05 0 0 0 .021-.037c.334-3.451-.559-6.449-2.366-9.107a.04.04 0 0 0-.02-.018ZM5.347 10.215c-.789 0-1.438-.724-1.438-1.612 0-.889.637-1.613 1.438-1.613.807 0 1.45.73 1.438 1.613 0 .888-.637 1.612-1.438 1.612Zm5.316 0c-.788 0-1.438-.724-1.438-1.612 0-.889.637-1.613 1.438-1.613.807 0 1.451.73 1.438 1.613 0 .888-.631 1.612-1.438 1.612Z"/></svg>
        </a>
        <button class="iconbtn icononly" id="themeBtn" title="Toggle light/dark" aria-label="Toggle theme"><span id="themeIcon">🌙</span></button>
      </div>
    </header>

    <!-- Home --><div class="view active" id="view-home"><div class="padw">
      <div id="home_notices"></div>
      <div class="kpis" id="home_kpis"></div>
      <div class="homegrid">
        <div class="card"><h2 data-i18n="home.default">Default endpoint</h2><p class="hint" data-i18n="home.defaultHint">The model used when none is specified.</p><div id="home_default" class="dep"></div></div>
        <div class="card"><h2 data-i18n="home.env">Environment</h2><p class="hint" data-i18n="home.envHint">This server and its active configuration.</p><table class="info" id="home_env"><tbody></tbody></table></div>
      </div>
      <div class="card"><div class="row" style="justify-content:space-between;align-items:center"><h2 style="margin:0" data-i18n="home.recent">Recent sessions</h2><button class="btn secondary" data-goto="sessions" title="Go to the Sessions page" data-i18n="act.allsessions">All sessions</button></div><div id="home_recent" style="margin-top:8px"></div></div>
      <div class="card"><h2 data-i18n="home.quick">Quick actions</h2><div class="quickacts" id="home_quick"></div></div>
    </div></div>

    <!-- Chat -->
    <div class="view" id="view-chat">
      <div class="chatwrap">
        <aside class="convos">
          <div class="convos-head">
            <button class="btn" id="newChatBtn" title="Start a fresh conversation">+ New</button>
            <button class="btn secondary" id="delMultiBtn" title="Delete multiple conversations">- Delete</button>
            <button class="btn secondary icon" id="refreshConvosBtn" title="Refresh the conversation list">⟳</button>
          </div>
          <div class="convo-list" id="convoList"></div>
        </aside>
        <div class="chat">
          <div class="chat-toolbar">
            <span><label title="The model endpoint this chat runs against" data-i18n="chat.endpoint">Endpoint</label></span>
            <select id="endpointSelect" style="min-width:260px" title="Choose which configured model endpoint answers your messages"></select>
            <span id="modelStatus" class="model-status" title="Whether the selected model is loaded and ready"></span>
            <span class="grow"></span>
            <span id="chatTitle" class="chat-title" title="The current conversation"></span>
          </div>
          <div class="messages" id="messages">
            <div class="empty" id="chatEmpty" data-i18n="chat.empty">Pick an endpoint and start chatting with your model.</div>
          </div>
          <div>
            <div class="composer">
              <textarea id="composer" rows="1" placeholder="Message your model… (Enter to send, Shift+Enter for newline)" title="Type a message. Enter sends it; Shift+Enter inserts a newline."></textarea>
              <button class="btn send" id="sendBtn" title="Send this message to the model">➤</button>
            </div>
            <div class="disclaimer" data-i18n="chat.disclaimer">mux is using the specified model.  Verify important results.</div>
          </div>
        </div>
      </div>
    </div>

    <!-- Endpoints --><div class="view" id="view-endpoints"><div class="padw">
      <div class="pagehead"><div class="desc">Model endpoints in <code>endpoints.json</code>. Secrets are never shown; leave a secret blank to keep it.</div>
        <div class="row"><button class="btn secondary" id="endpoints_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button><button class="btn" id="endpoints_add" title="Create a new endpoint">+ <span data-i18n="add.endpoint">Add endpoint</span></button></div></div>
      <div id="endpoints_list"></div></div></div>

    <!-- MCP --><div class="view" id="view-mcp"><div class="padw">
      <div class="pagehead"><div class="desc">MCP tool servers in <code>mcp-servers.json</code>. Auth secrets are never shown; leave blank to keep.</div>
        <div class="row"><button class="btn secondary" id="mcp_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button><button class="btn" id="mcp_add" title="Create a new server">+ <span data-i18n="add.server">Add server</span></button></div></div>
      <div id="mcp_list"></div></div></div>

    <!-- Prompts --><div class="view" id="view-prompts"><div class="padw">
      <div class="pagehead"><div class="desc">Prompt profiles in <code>prompts.json</code>. A blank system prompt inherits the built-in default.</div>
        <div class="row"><button class="btn secondary" id="prompts_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button><button class="btn" id="prompts_add" title="Create a new profile">+ <span data-i18n="add.profile">Add profile</span></button></div></div>
      <div id="prompts_list"></div></div></div>

    <!-- Subagents --><div class="view" id="view-subagents"><div class="padw">
      <div class="pagehead"><div class="desc">Subagents in <code>subagents.json</code> the model can delegate to via <code>spawn_subagent</code>.</div>
        <div class="row"><button class="btn secondary" id="subagents_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button><button class="btn" id="subagents_add" title="Create a new subagent">+ <span data-i18n="add.subagent">Add subagent</span></button></div></div>
      <div id="subagents_list"></div></div></div>

    <!-- Hooks --><div class="view" id="view-hooks"><div class="padw">
      <div class="pagehead"><div class="desc">Event hooks in <code>hooks.json</code>, run out-of-process on lifecycle events (session start, prompt submit, session end).</div>
        <div class="row"><button class="btn secondary" id="hooks_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button><button class="btn" id="hooks_add" title="Create a new hook">+ <span data-i18n="add.hook">Add hook</span></button></div></div>
      <div id="hooks_list"></div></div></div>

    <!-- Commands --><div class="view" id="view-commands"><div class="padw">
      <div class="pagehead"><div class="desc">Custom slash commands in <code>hooks.json</code>, surfaced as <code>/name</code> in the interactive shell and run out-of-process.</div>
        <div class="row"><button class="btn secondary" id="commands_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button><button class="btn" id="cmds_add" title="Create a new command">+ <span data-i18n="add.command">Add command</span></button></div></div>
      <div id="cmds_list"></div></div></div>

    <!-- Keybindings --><div class="view" id="view-keybindings"><div class="padw">
      <div class="pagehead"><div class="desc">Command key-chord overrides in <code>keybindings.json</code>. Blank chord unbinds the command.</div>
        <div class="row"><button class="btn secondary" id="keybindings_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button><button class="btn" id="keybindings_add" title="Create a new binding">+ <span data-i18n="add.binding">Add binding</span></button></div></div>
      <div id="keybindings_list"></div></div></div>

    <!-- Skills --><div class="view" id="view-skills"><div class="padw">
      <div class="pagehead"><div class="desc">Skills under the skills directory — versioned Markdown-plus-code capabilities. Create, edit, enable/disable, view, or delete them.</div>
        <div class="row"><button class="btn secondary" id="skills_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button><button class="btn" id="skills_add" title="Create a new skill">+ <span data-i18n="add.skill">Add skill</span></button></div></div>
      <div id="skills_list"></div></div></div>

    <!-- Sessions --><div class="view" id="view-sessions"><div class="padw">
      <div class="pagehead"><div class="desc">Saved sessions under <code>~/.mux/sessions</code>. Export to HTML/Markdown or delete.</div>
        <div class="row"><button class="btn secondary" id="sessions_reload" title="Reload this list from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button></div></div>
      <div id="sessions_list"></div></div></div>

    <!-- Settings -->
    <!-- Usage --><div class="view" id="view-usage"><div class="padw">
      <div class="pagehead"><div class="desc">Token usage, cost, latency, and time-to-first-token across your model calls. Reads the shared usage database (<code>~/.mux/usage.db</code>) written by every mux instance.</div>
        <div class="row"><button class="btn secondary" id="usage_refresh" title="Reload usage data for the current window">Refresh</button></div></div>
      <div class="usebar">
        <label>Range</label>
        <div class="useg" id="us_range">
          <button data-range="hour" title="Last hour">Hour</button>
          <button data-range="day" class="active" title="Last 24 hours">Day</button>
          <button data-range="week" title="Last 7 days">Week</button>
          <button data-range="month" title="Last 30 days">Month</button>
        </div>
        <label>Endpoint</label><select id="us_endpoint" title="Filter to one endpoint"><option value="">All endpoints</option></select>
        <label>Model</label><select id="us_model" title="Filter to one model"><option value="">All models</option></select>
      </div>
      <div id="us_disabled" style="display:none" class="empty"><div class="eicon">📊</div><div>Usage telemetry is disabled or empty. Run a turn in the CLI, or enable telemetry in settings.</div></div>
      <div class="kpis" id="us_kpis"></div>
      <div class="chartpanel">
        <div class="ustabs" id="us_tabs">
          <button data-tab="tokens" class="active">Tokens</button>
          <button data-tab="cost">Cost</button>
          <button data-tab="latency">Latency</button>
          <button data-tab="ttft">TTFT</button>
          <button data-tab="stream">Streaming</button>
          <button data-tab="throughput">Throughput</button>
        </div>
        <div class="chartbody" id="us_chart"></div>
      </div>
      <div class="pagehead" style="margin-top:28px"><div class="desc">Per-call history for the selected window. Click a row to inspect it; sort, filter, and choose columns from the table controls.</div></div>
      <div id="usage_history_list"></div>
    </div></div>

    <!-- Pricing --><div class="view" id="view-pricing"><div class="padw">
      <div class="pagehead"><div class="desc">Per-model rates in <code>pricing.json</code>, in US dollars per million tokens. Cost is derived from these at read time, so correcting a rate re-values history. Unknown models cost nothing until you add a rate.</div>
        <div class="row"><button class="btn secondary" id="pricing_reload" title="Reload pricing from disk, discarding unsaved changes">Reload</button><button class="btn" id="pricing_add" title="Add a model rate">+ <span>Add model</span></button></div></div>
      <div id="pricing_list"></div>
    </div></div>

    <div class="view" id="view-settings">
      <div class="pad">
        <div class="card">
          <h2 data-i18n="set.agent">Agent</h2>
          <p class="hint">Run limits and approval behavior for the agent loop.</p>
          <div class="field"><label>Default approval policy</label>
            <select id="s_defaultApprovalPolicy" title="How tool calls are approved by default: ask (prompt for mutating tools), auto (approve all), or deny (block all)."><option>ask</option><option>auto</option><option>deny</option></select></div>
          <div class="field"><label>Max agent iterations <span class="sub">(1–100)</span></label><input type="number" id="s_maxAgentIterations" title="Maximum number of model turns in a single run before mux stops (1-100)." min="1" max="100"></div>
          <div class="field"><label>Max concurrency <span class="sub">(1–32)</span></label><input type="number" id="s_maxConcurrency" title="Maximum number of jobs that may run at the same time (1-32)." min="1" max="32"></div>
          <div class="field"><label>Default enqueue behavior</label>
            <select id="s_defaultEnqueueBehavior" title="What happens when you submit a prompt while another turn is already running."><option>ask</option><option>run_now</option><option>queue_after</option><option>add_to_focused</option></select></div>
          <div class="field"><label>Tool timeout (ms)</label><input type="number" id="s_toolTimeoutMs" title="How long a built-in tool may run before timing out, in milliseconds."></div>
          <div class="field"><label>Process timeout (ms)</label><input type="number" id="s_processTimeoutMs" title="How long a spawned process (run_process) may run before timing out, in milliseconds."></div>
        </div>
        <div class="card">
          <h2 data-i18n="set.context">Context</h2>
          <p class="hint">Automatic compaction and context-pressure handling.</p>
          <div class="field"><label>Auto-compact</label><input type="checkbox" id="s_autoCompactEnabled" title="Automatically summarize old conversation history when the context window fills up."></div>
          <div class="field"><label>Compaction strategy</label>
            <select id="s_compactionStrategy" title="How history is compacted: 'summary' condenses it; 'trim' drops the oldest turns."><option>summary</option><option>trim</option></select></div>
          <div class="field"><label>Preserve turns <span class="sub">(1–10)</span></label><input type="number" id="s_compactionPreserveTurns" title="How many recent user turns to always keep uncompacted (1-10)." min="1" max="10"></div>
          <div class="field"><label>Warning threshold % <span class="sub">(50–95)</span></label><input type="number" id="s_contextWarningThresholdPercent" title="Warn about context pressure once usage passes this percent of the budget (50-95)." min="50" max="95"></div>
        </div>
        <div class="card">
          <h2 data-i18n="set.features">Features</h2>
          <div class="field"><label>Skills enabled</label><input type="checkbox" id="s_skillsEnabled" title="Whether user-authored skills are loaded and offered to the model."></div>
          <div class="field"><label>Task planning</label><input type="checkbox" id="s_taskPlanningEnabled" title="Whether the model may break work into a tracked task plan."></div>
          <div class="field"><label>Task parallelism</label><input type="checkbox" id="s_taskParallelismEnabled" title="Whether independent tasks may run as parallel jobs (advanced; off by default)."></div>
          <div class="field"><label>Ignore cert errors</label><input type="checkbox" id="s_ignoreCertErrors" title="Disable TLS certificate validation for mux network requests (for intercepting proxies)."></div>
          <div class="field"><label>Show boundary lines</label><input type="checkbox" id="s_showBoundaryLines" title="Draw thin boundary rules between regions in the interactive shell."></div>
        </div>
        <div class="card">
          <h2><span data-i18n="set.rest">REST server</span> <span class="pill" data-i18n="set.restReq">restart required</span></h2>
          <p class="hint">Host/port/SSL changes take effect on the next <code>mux serve</code>. The API key is masked; leave blank to keep the current key.</p>
          <div class="field"><label>Tray auto-start</label><input type="checkbox" id="s_rest_enabled" title="Whether the tray agent auto-starts this REST server. 'mux serve' starts it regardless."></div>
          <div class="field"><label>Hostname</label><input type="text" id="s_rest_hostname" title="The address the REST server binds to. 127.0.0.1 keeps it local-only. Restart required."></div>
          <div class="field"><label>Port</label><input type="number" id="s_rest_port" title="The TCP port the REST server listens on (1-65535). Restart required." min="1" max="65535"></div>
          <div class="field"><label>SSL</label><input type="checkbox" id="s_rest_ssl" title="Serve over HTTPS instead of HTTP. Restart required."></div>
          <div class="field"><label>CORS allow-origin</label><input type="text" id="s_rest_corsAllowOrigin" title="The Access-Control-Allow-Origin value the server returns."></div>
          <div class="field"><label>API key <span class="sub" id="apiKeyState"></span></label><input type="password" id="s_rest_apiKey" title="The bearer token required by the API. Never shown; leave blank to keep the current key." placeholder="(unchanged)"></div>
        </div>
        <div class="row"><button class="btn" id="saveSettingsBtn" title="Save these settings to settings.json. Run-affecting values apply next turn; REST changes need a restart." data-i18n="act.savesettings">Save settings</button><button class="btn secondary" id="reloadSettingsBtn" title="Reload settings from disk, discarding unsaved changes" data-i18n="act.reload">Reload</button></div>
      </div>
    </div>

  </main>
</div>
<div class="modal-overlay" id="modalOverlay"><div class="modal" id="modalBox">
  <div class="modal-head"><h3 id="modalTitle"></h3><button class="modal-x" id="modalX">×</button></div>
  <div class="modal-body" id="modalBody"></div>
  <div class="modal-foot" id="modalFoot"></div>
</div></div>
<div class="toast" id="toast"></div>
<script>
var API_KEY="__MUX_API_KEY__";
var I18N={
en:{
"nav.home":"Home","nav.chat":"Chat","nav.endpoints":"Endpoints","nav.mcp":"MCP Servers","nav.prompts":"Prompts","nav.subagents":"Subagents","nav.hooks":"Hooks","nav.commands":"Commands","nav.keybindings":"Keybindings","nav.skills":"Skills","nav.sessions":"Sessions","nav.settings":"Settings","grp.observability":"Observability","nav.usage":"Usage","nav.pricing":"Pricing",
"grp.config":"Configuration","grp.system":"System",
"vt.mcp":"MCP Servers","vt.hooks":"Event Hooks","vt.commands":"Custom Commands",
"act.reload":"Reload","act.save":"Save","act.cancel":"Cancel","act.close":"Close","act.delete":"Delete","act.edit":"Edit","act.duplicate":"Duplicate","act.viewjson":"View JSON","act.copyjson":"Copy JSON","act.copy":"Copy","act.retry":"Retry","act.view":"View","act.enable":"Enable","act.disable":"Disable","act.confirm":"Confirm","act.savesettings":"Save settings","act.allsessions":"All sessions",
"add.endpoint":"Add endpoint","add.server":"Add server","add.profile":"Add profile","add.subagent":"Add subagent","add.hook":"Add hook","add.command":"Add command","add.binding":"Add binding","add.skill":"Add skill","act.newchat":"New chat",
"tbl.rows":"Rows","tbl.columns":"Columns","tbl.filter":"Filter…","tbl.norows":"No rows match the current filters.","tbl.record":"record","tbl.records":"records","tbl.loading":"Loading…","tbl.failed":"Failed to load.","tbl.showing":"Showing {a}–{b} of {n}",
"toast.saved":"Saved","toast.deleted":"Deleted","toast.created":"Created","toast.copied":"Copied to clipboard","toast.nameReq":"Name is required","toast.exported":"Exported",
"modal.confirm":"Please confirm",
"empty.endpoints":"No endpoints configured yet.","empty.mcp":"No MCP servers configured yet.","empty.prompts":"No prompt profiles yet.","empty.subagents":"No subagents defined yet.","empty.hooks":"No event hooks configured.","empty.commands":"No custom commands configured.","empty.keybindings":"No keybinding overrides. Defaults are in effect.","empty.skills":"No skills found in the skills directory.","empty.sessions":"No saved sessions yet.",
"home.default":"Default endpoint","home.defaultHint":"The model used when none is specified.","home.env":"Environment","home.envHint":"This server and its active configuration.","home.recent":"Recent sessions","home.quick":"Quick actions","home.version":"Version","home.uptime":"Uptime","home.activePrompt":"Active prompt","home.auth":"Auth","home.configDir":"Configuration directory","home.authOn":"required (bearer token)","home.authOff":"disabled","home.noDefault":"No default endpoint is set.","home.manageEp":"Manage endpoints","home.msg":"msg",
"kpi.endpoints":"Endpoints","kpi.mcp":"MCP servers","kpi.prompts":"Prompts","kpi.subagents":"Subagents","kpi.skills":"Skills","kpi.hookscmds":"Hooks & commands","kpi.keybindings":"Keybindings","kpi.sessions":"Sessions","kpi.totalMsg":"Total messages",
"quick.newchat":"New chat","quick.addep":"Add endpoint","quick.sessions":"Sessions","quick.settings":"Settings",
"chat.endpoint":"Endpoint","chat.disclaimer":"mux is using the specified model. Verify important results.","chat.empty":"Pick an endpoint and start chatting with your model.",
"col.name":"Name","col.adapter":"Adapter","col.model":"Model","col.auth":"Auth","col.transport":"Transport","col.target":"Target","col.systemPrompt":"System prompt","col.description":"Description","col.tools":"Tools","col.event":"Event","col.command":"Command","col.blocking":"Blocking","col.commandId":"Command id","col.chord":"Chord","col.cmds":"Cmds","col.state":"State","col.title":"Title","col.updated":"Updated",
"tag.default":"default","tag.active":"active","tag.enabled":"enabled","tag.disabled":"disabled","tag.invalid":"invalid","tag.yes":"yes","tag.no":"no",
"set.agent":"Agent","set.context":"Context","set.features":"Features","set.rest":"REST server","set.restReq":"restart required",
"lang.label":"Language"
},
es:{
"nav.home":"Inicio","nav.chat":"Chat","nav.endpoints":"Endpoints","nav.mcp":"Servidores MCP","nav.prompts":"Prompts","nav.subagents":"Subagentes","nav.hooks":"Hooks","nav.commands":"Comandos","nav.keybindings":"Atajos de teclado","nav.skills":"Habilidades","nav.sessions":"Sesiones","nav.settings":"Ajustes",
"grp.config":"Configuración","grp.system":"Sistema",
"vt.mcp":"Servidores MCP","vt.hooks":"Hooks de eventos","vt.commands":"Comandos personalizados",
"act.reload":"Recargar","act.save":"Guardar","act.cancel":"Cancelar","act.close":"Cerrar","act.delete":"Eliminar","act.edit":"Editar","act.duplicate":"Duplicar","act.viewjson":"Ver JSON","act.copyjson":"Copiar JSON","act.copy":"Copiar","act.retry":"Reintentar","act.view":"Ver","act.enable":"Activar","act.disable":"Desactivar","act.confirm":"Confirmar","act.savesettings":"Guardar ajustes","act.allsessions":"Todas las sesiones",
"add.endpoint":"Añadir endpoint","add.server":"Añadir servidor","add.profile":"Añadir perfil","add.subagent":"Añadir subagente","add.hook":"Añadir hook","add.command":"Añadir comando","add.binding":"Añadir atajo","add.skill":"Añadir habilidad","act.newchat":"Nuevo chat",
"tbl.rows":"Filas","tbl.columns":"Columnas","tbl.filter":"Filtrar…","tbl.norows":"Ninguna fila coincide con los filtros.","tbl.record":"registro","tbl.records":"registros","tbl.loading":"Cargando…","tbl.failed":"Error al cargar.","tbl.showing":"Mostrando {a}–{b} de {n}",
"toast.saved":"Guardado","toast.deleted":"Eliminado","toast.created":"Creado","toast.copied":"Copiado al portapapeles","toast.nameReq":"El nombre es obligatorio","toast.exported":"Exportado",
"modal.confirm":"Confirme por favor",
"empty.endpoints":"Aún no hay endpoints configurados.","empty.mcp":"Aún no hay servidores MCP configurados.","empty.prompts":"Aún no hay perfiles de prompt.","empty.subagents":"Aún no hay subagentes definidos.","empty.hooks":"No hay hooks de eventos configurados.","empty.commands":"No hay comandos personalizados configurados.","empty.keybindings":"Sin atajos personalizados. Se usan los predeterminados.","empty.skills":"No se encontraron habilidades en el directorio.","empty.sessions":"Aún no hay sesiones guardadas.",
"home.default":"Endpoint predeterminado","home.defaultHint":"El modelo usado cuando no se especifica ninguno.","home.env":"Entorno","home.envHint":"Este servidor y su configuración activa.","home.recent":"Sesiones recientes","home.quick":"Acciones rápidas","home.version":"Versión","home.uptime":"Tiempo activo","home.activePrompt":"Prompt activo","home.auth":"Autenticación","home.configDir":"Directorio de configuración","home.authOn":"requerida (token bearer)","home.authOff":"desactivada","home.noDefault":"No hay endpoint predeterminado.","home.manageEp":"Gestionar endpoints","home.msg":"msj",
"kpi.endpoints":"Endpoints","kpi.mcp":"Servidores MCP","kpi.prompts":"Prompts","kpi.subagents":"Subagentes","kpi.skills":"Habilidades","kpi.hookscmds":"Hooks y comandos","kpi.keybindings":"Atajos de teclado","kpi.sessions":"Sesiones","kpi.totalMsg":"Mensajes totales",
"quick.newchat":"Nuevo chat","quick.addep":"Añadir endpoint","quick.sessions":"Sesiones","quick.settings":"Ajustes",
"chat.endpoint":"Endpoint","chat.disclaimer":"mux está usando el modelo especificado. Verifique los resultados importantes.","chat.empty":"Elige un endpoint y empieza a chatear con tu modelo.",
"col.name":"Nombre","col.adapter":"Adaptador","col.model":"Modelo","col.auth":"Autenticación","col.transport":"Transporte","col.target":"Destino","col.systemPrompt":"Prompt del sistema","col.description":"Descripción","col.tools":"Herramientas","col.event":"Evento","col.command":"Comando","col.blocking":"Bloqueante","col.commandId":"ID de comando","col.chord":"Combinación","col.cmds":"Cmds","col.state":"Estado","col.title":"Título","col.updated":"Actualizado",
"tag.default":"predeterminado","tag.active":"activo","tag.enabled":"activado","tag.disabled":"desactivado","tag.invalid":"no válido","tag.yes":"sí","tag.no":"no",
"set.agent":"Agente","set.context":"Contexto","set.features":"Funciones","set.rest":"Servidor REST","set.restReq":"requiere reinicio",
"lang.label":"Idioma"
},
pt:{
"nav.home":"Início","nav.chat":"Chat","nav.endpoints":"Endpoints","nav.mcp":"Servidores MCP","nav.prompts":"Prompts","nav.subagents":"Subagentes","nav.hooks":"Hooks","nav.commands":"Comandos","nav.keybindings":"Atalhos de teclado","nav.skills":"Habilidades","nav.sessions":"Sessões","nav.settings":"Configurações",
"grp.config":"Configuração","grp.system":"Sistema",
"vt.mcp":"Servidores MCP","vt.hooks":"Hooks de eventos","vt.commands":"Comandos personalizados",
"act.reload":"Recarregar","act.save":"Salvar","act.cancel":"Cancelar","act.close":"Fechar","act.delete":"Excluir","act.edit":"Editar","act.duplicate":"Duplicar","act.viewjson":"Ver JSON","act.copyjson":"Copiar JSON","act.copy":"Copiar","act.retry":"Tentar novamente","act.view":"Ver","act.enable":"Ativar","act.disable":"Desativar","act.confirm":"Confirmar","act.savesettings":"Salvar configurações","act.allsessions":"Todas as sessões",
"add.endpoint":"Adicionar endpoint","add.server":"Adicionar servidor","add.profile":"Adicionar perfil","add.subagent":"Adicionar subagente","add.hook":"Adicionar hook","add.command":"Adicionar comando","add.binding":"Adicionar atalho","add.skill":"Adicionar habilidade","act.newchat":"Novo chat",
"tbl.rows":"Linhas","tbl.columns":"Colunas","tbl.filter":"Filtrar…","tbl.norows":"Nenhuma linha corresponde aos filtros.","tbl.record":"registro","tbl.records":"registros","tbl.loading":"Carregando…","tbl.failed":"Falha ao carregar.","tbl.showing":"Mostrando {a}–{b} de {n}",
"toast.saved":"Salvo","toast.deleted":"Excluído","toast.created":"Criado","toast.copied":"Copiado para a área de transferência","toast.nameReq":"O nome é obrigatório","toast.exported":"Exportado",
"modal.confirm":"Confirme, por favor",
"empty.endpoints":"Nenhum endpoint configurado ainda.","empty.mcp":"Nenhum servidor MCP configurado ainda.","empty.prompts":"Nenhum perfil de prompt ainda.","empty.subagents":"Nenhum subagente definido ainda.","empty.hooks":"Nenhum hook de evento configurado.","empty.commands":"Nenhum comando personalizado configurado.","empty.keybindings":"Sem atalhos personalizados. Os padrões estão em vigor.","empty.skills":"Nenhuma habilidade encontrada no diretório.","empty.sessions":"Nenhuma sessão salva ainda.",
"home.default":"Endpoint padrão","home.defaultHint":"O modelo usado quando nenhum é especificado.","home.env":"Ambiente","home.envHint":"Este servidor e sua configuração ativa.","home.recent":"Sessões recentes","home.quick":"Ações rápidas","home.version":"Versão","home.uptime":"Tempo ativo","home.activePrompt":"Prompt ativo","home.auth":"Autenticação","home.configDir":"Diretório de configuração","home.authOn":"obrigatória (token bearer)","home.authOff":"desativada","home.noDefault":"Nenhum endpoint padrão definido.","home.manageEp":"Gerenciar endpoints","home.msg":"msg",
"kpi.endpoints":"Endpoints","kpi.mcp":"Servidores MCP","kpi.prompts":"Prompts","kpi.subagents":"Subagentes","kpi.skills":"Habilidades","kpi.hookscmds":"Hooks e comandos","kpi.keybindings":"Atalhos de teclado","kpi.sessions":"Sessões","kpi.totalMsg":"Total de mensagens",
"quick.newchat":"Novo chat","quick.addep":"Adicionar endpoint","quick.sessions":"Sessões","quick.settings":"Configurações",
"chat.endpoint":"Endpoint","chat.disclaimer":"O mux está usando o modelo especificado. Verifique os resultados importantes.","chat.empty":"Escolha um endpoint e comece a conversar com seu modelo.",
"col.name":"Nome","col.adapter":"Adaptador","col.model":"Modelo","col.auth":"Autenticação","col.transport":"Transporte","col.target":"Destino","col.systemPrompt":"Prompt do sistema","col.description":"Descrição","col.tools":"Ferramentas","col.event":"Evento","col.command":"Comando","col.blocking":"Bloqueante","col.commandId":"ID do comando","col.chord":"Combinação","col.cmds":"Cmds","col.state":"Estado","col.title":"Título","col.updated":"Atualizado",
"tag.default":"padrão","tag.active":"ativo","tag.enabled":"ativado","tag.disabled":"desativado","tag.invalid":"inválido","tag.yes":"sim","tag.no":"não",
"set.agent":"Agente","set.context":"Contexto","set.features":"Recursos","set.rest":"Servidor REST","set.restReq":"requer reinício",
"lang.label":"Idioma"
},
fr:{
"nav.home":"Accueil","nav.chat":"Chat","nav.endpoints":"Endpoints","nav.mcp":"Serveurs MCP","nav.prompts":"Prompts","nav.subagents":"Sous-agents","nav.hooks":"Hooks","nav.commands":"Commandes","nav.keybindings":"Raccourcis clavier","nav.skills":"Compétences","nav.sessions":"Sessions","nav.settings":"Paramètres",
"grp.config":"Configuration","grp.system":"Système",
"vt.mcp":"Serveurs MCP","vt.hooks":"Hooks d’événements","vt.commands":"Commandes personnalisées",
"act.reload":"Recharger","act.save":"Enregistrer","act.cancel":"Annuler","act.close":"Fermer","act.delete":"Supprimer","act.edit":"Modifier","act.duplicate":"Dupliquer","act.viewjson":"Voir le JSON","act.copyjson":"Copier le JSON","act.copy":"Copier","act.retry":"Réessayer","act.view":"Voir","act.enable":"Activer","act.disable":"Désactiver","act.confirm":"Confirmer","act.savesettings":"Enregistrer les paramètres","act.allsessions":"Toutes les sessions",
"add.endpoint":"Ajouter un endpoint","add.server":"Ajouter un serveur","add.profile":"Ajouter un profil","add.subagent":"Ajouter un sous-agent","add.hook":"Ajouter un hook","add.command":"Ajouter une commande","add.binding":"Ajouter un raccourci","add.skill":"Ajouter une compétence","act.newchat":"Nouveau chat",
"tbl.rows":"Lignes","tbl.columns":"Colonnes","tbl.filter":"Filtrer…","tbl.norows":"Aucune ligne ne correspond aux filtres.","tbl.record":"enregistrement","tbl.records":"enregistrements","tbl.loading":"Chargement…","tbl.failed":"Échec du chargement.","tbl.showing":"Affichage de {a} à {b} sur {n}",
"toast.saved":"Enregistré","toast.deleted":"Supprimé","toast.created":"Créé","toast.copied":"Copié dans le presse-papiers","toast.nameReq":"Le nom est obligatoire","toast.exported":"Exporté",
"modal.confirm":"Veuillez confirmer",
"empty.endpoints":"Aucun endpoint configuré.","empty.mcp":"Aucun serveur MCP configuré.","empty.prompts":"Aucun profil de prompt.","empty.subagents":"Aucun sous-agent défini.","empty.hooks":"Aucun hook d’événement configuré.","empty.commands":"Aucune commande personnalisée configurée.","empty.keybindings":"Aucun raccourci personnalisé. Les valeurs par défaut s’appliquent.","empty.skills":"Aucune compétence trouvée dans le répertoire.","empty.sessions":"Aucune session enregistrée.",
"home.default":"Endpoint par défaut","home.defaultHint":"Le modèle utilisé quand aucun n’est précisé.","home.env":"Environnement","home.envHint":"Ce serveur et sa configuration active.","home.recent":"Sessions récentes","home.quick":"Actions rapides","home.version":"Version","home.uptime":"Disponibilité","home.activePrompt":"Prompt actif","home.auth":"Authentification","home.configDir":"Répertoire de configuration","home.authOn":"requise (jeton bearer)","home.authOff":"désactivée","home.noDefault":"Aucun endpoint par défaut défini.","home.manageEp":"Gérer les endpoints","home.msg":"msg",
"kpi.endpoints":"Endpoints","kpi.mcp":"Serveurs MCP","kpi.prompts":"Prompts","kpi.subagents":"Sous-agents","kpi.skills":"Compétences","kpi.hookscmds":"Hooks et commandes","kpi.keybindings":"Raccourcis clavier","kpi.sessions":"Sessions","kpi.totalMsg":"Total des messages",
"quick.newchat":"Nouveau chat","quick.addep":"Ajouter un endpoint","quick.sessions":"Sessions","quick.settings":"Paramètres",
"chat.endpoint":"Endpoint","chat.disclaimer":"mux utilise le modèle spécifié. Vérifiez les résultats importants.","chat.empty":"Choisissez un endpoint et commencez à discuter avec votre modèle.",
"col.name":"Nom","col.adapter":"Adaptateur","col.model":"Modèle","col.auth":"Auth","col.transport":"Transport","col.target":"Cible","col.systemPrompt":"Prompt système","col.description":"Description","col.tools":"Outils","col.event":"Événement","col.command":"Commande","col.blocking":"Bloquant","col.commandId":"ID de commande","col.chord":"Combinaison","col.cmds":"Cmds","col.state":"État","col.title":"Titre","col.updated":"Mis à jour",
"tag.default":"par défaut","tag.active":"actif","tag.enabled":"activé","tag.disabled":"désactivé","tag.invalid":"non valide","tag.yes":"oui","tag.no":"non",
"set.agent":"Agent","set.context":"Contexte","set.features":"Fonctionnalités","set.rest":"Serveur REST","set.restReq":"redémarrage requis",
"lang.label":"Langue"
},
it:{
"nav.home":"Home","nav.chat":"Chat","nav.endpoints":"Endpoint","nav.mcp":"Server MCP","nav.prompts":"Prompt","nav.subagents":"Subagent","nav.hooks":"Hook","nav.commands":"Comandi","nav.keybindings":"Scorciatoie da tastiera","nav.skills":"Competenze","nav.sessions":"Sessioni","nav.settings":"Impostazioni",
"grp.config":"Configurazione","grp.system":"Sistema",
"vt.mcp":"Server MCP","vt.hooks":"Hook di eventi","vt.commands":"Comandi personalizzati",
"act.reload":"Ricarica","act.save":"Salva","act.cancel":"Annulla","act.close":"Chiudi","act.delete":"Elimina","act.edit":"Modifica","act.duplicate":"Duplica","act.viewjson":"Vedi JSON","act.copyjson":"Copia JSON","act.copy":"Copia","act.retry":"Riprova","act.view":"Vedi","act.enable":"Abilita","act.disable":"Disabilita","act.confirm":"Conferma","act.savesettings":"Salva impostazioni","act.allsessions":"Tutte le sessioni",
"add.endpoint":"Aggiungi endpoint","add.server":"Aggiungi server","add.profile":"Aggiungi profilo","add.subagent":"Aggiungi subagent","add.hook":"Aggiungi hook","add.command":"Aggiungi comando","add.binding":"Aggiungi scorciatoia","add.skill":"Aggiungi competenza","act.newchat":"Nuova chat",
"tbl.rows":"Righe","tbl.columns":"Colonne","tbl.filter":"Filtra…","tbl.norows":"Nessuna riga corrisponde ai filtri.","tbl.record":"record","tbl.records":"record","tbl.loading":"Caricamento…","tbl.failed":"Caricamento non riuscito.","tbl.showing":"Visualizzati {a}–{b} di {n}",
"toast.saved":"Salvato","toast.deleted":"Eliminato","toast.created":"Creato","toast.copied":"Copiato negli appunti","toast.nameReq":"Il nome è obbligatorio","toast.exported":"Esportato",
"modal.confirm":"Conferma",
"empty.endpoints":"Nessun endpoint configurato.","empty.mcp":"Nessun server MCP configurato.","empty.prompts":"Nessun profilo di prompt.","empty.subagents":"Nessun subagent definito.","empty.hooks":"Nessun hook di evento configurato.","empty.commands":"Nessun comando personalizzato configurato.","empty.keybindings":"Nessuna scorciatoia personalizzata. Valgono i valori predefiniti.","empty.skills":"Nessuna competenza trovata nella cartella.","empty.sessions":"Nessuna sessione salvata.",
"home.default":"Endpoint predefinito","home.defaultHint":"Il modello usato quando non ne viene specificato uno.","home.env":"Ambiente","home.envHint":"Questo server e la sua configurazione attiva.","home.recent":"Sessioni recenti","home.quick":"Azioni rapide","home.version":"Versione","home.uptime":"Tempo di attività","home.activePrompt":"Prompt attivo","home.auth":"Autenticazione","home.configDir":"Cartella di configurazione","home.authOn":"richiesta (token bearer)","home.authOff":"disabilitata","home.noDefault":"Nessun endpoint predefinito impostato.","home.manageEp":"Gestisci endpoint","home.msg":"msg",
"kpi.endpoints":"Endpoint","kpi.mcp":"Server MCP","kpi.prompts":"Prompt","kpi.subagents":"Subagent","kpi.skills":"Competenze","kpi.hookscmds":"Hook e comandi","kpi.keybindings":"Scorciatoie","kpi.sessions":"Sessioni","kpi.totalMsg":"Messaggi totali",
"quick.newchat":"Nuova chat","quick.addep":"Aggiungi endpoint","quick.sessions":"Sessioni","quick.settings":"Impostazioni",
"chat.endpoint":"Endpoint","chat.disclaimer":"mux sta usando il modello specificato. Verifica i risultati importanti.","chat.empty":"Scegli un endpoint e inizia a chattare con il tuo modello.",
"col.name":"Nome","col.adapter":"Adattatore","col.model":"Modello","col.auth":"Auth","col.transport":"Trasporto","col.target":"Destinazione","col.systemPrompt":"Prompt di sistema","col.description":"Descrizione","col.tools":"Strumenti","col.event":"Evento","col.command":"Comando","col.blocking":"Bloccante","col.commandId":"ID comando","col.chord":"Combinazione","col.cmds":"Cmd","col.state":"Stato","col.title":"Titolo","col.updated":"Aggiornato",
"tag.default":"predefinito","tag.active":"attivo","tag.enabled":"abilitato","tag.disabled":"disabilitato","tag.invalid":"non valido","tag.yes":"sì","tag.no":"no",
"set.agent":"Agente","set.context":"Contesto","set.features":"Funzioni","set.rest":"Server REST","set.restReq":"riavvio richiesto",
"lang.label":"Lingua"
},
de:{
"nav.home":"Start","nav.chat":"Chat","nav.endpoints":"Endpunkte","nav.mcp":"MCP-Server","nav.prompts":"Prompts","nav.subagents":"Subagenten","nav.hooks":"Hooks","nav.commands":"Befehle","nav.keybindings":"Tastenkürzel","nav.skills":"Fähigkeiten","nav.sessions":"Sitzungen","nav.settings":"Einstellungen",
"grp.config":"Konfiguration","grp.system":"System",
"vt.mcp":"MCP-Server","vt.hooks":"Ereignis-Hooks","vt.commands":"Benutzerdefinierte Befehle",
"act.reload":"Neu laden","act.save":"Speichern","act.cancel":"Abbrechen","act.close":"Schließen","act.delete":"Löschen","act.edit":"Bearbeiten","act.duplicate":"Duplizieren","act.viewjson":"JSON anzeigen","act.copyjson":"JSON kopieren","act.copy":"Kopieren","act.retry":"Wiederholen","act.view":"Ansehen","act.enable":"Aktivieren","act.disable":"Deaktivieren","act.confirm":"Bestätigen","act.savesettings":"Einstellungen speichern","act.allsessions":"Alle Sitzungen",
"add.endpoint":"Endpunkt hinzufügen","add.server":"Server hinzufügen","add.profile":"Profil hinzufügen","add.subagent":"Subagent hinzufügen","add.hook":"Hook hinzufügen","add.command":"Befehl hinzufügen","add.binding":"Tastenkürzel hinzufügen","add.skill":"Fähigkeit hinzufügen","act.newchat":"Neuer Chat",
"tbl.rows":"Zeilen","tbl.columns":"Spalten","tbl.filter":"Filtern…","tbl.norows":"Keine Zeile entspricht den Filtern.","tbl.record":"Eintrag","tbl.records":"Einträge","tbl.loading":"Wird geladen…","tbl.failed":"Laden fehlgeschlagen.","tbl.showing":"{a}–{b} von {n}",
"toast.saved":"Gespeichert","toast.deleted":"Gelöscht","toast.created":"Erstellt","toast.copied":"In die Zwischenablage kopiert","toast.nameReq":"Name ist erforderlich","toast.exported":"Exportiert",
"modal.confirm":"Bitte bestätigen",
"empty.endpoints":"Noch keine Endpunkte konfiguriert.","empty.mcp":"Noch keine MCP-Server konfiguriert.","empty.prompts":"Noch keine Prompt-Profile.","empty.subagents":"Noch keine Subagenten definiert.","empty.hooks":"Keine Ereignis-Hooks konfiguriert.","empty.commands":"Keine benutzerdefinierten Befehle konfiguriert.","empty.keybindings":"Keine eigenen Tastenkürzel. Standardwerte sind aktiv.","empty.skills":"Keine Fähigkeiten im Verzeichnis gefunden.","empty.sessions":"Noch keine gespeicherten Sitzungen.",
"home.default":"Standard-Endpunkt","home.defaultHint":"Das Modell, das verwendet wird, wenn keines angegeben ist.","home.env":"Umgebung","home.envHint":"Dieser Server und seine aktive Konfiguration.","home.recent":"Letzte Sitzungen","home.quick":"Schnellaktionen","home.version":"Version","home.uptime":"Laufzeit","home.activePrompt":"Aktiver Prompt","home.auth":"Authentifizierung","home.configDir":"Konfigurationsverzeichnis","home.authOn":"erforderlich (Bearer-Token)","home.authOff":"deaktiviert","home.noDefault":"Kein Standard-Endpunkt festgelegt.","home.manageEp":"Endpunkte verwalten","home.msg":"Nachr.",
"kpi.endpoints":"Endpunkte","kpi.mcp":"MCP-Server","kpi.prompts":"Prompts","kpi.subagents":"Subagenten","kpi.skills":"Fähigkeiten","kpi.hookscmds":"Hooks & Befehle","kpi.keybindings":"Tastenkürzel","kpi.sessions":"Sitzungen","kpi.totalMsg":"Nachrichten gesamt",
"quick.newchat":"Neuer Chat","quick.addep":"Endpunkt hinzufügen","quick.sessions":"Sitzungen","quick.settings":"Einstellungen",
"chat.endpoint":"Endpunkt","chat.disclaimer":"mux verwendet das angegebene Modell. Überprüfen Sie wichtige Ergebnisse.","chat.empty":"Wählen Sie einen Endpunkt und beginnen Sie den Chat mit Ihrem Modell.",
"col.name":"Name","col.adapter":"Adapter","col.model":"Modell","col.auth":"Auth","col.transport":"Transport","col.target":"Ziel","col.systemPrompt":"System-Prompt","col.description":"Beschreibung","col.tools":"Werkzeuge","col.event":"Ereignis","col.command":"Befehl","col.blocking":"Blockierend","col.commandId":"Befehls-ID","col.chord":"Tastenkombi","col.cmds":"Befehle","col.state":"Status","col.title":"Titel","col.updated":"Aktualisiert",
"tag.default":"Standard","tag.active":"aktiv","tag.enabled":"aktiviert","tag.disabled":"deaktiviert","tag.invalid":"ungültig","tag.yes":"ja","tag.no":"nein",
"set.agent":"Agent","set.context":"Kontext","set.features":"Funktionen","set.rest":"REST-Server","set.restReq":"Neustart erforderlich",
"lang.label":"Sprache"
},
zh:{
"nav.home":"主页","nav.chat":"聊天","nav.endpoints":"端点","nav.mcp":"MCP 服务器","nav.prompts":"提示词","nav.subagents":"子代理","nav.hooks":"钩子","nav.commands":"命令","nav.keybindings":"快捷键","nav.skills":"技能","nav.sessions":"会话","nav.settings":"设置",
"grp.config":"配置","grp.system":"系统",
"vt.mcp":"MCP 服务器","vt.hooks":"事件钩子","vt.commands":"自定义命令",
"act.reload":"重新加载","act.save":"保存","act.cancel":"取消","act.close":"关闭","act.delete":"删除","act.edit":"编辑","act.duplicate":"复制","act.viewjson":"查看 JSON","act.copyjson":"复制 JSON","act.copy":"复制","act.retry":"重试","act.view":"查看","act.enable":"启用","act.disable":"停用","act.confirm":"确认","act.savesettings":"保存设置","act.allsessions":"所有会话",
"add.endpoint":"添加端点","add.server":"添加服务器","add.profile":"添加配置","add.subagent":"添加子代理","add.hook":"添加钩子","add.command":"添加命令","add.binding":"添加快捷键","add.skill":"添加技能","act.newchat":"新建聊天",
"tbl.rows":"行数","tbl.columns":"列","tbl.filter":"筛选…","tbl.norows":"没有符合筛选条件的行。","tbl.record":"条","tbl.records":"条","tbl.loading":"加载中…","tbl.failed":"加载失败。","tbl.showing":"显示第 {a}–{b} 项，共 {n} 项",
"toast.saved":"已保存","toast.deleted":"已删除","toast.created":"已创建","toast.copied":"已复制到剪贴板","toast.nameReq":"名称为必填项","toast.exported":"已导出",
"modal.confirm":"请确认",
"empty.endpoints":"尚未配置端点。","empty.mcp":"尚未配置 MCP 服务器。","empty.prompts":"尚无提示词配置。","empty.subagents":"尚未定义子代理。","empty.hooks":"未配置事件钩子。","empty.commands":"未配置自定义命令。","empty.keybindings":"没有自定义快捷键，正在使用默认设置。","empty.skills":"技能目录中未找到技能。","empty.sessions":"尚无已保存的会话。",
"home.default":"默认端点","home.defaultHint":"未指定端点时使用的模型。","home.env":"环境","home.envHint":"此服务器及其当前配置。","home.recent":"最近会话","home.quick":"快捷操作","home.version":"版本","home.uptime":"运行时间","home.activePrompt":"当前提示词","home.auth":"认证","home.configDir":"配置目录","home.authOn":"需要（Bearer 令牌）","home.authOff":"已禁用","home.noDefault":"未设置默认端点。","home.manageEp":"管理端点","home.msg":"条消息",
"kpi.endpoints":"端点","kpi.mcp":"MCP 服务器","kpi.prompts":"提示词","kpi.subagents":"子代理","kpi.skills":"技能","kpi.hookscmds":"钩子和命令","kpi.keybindings":"快捷键","kpi.sessions":"会话","kpi.totalMsg":"消息总数",
"quick.newchat":"新建聊天","quick.addep":"添加端点","quick.sessions":"会话","quick.settings":"设置",
"chat.endpoint":"端点","chat.disclaimer":"mux 正在使用指定的模型。请核实重要结果。","chat.empty":"选择一个端点，开始与您的模型聊天。",
"col.name":"名称","col.adapter":"适配器","col.model":"模型","col.auth":"认证","col.transport":"传输","col.target":"目标","col.systemPrompt":"系统提示词","col.description":"描述","col.tools":"工具","col.event":"事件","col.command":"命令","col.blocking":"阻断","col.commandId":"命令 ID","col.chord":"组合键","col.cmds":"命令数","col.state":"状态","col.title":"标题","col.updated":"更新时间",
"tag.default":"默认","tag.active":"活动","tag.enabled":"已启用","tag.disabled":"已停用","tag.invalid":"无效","tag.yes":"是","tag.no":"否",
"set.agent":"代理","set.context":"上下文","set.features":"功能","set.rest":"REST 服务器","set.restReq":"需要重启",
"lang.label":"语言"
},
ar:{
"nav.home":"الرئيسية","nav.chat":"الدردشة","nav.endpoints":"نقاط النهاية","nav.mcp":"خوادم MCP","nav.prompts":"التوجيهات","nav.subagents":"الوكلاء الفرعيون","nav.hooks":"الخطافات","nav.commands":"الأوامر","nav.keybindings":"اختصارات المفاتيح","nav.skills":"المهارات","nav.sessions":"الجلسات","nav.settings":"الإعدادات",
"grp.config":"الإعداد","grp.system":"النظام",
"vt.mcp":"خوادم MCP","vt.hooks":"خطافات الأحداث","vt.commands":"أوامر مخصصة",
"act.reload":"إعادة تحميل","act.save":"حفظ","act.cancel":"إلغاء","act.close":"إغلاق","act.delete":"حذف","act.edit":"تعديل","act.duplicate":"تكرار","act.viewjson":"عرض JSON","act.copyjson":"نسخ JSON","act.copy":"نسخ","act.retry":"إعادة المحاولة","act.view":"عرض","act.enable":"تفعيل","act.disable":"تعطيل","act.confirm":"تأكيد","act.savesettings":"حفظ الإعدادات","act.allsessions":"كل الجلسات",
"add.endpoint":"إضافة نقطة نهاية","add.server":"إضافة خادم","add.profile":"إضافة ملف تعريف","add.subagent":"إضافة وكيل فرعي","add.hook":"إضافة خطاف","add.command":"إضافة أمر","add.binding":"إضافة اختصار","add.skill":"إضافة مهارة","act.newchat":"دردشة جديدة",
"tbl.rows":"الصفوف","tbl.columns":"الأعمدة","tbl.filter":"تصفية…","tbl.norows":"لا توجد صفوف مطابقة للمرشحات.","tbl.record":"سجل","tbl.records":"سجلات","tbl.loading":"جارٍ التحميل…","tbl.failed":"فشل التحميل.","tbl.showing":"عرض {a}–{b} من {n}",
"toast.saved":"تم الحفظ","toast.deleted":"تم الحذف","toast.created":"تم الإنشاء","toast.copied":"تم النسخ إلى الحافظة","toast.nameReq":"الاسم مطلوب","toast.exported":"تم التصدير",
"modal.confirm":"يرجى التأكيد",
"empty.endpoints":"لا توجد نقاط نهاية مُعدّة بعد.","empty.mcp":"لا توجد خوادم MCP مُعدّة بعد.","empty.prompts":"لا توجد ملفات توجيه بعد.","empty.subagents":"لم يتم تعريف وكلاء فرعيين بعد.","empty.hooks":"لا توجد خطافات أحداث مُعدّة.","empty.commands":"لا توجد أوامر مخصصة مُعدّة.","empty.keybindings":"لا اختصارات مخصصة. يتم استخدام الافتراضية.","empty.skills":"لم يتم العثور على مهارات في المجلد.","empty.sessions":"لا توجد جلسات محفوظة بعد.",
"home.default":"نقطة النهاية الافتراضية","home.defaultHint":"النموذج المُستخدم عند عدم تحديد أي نموذج.","home.env":"البيئة","home.envHint":"هذا الخادم وإعداده النشط.","home.recent":"الجلسات الأخيرة","home.quick":"إجراءات سريعة","home.version":"الإصدار","home.uptime":"مدة التشغيل","home.activePrompt":"التوجيه النشط","home.auth":"المصادقة","home.configDir":"مجلد الإعداد","home.authOn":"مطلوبة (رمز Bearer)","home.authOff":"معطّلة","home.noDefault":"لم يتم تعيين نقطة نهاية افتراضية.","home.manageEp":"إدارة نقاط النهاية","home.msg":"رسالة",
"kpi.endpoints":"نقاط النهاية","kpi.mcp":"خوادم MCP","kpi.prompts":"التوجيهات","kpi.subagents":"الوكلاء الفرعيون","kpi.skills":"المهارات","kpi.hookscmds":"الخطافات والأوامر","kpi.keybindings":"اختصارات المفاتيح","kpi.sessions":"الجلسات","kpi.totalMsg":"إجمالي الرسائل",
"quick.newchat":"دردشة جديدة","quick.addep":"إضافة نقطة نهاية","quick.sessions":"الجلسات","quick.settings":"الإعدادات",
"chat.endpoint":"نقطة النهاية","chat.disclaimer":"يستخدم mux النموذج المحدد. تحقق من النتائج المهمة.","chat.empty":"اختر نقطة نهاية وابدأ الدردشة مع نموذجك.",
"col.name":"الاسم","col.adapter":"المحوّل","col.model":"النموذج","col.auth":"المصادقة","col.transport":"النقل","col.target":"الهدف","col.systemPrompt":"توجيه النظام","col.description":"الوصف","col.tools":"الأدوات","col.event":"الحدث","col.command":"الأمر","col.blocking":"حاجب","col.commandId":"معرّف الأمر","col.chord":"تركيبة المفاتيح","col.cmds":"الأوامر","col.state":"الحالة","col.title":"العنوان","col.updated":"آخر تحديث",
"tag.default":"افتراضي","tag.active":"نشط","tag.enabled":"مُفعّل","tag.disabled":"مُعطّل","tag.invalid":"غير صالح","tag.yes":"نعم","tag.no":"لا",
"set.agent":"الوكيل","set.context":"السياق","set.features":"الميزات","set.rest":"خادم REST","set.restReq":"يتطلب إعادة تشغيل",
"lang.label":"اللغة"
},
ru:{
"nav.home":"Главная","nav.chat":"Чат","nav.endpoints":"Эндпоинты","nav.mcp":"Серверы MCP","nav.prompts":"Промпты","nav.subagents":"Субагенты","nav.hooks":"Хуки","nav.commands":"Команды","nav.keybindings":"Горячие клавиши","nav.skills":"Навыки","nav.sessions":"Сессии","nav.settings":"Настройки",
"grp.config":"Конфигурация","grp.system":"Система",
"vt.mcp":"Серверы MCP","vt.hooks":"Хуки событий","vt.commands":"Пользовательские команды",
"act.reload":"Обновить","act.save":"Сохранить","act.cancel":"Отмена","act.close":"Закрыть","act.delete":"Удалить","act.edit":"Изменить","act.duplicate":"Дублировать","act.viewjson":"Показать JSON","act.copyjson":"Копировать JSON","act.copy":"Копировать","act.retry":"Повторить","act.view":"Просмотр","act.enable":"Включить","act.disable":"Выключить","act.confirm":"Подтвердить","act.savesettings":"Сохранить настройки","act.allsessions":"Все сессии",
"add.endpoint":"Добавить эндпоинт","add.server":"Добавить сервер","add.profile":"Добавить профиль","add.subagent":"Добавить субагента","add.hook":"Добавить хук","add.command":"Добавить команду","add.binding":"Добавить сочетание","add.skill":"Добавить навык","act.newchat":"Новый чат",
"tbl.rows":"Строки","tbl.columns":"Столбцы","tbl.filter":"Фильтр…","tbl.norows":"Нет строк, соответствующих фильтрам.","tbl.record":"запись","tbl.records":"записей","tbl.loading":"Загрузка…","tbl.failed":"Не удалось загрузить.","tbl.showing":"Показаны {a}–{b} из {n}",
"toast.saved":"Сохранено","toast.deleted":"Удалено","toast.created":"Создано","toast.copied":"Скопировано в буфер обмена","toast.nameReq":"Имя обязательно","toast.exported":"Экспортировано",
"modal.confirm":"Подтвердите действие",
"empty.endpoints":"Эндпоинты ещё не настроены.","empty.mcp":"Серверы MCP ещё не настроены.","empty.prompts":"Профилей промптов пока нет.","empty.subagents":"Субагенты ещё не определены.","empty.hooks":"Хуки событий не настроены.","empty.commands":"Пользовательские команды не настроены.","empty.keybindings":"Нет своих сочетаний. Действуют значения по умолчанию.","empty.skills":"В каталоге навыков ничего не найдено.","empty.sessions":"Сохранённых сессий пока нет.",
"home.default":"Эндпоинт по умолчанию","home.defaultHint":"Модель, используемая, если ни одна не указана.","home.env":"Окружение","home.envHint":"Этот сервер и его активная конфигурация.","home.recent":"Недавние сессии","home.quick":"Быстрые действия","home.version":"Версия","home.uptime":"Время работы","home.activePrompt":"Активный промпт","home.auth":"Аутентификация","home.configDir":"Каталог конфигурации","home.authOn":"требуется (токен Bearer)","home.authOff":"отключена","home.noDefault":"Эндпоинт по умолчанию не задан.","home.manageEp":"Управление эндпоинтами","home.msg":"сообщ.",
"kpi.endpoints":"Эндпоинты","kpi.mcp":"Серверы MCP","kpi.prompts":"Промпты","kpi.subagents":"Субагенты","kpi.skills":"Навыки","kpi.hookscmds":"Хуки и команды","kpi.keybindings":"Горячие клавиши","kpi.sessions":"Сессии","kpi.totalMsg":"Всего сообщений",
"quick.newchat":"Новый чат","quick.addep":"Добавить эндпоинт","quick.sessions":"Сессии","quick.settings":"Настройки",
"chat.endpoint":"Эндпоинт","chat.disclaimer":"mux использует указанную модель. Проверяйте важные результаты.","chat.empty":"Выберите эндпоинт и начните общаться с вашей моделью.",
"col.name":"Имя","col.adapter":"Адаптер","col.model":"Модель","col.auth":"Аутент.","col.transport":"Транспорт","col.target":"Цель","col.systemPrompt":"Системный промпт","col.description":"Описание","col.tools":"Инструменты","col.event":"Событие","col.command":"Команда","col.blocking":"Блокирующий","col.commandId":"ID команды","col.chord":"Сочетание","col.cmds":"Команды","col.state":"Состояние","col.title":"Заголовок","col.updated":"Обновлено",
"tag.default":"по умолчанию","tag.active":"активный","tag.enabled":"включён","tag.disabled":"выключен","tag.invalid":"недопустимый","tag.yes":"да","tag.no":"нет",
"set.agent":"Агент","set.context":"Контекст","set.features":"Функции","set.rest":"REST-сервер","set.restReq":"требуется перезапуск",
"lang.label":"Язык"
},
ms:{
"nav.home":"Laman Utama","nav.chat":"Sembang","nav.endpoints":"Titik Akhir","nav.mcp":"Pelayan MCP","nav.prompts":"Gesaan","nav.subagents":"Subejen","nav.hooks":"Cangkuk","nav.commands":"Perintah","nav.keybindings":"Pintasan Papan Kekunci","nav.skills":"Kemahiran","nav.sessions":"Sesi","nav.settings":"Tetapan",
"grp.config":"Konfigurasi","grp.system":"Sistem",
"vt.mcp":"Pelayan MCP","vt.hooks":"Cangkuk Peristiwa","vt.commands":"Perintah Tersuai",
"act.reload":"Muat Semula","act.save":"Simpan","act.cancel":"Batal","act.close":"Tutup","act.delete":"Padam","act.edit":"Sunting","act.duplicate":"Salin","act.viewjson":"Lihat JSON","act.copyjson":"Salin JSON","act.copy":"Salin","act.retry":"Cuba semula","act.view":"Lihat","act.enable":"Dayakan","act.disable":"Lumpuhkan","act.confirm":"Sahkan","act.savesettings":"Simpan tetapan","act.allsessions":"Semua sesi",
"add.endpoint":"Tambah titik akhir","add.server":"Tambah pelayan","add.profile":"Tambah profil","add.subagent":"Tambah subejen","add.hook":"Tambah cangkuk","add.command":"Tambah perintah","add.binding":"Tambah pintasan","add.skill":"Tambah kemahiran","act.newchat":"Sembang baharu",
"tbl.rows":"Baris","tbl.columns":"Lajur","tbl.filter":"Tapis…","tbl.norows":"Tiada baris sepadan dengan penapis.","tbl.record":"rekod","tbl.records":"rekod","tbl.loading":"Memuatkan…","tbl.failed":"Gagal dimuatkan.","tbl.showing":"Memaparkan {a}–{b} daripada {n}",
"toast.saved":"Disimpan","toast.deleted":"Dipadam","toast.created":"Dicipta","toast.copied":"Disalin ke papan keratan","toast.nameReq":"Nama diperlukan","toast.exported":"Dieksport",
"modal.confirm":"Sila sahkan",
"empty.endpoints":"Belum ada titik akhir dikonfigurasi.","empty.mcp":"Belum ada pelayan MCP dikonfigurasi.","empty.prompts":"Belum ada profil gesaan.","empty.subagents":"Belum ada subejen ditakrifkan.","empty.hooks":"Tiada cangkuk peristiwa dikonfigurasi.","empty.commands":"Tiada perintah tersuai dikonfigurasi.","empty.keybindings":"Tiada pintasan tersuai. Nilai lalai digunakan.","empty.skills":"Tiada kemahiran ditemui dalam direktori.","empty.sessions":"Belum ada sesi disimpan.",
"home.default":"Titik akhir lalai","home.defaultHint":"Model yang digunakan apabila tiada dinyatakan.","home.env":"Persekitaran","home.envHint":"Pelayan ini dan konfigurasi aktifnya.","home.recent":"Sesi terkini","home.quick":"Tindakan pantas","home.version":"Versi","home.uptime":"Masa berjalan","home.activePrompt":"Gesaan aktif","home.auth":"Pengesahan","home.configDir":"Direktori konfigurasi","home.authOn":"diperlukan (token bearer)","home.authOff":"dilumpuhkan","home.noDefault":"Tiada titik akhir lalai ditetapkan.","home.manageEp":"Urus titik akhir","home.msg":"mesej",
"kpi.endpoints":"Titik akhir","kpi.mcp":"Pelayan MCP","kpi.prompts":"Gesaan","kpi.subagents":"Subejen","kpi.skills":"Kemahiran","kpi.hookscmds":"Cangkuk & perintah","kpi.keybindings":"Pintasan","kpi.sessions":"Sesi","kpi.totalMsg":"Jumlah mesej",
"quick.newchat":"Sembang baharu","quick.addep":"Tambah titik akhir","quick.sessions":"Sesi","quick.settings":"Tetapan",
"chat.endpoint":"Titik akhir","chat.disclaimer":"mux menggunakan model yang dinyatakan. Sahkan keputusan penting.","chat.empty":"Pilih titik akhir dan mula bersembang dengan model anda.",
"col.name":"Nama","col.adapter":"Penyesuai","col.model":"Model","col.auth":"Auth","col.transport":"Pengangkutan","col.target":"Sasaran","col.systemPrompt":"Gesaan sistem","col.description":"Perihalan","col.tools":"Alat","col.event":"Peristiwa","col.command":"Perintah","col.blocking":"Menyekat","col.commandId":"ID perintah","col.chord":"Gabungan kekunci","col.cmds":"Perintah","col.state":"Keadaan","col.title":"Tajuk","col.updated":"Dikemas kini",
"tag.default":"lalai","tag.active":"aktif","tag.enabled":"didayakan","tag.disabled":"dilumpuhkan","tag.invalid":"tidak sah","tag.yes":"ya","tag.no":"tidak",
"set.agent":"Ejen","set.context":"Konteks","set.features":"Ciri","set.rest":"Pelayan REST","set.restReq":"perlu dimulakan semula",
"lang.label":"Bahasa"
},
hi:{
"nav.home":"होम","nav.chat":"चैट","nav.endpoints":"एंडपॉइंट","nav.mcp":"MCP सर्वर","nav.prompts":"प्रॉम्प्ट","nav.subagents":"सबएजेंट","nav.hooks":"हुक","nav.commands":"कमांड","nav.keybindings":"कीबाइंडिंग","nav.skills":"स्किल","nav.sessions":"सत्र","nav.settings":"सेटिंग्स",
"grp.config":"कॉन्फ़िगरेशन","grp.system":"सिस्टम",
"vt.mcp":"MCP सर्वर","vt.hooks":"इवेंट हुक","vt.commands":"कस्टम कमांड",
"act.reload":"पुनः लोड करें","act.save":"सहेजें","act.cancel":"रद्द करें","act.close":"बंद करें","act.delete":"हटाएँ","act.edit":"संपादित करें","act.duplicate":"डुप्लिकेट","act.viewjson":"JSON देखें","act.copyjson":"JSON कॉपी करें","act.copy":"कॉपी","act.retry":"पुनः प्रयास","act.view":"देखें","act.enable":"सक्षम करें","act.disable":"अक्षम करें","act.confirm":"पुष्टि करें","act.savesettings":"सेटिंग्स सहेजें","act.allsessions":"सभी सत्र",
"add.endpoint":"एंडपॉइंट जोड़ें","add.server":"सर्वर जोड़ें","add.profile":"प्रोफ़ाइल जोड़ें","add.subagent":"सबएजेंट जोड़ें","add.hook":"हुक जोड़ें","add.command":"कमांड जोड़ें","add.binding":"बाइंडिंग जोड़ें","add.skill":"स्किल जोड़ें","act.newchat":"नई चैट",
"tbl.rows":"पंक्तियाँ","tbl.columns":"कॉलम","tbl.filter":"फ़िल्टर…","tbl.norows":"किसी भी पंक्ति में फ़िल्टर मेल नहीं खाते।","tbl.record":"रिकॉर्ड","tbl.records":"रिकॉर्ड","tbl.loading":"लोड हो रहा है…","tbl.failed":"लोड करने में विफल।","tbl.showing":"{n} में से {a}–{b} दिखा रहे हैं",
"toast.saved":"सहेजा गया","toast.deleted":"हटाया गया","toast.created":"बनाया गया","toast.copied":"क्लिपबोर्ड पर कॉपी किया गया","toast.nameReq":"नाम आवश्यक है","toast.exported":"निर्यात किया गया",
"modal.confirm":"कृपया पुष्टि करें",
"empty.endpoints":"अभी तक कोई एंडपॉइंट कॉन्फ़िगर नहीं है।","empty.mcp":"अभी तक कोई MCP सर्वर कॉन्फ़िगर नहीं है।","empty.prompts":"अभी तक कोई प्रॉम्प्ट प्रोफ़ाइल नहीं।","empty.subagents":"अभी तक कोई सबएजेंट परिभाषित नहीं।","empty.hooks":"कोई इवेंट हुक कॉन्फ़िगर नहीं।","empty.commands":"कोई कस्टम कमांड कॉन्फ़िगर नहीं।","empty.keybindings":"कोई कस्टम कीबाइंडिंग नहीं। डिफ़ॉल्ट लागू हैं।","empty.skills":"स्किल डायरेक्टरी में कोई स्किल नहीं मिली।","empty.sessions":"अभी तक कोई सहेजा गया सत्र नहीं।",
"home.default":"डिफ़ॉल्ट एंडपॉइंट","home.defaultHint":"जब कोई निर्दिष्ट न हो तब उपयोग होने वाला मॉडल।","home.env":"वातावरण","home.envHint":"यह सर्वर और इसका सक्रिय कॉन्फ़िगरेशन।","home.recent":"हाल के सत्र","home.quick":"त्वरित क्रियाएँ","home.version":"संस्करण","home.uptime":"अपटाइम","home.activePrompt":"सक्रिय प्रॉम्प्ट","home.auth":"प्रमाणीकरण","home.configDir":"कॉन्फ़िगरेशन डायरेक्टरी","home.authOn":"आवश्यक (बियरर टोकन)","home.authOff":"अक्षम","home.noDefault":"कोई डिफ़ॉल्ट एंडपॉइंट सेट नहीं है।","home.manageEp":"एंडपॉइंट प्रबंधित करें","home.msg":"संदेश",
"kpi.endpoints":"एंडपॉइंट","kpi.mcp":"MCP सर्वर","kpi.prompts":"प्रॉम्प्ट","kpi.subagents":"सबएजेंट","kpi.skills":"स्किल","kpi.hookscmds":"हुक और कमांड","kpi.keybindings":"कीबाइंडिंग","kpi.sessions":"सत्र","kpi.totalMsg":"कुल संदेश",
"quick.newchat":"नई चैट","quick.addep":"एंडपॉइंट जोड़ें","quick.sessions":"सत्र","quick.settings":"सेटिंग्स",
"chat.endpoint":"एंडपॉइंट","chat.disclaimer":"mux निर्दिष्ट मॉडल का उपयोग कर रहा है। महत्वपूर्ण परिणाम सत्यापित करें।","chat.empty":"एक एंडपॉइंट चुनें और अपने मॉडल से चैट शुरू करें।",
"col.name":"नाम","col.adapter":"अडैप्टर","col.model":"मॉडल","col.auth":"प्रमाणीकरण","col.transport":"ट्रांसपोर्ट","col.target":"लक्ष्य","col.systemPrompt":"सिस्टम प्रॉम्प्ट","col.description":"विवरण","col.tools":"टूल","col.event":"इवेंट","col.command":"कमांड","col.blocking":"अवरोधक","col.commandId":"कमांड आईडी","col.chord":"कुंजी संयोजन","col.cmds":"कमांड","col.state":"स्थिति","col.title":"शीर्षक","col.updated":"अपडेट किया गया",
"tag.default":"डिफ़ॉल्ट","tag.active":"सक्रिय","tag.enabled":"सक्षम","tag.disabled":"अक्षम","tag.invalid":"अमान्य","tag.yes":"हाँ","tag.no":"नहीं",
"set.agent":"एजेंट","set.context":"संदर्भ","set.features":"विशेषताएँ","set.rest":"REST सर्वर","set.restReq":"पुनरारंभ आवश्यक",
"lang.label":"भाषा"
}
};
var LOCALE=(function(){try{return localStorage.getItem("mux.lang")||"en";}catch(e){return "en";}})();
if(!I18N[LOCALE])LOCALE="en";
function t(k,p){var s=(I18N[LOCALE]&&I18N[LOCALE][k]!=null)?I18N[LOCALE][k]:(I18N.en[k]!=null?I18N.en[k]:k);if(p)for(var x in p)s=(""+s).split("{"+x+"}").join(p[x]);return s;}
function viewTitle(v){var m={mcp:"vt.mcp",hooks:"vt.hooks",commands:"vt.commands"};return t(m[v]||("nav."+v));}
function applyI18n(){
  var e=document.querySelectorAll("[data-i18n]"),i;for(i=0;i<e.length;i++)e[i].textContent=t(e[i].getAttribute("data-i18n"));
  var a=document.querySelectorAll("[data-i18n-title]");for(i=0;i<a.length;i++)a[i].title=t(a[i].getAttribute("data-i18n-title"));
  var b=document.querySelectorAll("[data-i18n-ph]");for(i=0;i<b.length;i++)b[i].placeholder=t(b[i].getAttribute("data-i18n-ph"));
  document.documentElement.lang=LOCALE;document.documentElement.dir=(LOCALE==="ar")?"rtl":"ltr";
}
function i18nRerender(){var act=document.querySelector(".nav-item.active");var v=act?act.getAttribute("data-view"):"home";el("viewTitle").textContent=viewTitle(v);document.title="mux · "+viewTitle(v);if(VIEW_LOADERS[v])VIEW_LOADERS[v]();else if(v==="chat")renderMessages();}
function setLocale(l){LOCALE=I18N[l]?l:"en";try{localStorage.setItem("mux.lang",LOCALE);}catch(e){}applyI18n();i18nRerender();}

var messages=[];
var busy=false;
var currentSessionId=null;
var currentModel="";

function el(id){return document.getElementById(id);}
function esc(s){return (s||"").replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;");}

function toast(msg,isErr){var t=el("toast");t.textContent=msg;t.className="toast show"+(isErr?" err":"");setTimeout(function(){t.className="toast";},2600);}

function api(path,method,body){
  var headers={"Content-Type":"application/json"};
  if(API_KEY) headers["Authorization"]="Bearer "+API_KEY;
  return fetch(path,{method:method||"GET",headers:headers,body:body?JSON.stringify(body):undefined})
    .then(function(r){return r.text().then(function(t){var j=null;try{j=t?JSON.parse(t):null;}catch(e){}
      if(!r.ok){var m=(j&&j.Message)||(j&&j.message)||("HTTP "+r.status);throw new Error(m);}return j;});});
}

/* minimal, safe markdown. Everything is escaped first; block elements (code fences, headings,
   lists, blockquotes, paragraphs) are emitted as real HTML so inline transforms and newline
   handling never leak into a code block. */
function inline(s){
  s=esc(s);
  s=s.replace(/`([^`]+)`/g,function(m,c){return "<code>"+c+"</code>";});
  s=s.replace(/\*\*([^*]+)\*\*/g,"<strong>$1</strong>");
  s=s.replace(/(^|[^*])\*([^*\n]+)\*/g,"$1<em>$2</em>");
  s=s.replace(/\[([^\]]+)\]\((https?:[^)\s]+)\)/g,'<a href="$2" target="_blank" rel="noopener">$1</a>');
  return s;
}
function md(text){
  var lines=(text||"").replace(/\r\n/g,"\n").split("\n");
  var html="",para=[],i=0;
  function flush(){ if(para.length){ html+="<p>"+para.map(inline).join("<br>")+"</p>"; para=[]; } }
  while(i<lines.length){
    var line=lines[i];
    /* fenced code block: opening ``` on its own line, contents verbatim, closing ``` */
    if(/^\s*```/.test(line)){
      flush(); i++;
      var code=[];
      while(i<lines.length && !/^\s*```/.test(lines[i])){ code.push(lines[i]); i++; }
      if(i<lines.length) i++;
      html+="<pre><code>"+esc(code.join("\n"))+"</code></pre>";
      continue;
    }
    if(/^\s*$/.test(line)){ flush(); i++; continue; }
    var h=line.match(/^(#{1,6})\s+(.*)$/);
    if(h){ flush(); var lv=h[1].length; html+="<h"+lv+">"+inline(h[2])+"</h"+lv+">"; i++; continue; }
    if(/^\s*[-*+]\s+/.test(line)){ flush(); html+="<ul>"; while(i<lines.length&&/^\s*[-*+]\s+/.test(lines[i])){ html+="<li>"+inline(lines[i].replace(/^\s*[-*+]\s+/,""))+"</li>"; i++; } html+="</ul>"; continue; }
    if(/^\s*\d+\.\s+/.test(line)){ flush(); html+="<ol>"; while(i<lines.length&&/^\s*\d+\.\s+/.test(lines[i])){ html+="<li>"+inline(lines[i].replace(/^\s*\d+\.\s+/,""))+"</li>"; i++; } html+="</ol>"; continue; }
    if(/^\s*>\s?/.test(line)){ flush(); var q=[]; while(i<lines.length&&/^\s*>\s?/.test(lines[i])){ q.push(lines[i].replace(/^\s*>\s?/,"")); i++; } html+="<blockquote>"+q.map(inline).join("<br>")+"</blockquote>"; continue; }
    para.push(line); i++;
  }
  flush();
  return html;
}

function fmtMs(n){ if(n==null||n<0)return "—"; return n>=1000?(n/1000).toFixed(2)+" s":n+" ms"; }
function statInfo(s){
  if(!s)return "";
  var rows=""
    +'<div class="r"><span>Time to first token</span><span>'+fmtMs(s.TtftMs)+'</span></div>'
    +'<div class="r"><span>Streaming time</span><span>'+fmtMs(s.StreamingMs)+'</span></div>'
    +'<div class="r"><span>Total time</span><span>'+fmtMs(s.TotalMs)+'</span></div>'
    +'<div class="r"><span>Input tokens</span><span>'+(s.InputTokens||0)+'</span></div>'
    +'<div class="r"><span>Output tokens</span><span>'+(s.OutputTokens||0)+'</span></div>'
    +'<div class="r"><span>Total tokens</span><span>'+(s.TotalTokens||0)+'</span></div>';
  return ' <i class="statinfo">i<span class="stattip">'+rows+'</span></i>';
}
function renderMessages(){
  var box=el("messages");
  if(messages.length===0){box.innerHTML='<div class="empty">'+esc(t("chat.empty"))+'</div>';return;}
  var html="";
  for(var i=0;i<messages.length;i++){
    var m=messages[i];
    var think=(m.role==="assistant"&&m.thinking)?'<div class="think">💭 '+esc(m.thinking).replace(/\n/g,"<br>")+'</div>':'';
    var body=m.typing?'<div class="thinking"><span></span><span></span><span></span></div>':(m.role==="assistant"?md(m.content):"<p>"+esc(m.content).replace(/\n/g,"<br>")+"</p>");
    var cp=(m.role==="assistant"&&!m.typing&&!m.local&&m.content)?'<button class="msgcopy" title="Copy response to the clipboard" onclick="copyMsg('+i+',this)">⧉</button>':'';
    var inner=cp+think+body;
    html+='<div class="msg '+m.role+'"><div class="bubble">'+inner+'</div>';
    if(m.role==="assistant"&&!m.typing&&m.model){html+='<div class="meta">'+esc(m.model)+statInfo(m.stats)+'</div>';}
    html+='</div>';
  }
  box.innerHTML=html;
  box.scrollTop=box.scrollHeight;
}
function copyMsg(i,btn){var m=messages[i];if(!m||!m.content)return;copyText(m.content,btn);}

/* ---- conversations (persisted like the desktop/TUI: list, open, new, rename, delete, export) ---- */
function setChatTitle(x){var e=el("chatTitle");if(e)e.textContent=x||"";}
function loadConvos(){api("/v1.0/api/sessions").then(function(r){renderConvos((r&&r.Items)||[]);}).catch(function(){renderConvos([]);});}
function highlightConvo(){Array.prototype.forEach.call(document.querySelectorAll("#convoList .convo-item"),function(row){row.classList.toggle("active",row.getAttribute("data-id")===currentSessionId);});}
function renderConvos(items){
  var box=el("convoList");if(!box)return;
  if(!items.length){box.innerHTML='<div class="convo-empty">'+"No conversations yet."+'</div>';return;}
  items.sort(function(a,b){return String(b.UpdatedUtc||"").localeCompare(String(a.UpdatedUtc||""));});
  var html="";
  for(var i=0;i<items.length;i++){var s=items[i];var act=(s.Id===currentSessionId)?" active":"";
    html+='<div class="convo-item'+act+'" data-id="'+esc(s.Id)+'"><span class="ct" title="'+esc(s.Title||s.Id)+'">'+esc(s.Title||"Untitled")+'</span>'
      +'<button class="convo-menu" title="Actions">&#8942;</button></div>';
  }
  box.innerHTML=html;
  Array.prototype.forEach.call(box.querySelectorAll(".convo-item"),function(row){
    var id=row.getAttribute("data-id");
    row.addEventListener("click",function(e){
      var b=e.target.closest(".convo-menu");
      if(b){e.stopPropagation();openMenu(b,[
        {label:"Rename",run:function(){renameConvo(id);}},
        {label:"Export (Markdown)",run:function(){exportSe(id,"md");}},
        {label:"Export (HTML)",run:function(){exportSe(id,"html");}},
        {sep:true},
        {label:"Delete",danger:true,run:function(){deleteConvo(id);}}
      ]);return;}
      openConvo(id);
    });
  });
}
function deleteMultiple(){
  api("/v1.0/api/sessions").then(function(r){
    var items=(r&&r.Items)||[];
    if(!items.length){toast("No conversations to delete.");return;}
    items.sort(function(a,b){return String(b.UpdatedUtc||"").localeCompare(String(a.UpdatedUtc||""));});
    var rows=items.map(function(s){return '<label class="pickrow"><input type="checkbox" value="'+esc(s.Id)+'"><span>'+esc(s.Title||"Untitled")+'</span></label>';}).join("");
    var body='<div class="pickhead"><button type="button" class="btn secondary" id="pickAll">Select all</button><button type="button" class="btn secondary" id="pickNone">Select none</button></div><div class="picklist">'+rows+'</div>';
    openModal("Delete conversations",body,[
      {label:"Cancel"},
      {label:"Delete selected",danger:true,onClick:function(){
        var ids=Array.prototype.slice.call(document.querySelectorAll("#modalBody .picklist input:checked")).map(function(c){return c.value;});
        if(!ids.length){toast("Select at least one conversation.",true);return;}
        confirmModal("Delete "+ids.length+" conversation(s)? This cannot be undone.",function(){doDeleteMultiple(ids);});
      }}
    ],true);
    el("pickAll").addEventListener("click",function(){Array.prototype.forEach.call(document.querySelectorAll("#modalBody .picklist input"),function(c){c.checked=true;});});
    el("pickNone").addEventListener("click",function(){Array.prototype.forEach.call(document.querySelectorAll("#modalBody .picklist input"),function(c){c.checked=false;});});
  }).catch(function(e){toast(e.message,true);});
}
function doDeleteMultiple(ids){
  var ok=0,fail=0,done=0;
  ids.forEach(function(id){
    api("/v1.0/api/sessions?id="+encodeURIComponent(id),"DELETE").then(function(){ok++;},function(){fail++;}).then(function(){
      done++;if(done!==ids.length)return;
      if(ids.indexOf(currentSessionId)>=0)newChat();
      loadConvos();
      var msg=(fail===0)?("Deleted "+ok+" conversation"+(ok===1?"":"s")+"."):("Deleted "+ok+", failed to delete "+fail+".");
      openModal(fail===0?"Deleted":"Completed with errors",'<p style="margin:0">'+esc(msg)+'</p>',[{label:"OK",primary:true}]);
    });
  });
}
function openConvo(id){
  api("/v1.0/api/sessions/detail?id="+encodeURIComponent(id)).then(function(d){
    messages=((d&&d.Messages)||[]).map(function(m){return {role:m.Role,content:m.Content};});
    currentSessionId=(d&&d.Id)||id;currentModel=(d&&d.Model)||"";
    if(d&&d.EndpointName){var sel=el("endpointSelect");if(sel){for(var i=0;i<sel.options.length;i++){if(sel.options[i].value===d.EndpointName){sel.selectedIndex=i;break;}}}}
    setChatTitle((d&&d.Title)||"");renderMessages();highlightConvo();
  }).catch(function(e){toast(e.message,true);});
}
function newChat(){messages=[];currentSessionId=null;currentModel="";setChatTitle("");renderMessages();highlightConvo();var c=el("composer");if(c)c.focus();}
function persistConvo(){
  var real=messages.filter(function(m){return !m.typing&&!m.local;});
  if(!real.length)return;
  var payload={Id:currentSessionId||"",Title:"",EndpointName:el("endpointSelect").value||"",Model:currentModel||"",Messages:real.map(function(m){return {Role:m.role,Content:m.content};})};
  api("/v1.0/api/sessions","PUT",payload).then(function(s){if(s&&s.Id){currentSessionId=s.Id;setChatTitle(s.Title||"");}loadConvos();}).catch(function(){});
}
function renameConvo(id){
  var cur="";var e=document.querySelector('#convoList .convo-item[data-id="'+id+'"] .ct');if(e)cur=e.getAttribute("title")||e.textContent;
  var val=window.prompt("Rename conversation",cur);if(val==null)return;val=val.trim();if(!val)return;
  api("/v1.0/api/sessions/detail?id="+encodeURIComponent(id)).then(function(d){
    var payload={Id:id,Title:val,EndpointName:(d&&d.EndpointName)||"",Model:(d&&d.Model)||"",Messages:((d&&d.Messages)||[]).map(function(m){return {Role:m.Role,Content:m.Content};})};
    return api("/v1.0/api/sessions","PUT",payload);
  }).then(function(s){if(id===currentSessionId&&s)setChatTitle(s.Title||"");loadConvos();}).catch(function(e){toast(e.message,true);});
}
function deleteConvo(id){
  confirmModal("Delete this conversation?",function(){
    api("/v1.0/api/sessions?id="+encodeURIComponent(id),"DELETE").then(function(){if(id===currentSessionId)newChat();loadConvos();toast("Deleted");}).catch(function(e){toast(e.message,true);});
  });
}

var CHAT_HELP="**Chat commands**\n\n"+
"- `/?`, `/help` — show this list\n"+
"- `/new`, `/clear` — start a new conversation\n"+
"- `/context`, `/stats` — show the last turn's timing and tokens\n"+
"- `/usage` — open usage analytics\n"+
"- `/endpoints`, `/models` — manage model endpoints\n"+
"- `/mcp` — manage MCP servers\n"+
"- `/prompts` — manage prompt profiles\n"+
"- `/skills` — manage skills\n"+
"- `/subagents` — manage subagents\n"+
"- `/pricing` — edit model pricing\n"+
"- `/plugins`, `/hooks` — manage hooks\n"+
"- `/commands` — manage custom commands\n"+
"- `/keybindings` — edit keybindings\n"+
"- `/sessions` — browse saved sessions\n"+
"- `/settings` — open settings";
function showChatStats(){
  var last=null;
  for(var i=messages.length-1;i>=0;i--){if(messages[i].role==="assistant"&&messages[i].stats){last=messages[i];break;}}
  if(!last){toast("No completed turns yet.",true);return;}
  var s=last.stats;
  var body="**Last turn**\n\n"+
    "- Model: `"+(last.model||currentModel||"—")+"`\n"+
    "- Time to first token: "+(s.TtftMs>=0?s.TtftMs+" ms":"—")+"\n"+
    "- Streaming: "+(s.StreamingMs||0)+" ms\n"+
    "- Total: "+(s.TotalMs||0)+" ms\n"+
    "- Tokens — in: "+(s.InputTokens||0)+", out: "+(s.OutputTokens||0)+", total: "+(s.TotalTokens||0);
  messages.push({role:"assistant",content:body,local:true});renderMessages();
}
function handleChatCommand(text){
  var raw=text.trim();var sp=raw.indexOf(" ");if(sp>0)raw=raw.slice(0,sp);
  var cmd=raw.slice(1).toLowerCase();
  switch(cmd){
    case "?": case "help": case "menu": messages.push({role:"assistant",content:CHAT_HELP,local:true});renderMessages();return;
    case "new": case "clear": newChat();return;
    case "context": case "stats": showChatStats();return;
    case "usage": switchView("usage");return;
    case "endpoints": case "endpoint": case "models": case "model": switchView("endpoints");return;
    case "mcp": case "mcps": switchView("mcp");return;
    case "prompt": case "prompts": switchView("prompts");return;
    case "skill": case "skills": switchView("skills");return;
    case "subagent": case "subagents": case "agents": switchView("subagents");return;
    case "pricing": switchView("pricing");return;
    case "plugin": case "plugins": case "hooks": switchView("hooks");return;
    case "commands": switchView("commands");return;
    case "keys": case "keybindings": case "shortcuts": switchView("keybindings");return;
    case "sessions": switchView("sessions");return;
    case "settings": switchView("settings");return;
    default: toast("Unknown command: "+text+" (try /?)",true);
  }
}
function sendChat(){
  if(busy)return;
  var text=el("composer").value.trim();
  var endpoint=el("endpointSelect").value;
  if(!text)return;
  // Chat slash commands are handled locally and never sent to the model.
  if(text.charAt(0)==="/"){el("composer").value="";el("composer").style.height="44px";handleChatCommand(text);return;}
  if(!endpoint){toast("No endpoint selected",true);return;}
  messages.push({role:"user",content:text});
  el("composer").value="";el("composer").style.height="44px";
  var typing={role:"assistant",content:"",typing:true};
  messages.push(typing);renderMessages();busy=true;el("sendBtn").textContent="■";el("sendBtn").title="Stop the response";el("sendBtn").style.background="#e5534b";
  var payload={endpoint:endpoint,messages:messages.filter(function(m){return !m.typing&&!m.local;}).map(function(m){return {role:m.role,content:m.content};})};
  streamChat(payload,typing);
}

var currentAbort=null;
function endChat(){busy=false;currentAbort=null;el("sendBtn").textContent="➤";el("sendBtn").title="Send";el("sendBtn").style.background="";}
// Stop the in-flight response. Abort the stream and drop the incomplete turn (both the empty assistant
// bubble and the user prompt that started it) so the model history stays clean — matching the TUI/desktop.
function stopChat(){if(currentAbort){try{currentAbort.abort();}catch(e){}}}
function dropIncompleteTurn(typing){
  var i=messages.indexOf(typing);if(i>=0)messages.splice(i,1);
  if(messages.length&&messages[messages.length-1].role==="user")messages.pop();
  renderMessages();
}

// Streams the assistant reply token-by-token over Server-Sent Events so the answer appears as it is
// produced, rather than all at once when the whole completion finishes.
function streamChat(payload,typing){
  var ac=(typeof AbortController!=="undefined")?new AbortController():null;
  currentAbort=ac;
  var headers={"Content-Type":"application/json","Accept":"text/event-stream"};
  if(API_KEY) headers["Authorization"]="Bearer "+API_KEY;
  var opts={method:"POST",headers:headers,body:JSON.stringify(payload)};
  if(ac)opts.signal=ac.signal;
  fetch("/v1.0/api/chat/stream",opts).then(function(res){
    if(!res.ok||!res.body){
      return res.text().then(function(t){var j=null;try{j=t?JSON.parse(t):null;}catch(e){}throw new Error((j&&(j.Message||j.message))||("HTTP "+res.status));});
    }
    var reader=res.body.getReader();var dec=new TextDecoder();var buf="";
    function pump(){
      return reader.read().then(function(r){
        if(r.done){if(typing.typing){typing.typing=false;renderMessages();}endChat();return;}
        buf+=dec.decode(r.value,{stream:true});
        var idx;
        while((idx=buf.indexOf("\n\n"))>=0){var block=buf.slice(0,idx);buf=buf.slice(idx+2);handleSse(block,typing);}
        return pump();
      });
    }
    return pump();
  }).catch(function(e){
    if(e&&e.name==="AbortError"){dropIncompleteTurn(typing);endChat();toast("Stopped");return;}
    typing.typing=false;typing.content="⚠️ "+e.message;typing.role="assistant";renderMessages();toast(e.message,true);endChat();
  });
}

function handleSse(block,typing){
  var ev="message",data="";
  var lines=block.split("\n");
  for(var i=0;i<lines.length;i++){
    var line=lines[i];
    if(line.indexOf("event:")===0){ev=line.slice(6).trim();}
    else if(line.indexOf("data:")===0){data+=(data?"\n":"")+line.slice(5).replace(/^ /,"");}
  }
  if(!data)return;
  var parsed;try{parsed=JSON.parse(data);}catch(e){return;}
  if(ev==="thinking"){typing.thinking=(typing.thinking||"")+parsed;renderMessages();}
  else if(ev==="token"){typing.typing=false;typing.content+=parsed;renderMessages();}
  else if(ev==="done"){typing.typing=false;typing.content=(parsed&&parsed.Content)||typing.content;typing.model=(parsed&&parsed.Model)||"";typing.stats=(parsed&&parsed.Stats)||null;if(typing.model)currentModel=typing.model;renderMessages();endChat();persistConvo();}
  else if(ev==="error"){typing.typing=false;typing.content="⚠️ "+parsed;renderMessages();toast(String(parsed),true);endChat();}
}

function loadEndpoints(){
  api("/v1.0/api/endpoints").then(function(res){
    var items=(res&&res.Items)||[];var sel=el("endpointSelect");sel.innerHTML="";
    if(items.length===0){var o=document.createElement("option");o.textContent="(no endpoints configured)";o.value="";sel.appendChild(o);return;}
    items.forEach(function(ep){var o=document.createElement("option");o.value=ep.Name;o.textContent=ep.Name+"  ·  "+ep.Model;if(ep.IsDefault)o.selected=true;sel.appendChild(o);});
    warmModel();
  }).catch(function(e){toast("Failed to load endpoints: "+e.message,true);});
}

// Warm (load) the selected model so the first chat token is fast, and surface a small ready/unreachable
// status. Called when endpoints load, when the chat view opens, and whenever the endpoint is switched.
function warmModel(){
  var ep=el("endpointSelect");var st=el("modelStatus");
  if(!ep||!ep.value){if(st)st.textContent="";return;}
  if(st){st.textContent="⏳ loading…";st.className="model-status";}
  api("/v1.0/api/model/load","POST",{Endpoint:ep.value}).then(function(r){
    if(!st)return;
    if(r&&r.Ok){st.textContent="✓ ready";st.className="model-status ready";}
    else if(r&&r.Reachable){st.textContent="⚠ reachable";st.className="model-status warn";}
    else{st.textContent="✗ unreachable";st.className="model-status err";}
  }).catch(function(){if(st){st.textContent="✗ unreachable";st.className="model-status err";}});
}

function loadChat(){loadConvos();warmModel();}

function loadSettings(){
  api("/v1.0/api/settings").then(function(s){
    el("s_defaultApprovalPolicy").value=s.DefaultApprovalPolicy;
    el("s_maxAgentIterations").value=s.MaxAgentIterations;
    el("s_maxConcurrency").value=s.MaxConcurrency;
    el("s_defaultEnqueueBehavior").value=s.DefaultEnqueueBehavior;
    el("s_toolTimeoutMs").value=s.ToolTimeoutMs;
    el("s_processTimeoutMs").value=s.ProcessTimeoutMs;
    el("s_autoCompactEnabled").checked=s.AutoCompactEnabled;
    el("s_compactionStrategy").value=s.CompactionStrategy;
    el("s_compactionPreserveTurns").value=s.CompactionPreserveTurns;
    el("s_contextWarningThresholdPercent").value=s.ContextWarningThresholdPercent;
    el("s_skillsEnabled").checked=s.SkillsEnabled;
    el("s_taskPlanningEnabled").checked=s.TaskPlanningEnabled;
    el("s_taskParallelismEnabled").checked=s.TaskParallelismEnabled;
    el("s_ignoreCertErrors").checked=s.IgnoreCertErrors;
    el("s_showBoundaryLines").checked=s.ShowBoundaryLines;
    el("s_rest_enabled").checked=s.Rest.Enabled;
    el("s_rest_hostname").value=s.Rest.Hostname;
    el("s_rest_port").value=s.Rest.Port;
    el("s_rest_ssl").checked=s.Rest.Ssl;
    el("s_rest_corsAllowOrigin").value=s.Rest.CorsAllowOrigin;
    el("s_rest_apiKey").value="";
    el("apiKeyState").textContent=s.Rest.ApiKeySet?"(set — leave blank to keep)":"(not set)";
  }).catch(function(e){toast("Failed to load settings: "+e.message,true);});
}

function saveSettings(){
  var dto={
    DefaultApprovalPolicy:el("s_defaultApprovalPolicy").value,
    MaxAgentIterations:parseInt(el("s_maxAgentIterations").value,10),
    MaxConcurrency:parseInt(el("s_maxConcurrency").value,10),
    DefaultEnqueueBehavior:el("s_defaultEnqueueBehavior").value,
    ToolTimeoutMs:parseInt(el("s_toolTimeoutMs").value,10),
    ProcessTimeoutMs:parseInt(el("s_processTimeoutMs").value,10),
    AutoCompactEnabled:el("s_autoCompactEnabled").checked,
    CompactionStrategy:el("s_compactionStrategy").value,
    CompactionPreserveTurns:parseInt(el("s_compactionPreserveTurns").value,10),
    ContextWarningThresholdPercent:parseInt(el("s_contextWarningThresholdPercent").value,10),
    SkillsEnabled:el("s_skillsEnabled").checked,
    TaskPlanningEnabled:el("s_taskPlanningEnabled").checked,
    TaskParallelismEnabled:el("s_taskParallelismEnabled").checked,
    IgnoreCertErrors:el("s_ignoreCertErrors").checked,
    ShowBoundaryLines:el("s_showBoundaryLines").checked,
    Rest:{
      Enabled:el("s_rest_enabled").checked,
      Hostname:el("s_rest_hostname").value,
      Port:parseInt(el("s_rest_port").value,10),
      Ssl:el("s_rest_ssl").checked,
      CorsAllowOrigin:el("s_rest_corsAllowOrigin").value,
      ApiKey:el("s_rest_apiKey").value||null
    }
  };
  var btn=el("saveSettingsBtn");if(btn)btn.disabled=true;
  api("/v1.0/api/settings","PUT",dto).then(function(){toast("Settings saved");loadSettings();}).catch(function(e){toast(e.message,true);}).finally(function(){if(btn)btn.disabled=false;});
}

/* ================= reusable modal + table + form infra ================= */
var IC={
 edit:'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
 del:'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/></svg>',
 view:'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12Z"/><circle cx="12" cy="12" r="3"/></svg>',
 dl:'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="M7 10l5 5 5-5"/><path d="M12 15V3"/></svg>',
 power:'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18.4 6.6a9 9 0 1 1-12.8 0"/><path d="M12 2v10"/></svg>',
 reload:'<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>'
};
/* Turn the text "Reload"/"Refresh" list buttons into compact icon buttons. Done in JS (not the static
   markup) so the icon is applied uniformly and the i18n pass can't overwrite it: data-i18n is removed so
   a later language switch won't restore the text label. The button id, handlers, and title tooltip are
   left intact, and the current label becomes the accessible name. */
function iconizeRefreshButtons(){
  Array.prototype.forEach.call(document.querySelectorAll("button"),function(b){
    var id=b.id||"";
    if(!(/_reload$/.test(id)||id==="usage_refresh"||id==="reloadSettingsBtn"))return;
    if(!b.getAttribute("aria-label"))b.setAttribute("aria-label",(b.textContent||"Reload").trim());
    b.removeAttribute("data-i18n");
    b.classList.remove("btn","secondary");
    b.classList.add("iconbtn","icononly");
    b.innerHTML=IC.reload;
  });
}
function openModal(title,bodyHtml,buttons,wide){
  el("modalTitle").textContent=title;el("modalBody").innerHTML=bodyHtml;
  el("modalBox").className="modal"+(wide?(wide===true?" wide":" "+wide):"");
  var foot=el("modalFoot");foot.innerHTML="";
  (buttons||[]).forEach(function(b){var btn=document.createElement("button");
    btn.className="btn"+(b.primary?"":" secondary")+(b.danger?" danger":"");btn.textContent=b.label;
    btn.addEventListener("click",b.onClick||closeModal);foot.appendChild(btn);});
  el("modalOverlay").classList.add("show");
}
function closeModal(){el("modalOverlay").classList.remove("show");el("modalBody").innerHTML="";}
function confirmModal(msg,onYes){openModal(t("modal.confirm"),'<p style="margin:0">'+esc(msg)+'</p>',
  [{label:t("act.cancel"),onClick:closeModal},{label:t("act.confirm"),primary:true,danger:true,onClick:function(){closeModal();onYes();}}]);}
function trim(x){return (""+x).trim();}
function fieldHtml(f){
  if(f.type==="section")return '<div class="form-section">'+esc(f.label)+'</div>';
  var id="f_"+f.id,sub=f.sub?' <span class="sub">'+esc(f.sub)+'</span>':'',inp,tall="";
  if(f.type==="checkbox")inp='<input type="checkbox" id="'+id+'">';
  else if(f.type==="textarea"||f.type==="lines"){inp='<textarea id="'+id+'" rows="'+(f.rows||4)+'"'+(f.placeholder?' placeholder="'+esc(f.placeholder)+'"':'')+'></textarea>';tall=" tall";}
  else if(f.type==="select"){var o="";for(var i=0;i<f.options.length;i++)o+='<option>'+esc(f.options[i])+'</option>';inp='<select id="'+id+'">'+o+'</select>';}
  else if(f.type==="multiselect"){var c="";for(var i=0;i<f.options.length;i++){var ov=f.options[i];c+='<label class="chip"><input type="checkbox" value="'+esc(ov)+'">'+esc(ov)+'</label>';}inp='<div class="chips" id="'+id+'">'+c+'</div>';tall=" tall";}
  else inp='<input type="'+(f.type==="password"?"password":(f.type==="number"?"number":"text"))+'" id="'+id+'"'+(f.placeholder?' placeholder="'+esc(f.placeholder)+'"':'')+(f.step?' step="'+f.step+'"':'')+(f.tip?' title="'+esc(f.tip)+'"':'')+'>';
  var ttl=f.tip?' title="'+esc(f.tip)+'"':'';
  return '<div class="field'+tall+'" id="fw_'+f.id+'"'+ttl+'><label>'+esc(f.label)+sub+'</label>'+inp+'</div>';
}
function applyShowIf(fields){var vals=collectForm(fields);
  for(var i=0;i<fields.length;i++){var f=fields[i];if(f.type==="section"||!f.showIf)continue;var w=el("fw_"+f.id);if(w)w.style.display=f.showIf(vals)?"":"none";}}
function setFields(fields,vals){fields.forEach(function(f){var e=el("f_"+f.id);if(!e)return;var v=vals?vals[f.id]:undefined;
  if(f.type==="checkbox")e.checked=!!v;
  else if(f.type==="multiselect"){var arr=v||[];var bs=e.querySelectorAll("input[type=checkbox]");for(var j=0;j<bs.length;j++)bs[j].checked=arr.indexOf(bs[j].value)>=0;return;}
  else if(f.type==="csv"||f.type==="words")e.value=(v||[]).join(" ");
  else if(f.type==="lines")e.value=(v||[]).join("\n");
  else e.value=(v==null?"":v);
  if(f.disabled)e.disabled=true;});}
function collectForm(fields){var o={};fields.forEach(function(f){var e=el("f_"+f.id);if(!e)return;
  if(f.type==="checkbox")o[f.id]=e.checked;
  else if(f.type==="multiselect"){var out=[];var bs=e.querySelectorAll("input[type=checkbox]:checked");for(var j=0;j<bs.length;j++)out.push(bs[j].value);o[f.id]=out;}
  else if(f.type==="number"){var n=(""+e.value).trim();o[f.id]=(n===""?(f.nullable?null:0):parseFloat(n));}
  else if(f.type==="csv")o[f.id]=e.value.split(/[\s,]+/).map(trim).filter(Boolean);
  else if(f.type==="words")o[f.id]=e.value.split(/\s+/).map(trim).filter(Boolean);
  else if(f.type==="lines")o[f.id]=e.value.split(/\r?\n/).map(trim).filter(Boolean);
  else o[f.id]=trim(e.value);});return o;}
function formModal(title,fields,values,onSave,size){
  var h="";for(var i=0;i<fields.length;i++)h+=fieldHtml(fields[i]);
  openModal(title,h,[{label:t("act.cancel"),onClick:closeModal},{label:t("act.save"),primary:true,onClick:function(){onSave(collectForm(fields));}}],size||true);
  setFields(fields,values);
  fields.forEach(function(f){if(f.type==="section")return;var e=el("f_"+f.id);if(e)e.addEventListener("change",function(){applyShowIf(fields);});});
  applyShowIf(fields);
  var first=el("modalBody").querySelector("input:not([disabled]),select:not([disabled]),textarea:not([disabled])");if(first)first.focus();
}
/* sortable + filterable + paginated data grid with a column picker. State persists per container across
   data reloads. Rows keep their original index so row-click edit and the row menu target the right item. */
var _grid={};
function gridState(cid){if(!_grid[cid])_grid[cid]={cols:[],rows:[],empty:null,sortCol:-1,sortDir:0,filters:{},hidden:{},page:1,pageSize:25,setup:false};return _grid[cid];}
function cellText(html){return (""+html).replace(/<[^>]*>/g,"").replace(/&amp;/g,"&").replace(/&lt;/g,"<").replace(/&gt;/g,">").replace(/&#39;/g,"'").replace(/&quot;/g,'"').trim();}
function gridItems(st){
  var items=[],i;for(i=0;i<st.rows.length;i++)items.push({r:st.rows[i],i:i});
  items=items.filter(function(it){for(var c=0;c<st.cols.length;c++){var f=(st.filters[c]||"").toLowerCase();if(!f)continue;if(cellText(st.cols[c].get(it.r,it.i)).toLowerCase().indexOf(f)<0)return false;}return true;});
  if(st.sortCol>=0&&st.sortDir!==0){var sc=st.sortCol,sd=st.sortDir;
    items.sort(function(a,b){var ta=cellText(st.cols[sc].get(a.r,a.i)),tb=cellText(st.cols[sc].get(b.r,b.i));
      var na=parseFloat(ta),nb=parseFloat(tb),cmp;
      if(!isNaN(na)&&!isNaN(nb)&&(""+na)===ta&&(""+nb)===tb)cmp=na-nb;else cmp=ta.localeCompare(tb,undefined,{numeric:true,sensitivity:"base"});
      return cmp*sd;});}
  return items;
}
function visCols(st){var v=[],c;for(c=0;c<st.cols.length;c++)if(c===0||!st.hidden[c])v.push(c);return v;}
function gridBody(st,vis){
  var items=gridItems(st),pages=Math.max(1,Math.ceil(items.length/st.pageSize));if(st.page>pages)st.page=pages;if(st.page<1)st.page=1;
  var slice=items.slice((st.page-1)*st.pageSize,st.page*st.pageSize);
  if(!items.length)return '<tr><td colspan="'+(vis.length+1)+'" class="norows">'+t("tbl.norows")+'</td></tr>';
  var h="",k,ci;for(k=0;k<slice.length;k++){var it=slice[k];h+='<tr data-row="'+it.i+'">';
    for(ci=0;ci<vis.length;ci++){var c=vis[ci];h+='<td'+(st.cols[c].mono?' class="mono"':'')+'>'+st.cols[c].get(it.r,it.i)+'</td>';}
    h+='<td class="actcell"><button class="rowmenu-btn" data-menu="'+it.i+'" title="Open the actions menu for this row (edit, duplicate, view JSON, delete)">&#8942;</button></td></tr>';}
  return h;
}
function gridBar(st,total){var pages=Math.max(1,Math.ceil(total/st.pageSize)),a=total?((st.page-1)*st.pageSize+1):0,b=Math.min(st.page*st.pageSize,total);
  var sizes=[10,25,50,100].map(function(n){return '<option'+(n===st.pageSize?' selected':'')+'>'+n+'</option>';}).join("");
  return '<span class="count">'+t("tbl.showing",{a:a,b:b,n:total})+'</span><span class="spacer"></span>'
    +'<label title="How many rows to show per page">'+t("tbl.rows")+' <select class="pgsize">'+sizes+'</select></label>'
    +'<button class="colbtn" data-cols title="Choose which columns are visible">'+t("tbl.columns")+'</button>'
    +'<span class="pager"><button class="pgbtn" data-pg="first" title="First page"'+(st.page<=1?" disabled":"")+'>«</button>'
    +'<button class="pgbtn" data-pg="prev" title="Previous page"'+(st.page<=1?" disabled":"")+'>‹</button>'
    +'<span style="padding:0 6px" title="Current page">'+st.page+' / '+pages+'</span>'
    +'<button class="pgbtn" data-pg="next" title="Next page"'+(st.page>=pages?" disabled":"")+'>›</button>'
    +'<button class="pgbtn" data-pg="last" title="Last page"'+(st.page>=pages?" disabled":"")+'>»</button></span>';
}
function gridDraw(cid){var st=gridState(cid),box=el(cid),vi,c;
  if(!st.rows.length){box.innerHTML='<div class="empty"><div class="eicon">'+((st.empty&&st.empty.icon)||"📭")+'</div><div>'+esc((st.empty&&st.empty.msg)||"Nothing configured yet.")+'</div>'+((st.empty&&st.empty.add)?'<button class="btn" data-add="1">'+esc(st.empty.add)+'</button>':'')+'</div>';return;}
  var vis=visCols(st),items=gridItems(st);
  var h='<div class="gridbar" id="'+cid+'_bar">'+gridBar(st,items.length)+'</div><div class="tablewrap"><table class="grid"><thead><tr class="hrow">';
  for(vi=0;vi<vis.length;vi++){c=vis[vi];var col=st.cols[c],arr=(st.sortCol===c)?(st.sortDir>0?"▲":(st.sortDir<0?"▼":"")):"";
    h+='<th scope="col" class="sortable"'+(col.w?' style="width:'+col.w+'"':'')+' data-sort="'+c+'"'+(st.sortCol===c?' aria-sort="'+(st.sortDir>0?"ascending":"descending")+'"':'')+' title="'+esc(col.tip||("Sort rows by "+col.h))+'">'+esc(col.h)+' <span class="sarrow">'+arr+'</span></th>';}
  h+='<th class="actcell" scope="col"></th></tr><tr class="frow">';
  for(vi=0;vi<vis.length;vi++){c=vis[vi];h+='<th><input class="fbox" data-fcol="'+c+'" placeholder="'+t("tbl.filter")+'" title="Type here to show only rows whose '+esc(st.cols[c].h)+' contains this text" value="'+esc(st.filters[c]||"")+'"></th>';}
  h+='<th class="actcell"></th></tr></thead><tbody id="'+cid+'_body">'+gridBody(st,vis)+'</tbody></table></div>';
  box.innerHTML=h;
}
function gridRefresh(cid){var st=gridState(cid),vis=visCols(st),items=gridItems(st);
  var bar=el(cid+"_bar");if(bar)bar.innerHTML=gridBar(st,items.length);
  var body=el(cid+"_body");if(body)body.innerHTML=gridBody(st,vis);}
function colMenu(cid){var st=gridState(cid),items=[];for(var c=1;c<st.cols.length;c++){(function(c){items.push({label:st.cols[c].h,check:!st.hidden[c],keepOpen:true,run:function(){st.hidden[c]=!st.hidden[c];try{localStorage.setItem("mux.cols."+cid,JSON.stringify(st.hidden));}catch(e){}gridDraw(cid);}});})(c);}return items;}
function gridSetup(cid){var st=gridState(cid);if(st.setup)return;st.setup=true;var box=el(cid);
  try{var sv=JSON.parse(localStorage.getItem("mux.cols."+cid)||"null");if(sv)st.hidden=sv;}catch(e){}
  box.addEventListener("click",function(e){
    var th=e.target.closest?e.target.closest("th[data-sort]"):null;
    if(th){var c=parseInt(th.getAttribute("data-sort"),10);if(st.sortCol===c)st.sortDir=(st.sortDir>0?-1:(st.sortDir<0?0:1));else{st.sortCol=c;st.sortDir=1;}if(st.sortDir===0)st.sortCol=-1;gridDraw(cid);return;}
    var pg=e.target.closest?e.target.closest("[data-pg]"):null;
    if(pg){var pages=Math.max(1,Math.ceil(gridItems(st).length/st.pageSize)),a=pg.getAttribute("data-pg");if(a==="first")st.page=1;else if(a==="prev")st.page=Math.max(1,st.page-1);else if(a==="next")st.page=Math.min(pages,st.page+1);else st.page=pages;gridRefresh(cid);return;}
    var cb=e.target.closest?e.target.closest("[data-cols]"):null;
    if(cb){e.stopPropagation();openMenu(cb,colMenu(cid));return;}
  });
  box.addEventListener("input",function(e){var f=e.target.closest?e.target.closest(".fbox"):null;if(f){st.filters[parseInt(f.getAttribute("data-fcol"),10)]=f.value;st.page=1;gridRefresh(cid);}});
  box.addEventListener("change",function(e){var s=e.target.closest?e.target.closest(".pgsize"):null;if(s){st.pageSize=parseInt(s.value,10)||25;st.page=1;gridRefresh(cid);}});
}
function renderGrid(cid,cols,rows,empty){var st=gridState(cid);st.cols=cols;st.rows=rows;st.empty=empty;gridSetup(cid);gridDraw(cid);}
function gridLoading(cid){var b=el(cid);if(b)b.innerHTML='<div class="empty"><div class="spinner"></div><div>'+t("tbl.loading")+'</div></div>';}
function gridError(cid,msg){var b=el(cid);if(b)b.innerHTML='<div class="empty"><div class="eicon">⚠️</div><div>'+esc(msg||t("tbl.failed"))+'</div><button class="btn" data-retry="'+cid+'">'+t("act.retry")+'</button></div>';}
var _reload={};
function fallbackCopy(t){var ta=document.createElement("textarea");ta.value=t;ta.style.position="fixed";ta.style.opacity="0";document.body.appendChild(ta);ta.select();try{document.execCommand("copy");}catch(e){}document.body.removeChild(ta);}
// The single shared copy helper used everywhere in the dashboard. Copies text (navigator.clipboard on a
// secure context, execCommand fallback on plain http / non-localhost) and briefly flips the button to a
// green checkmark (via the .ok class) instead of a toast, restoring its original content afterward.
function copyText(t,btn){var done=function(){if(btn){if(btn.getAttribute("data-flashing"))return;btn.setAttribute("data-orig",btn.innerHTML);btn.setAttribute("data-flashing","1");btn.innerHTML="✓";btn.classList.add("ok");setTimeout(function(){btn.innerHTML=btn.getAttribute("data-orig")||btn.innerHTML;btn.classList.remove("ok");btn.removeAttribute("data-flashing");btn.removeAttribute("data-orig");},1200);}else toast(t("toast.copied"));};
  if(navigator.clipboard&&navigator.clipboard.writeText&&window.isSecureContext){navigator.clipboard.writeText(t).then(done).catch(function(){fallbackCopy(t);done();});}else{fallbackCopy(t);done();}}
function viewJson(title,obj){var txt=JSON.stringify(obj,null,2);openModal(title,'<pre style="white-space:pre-wrap;margin:0;max-height:60vh">'+esc(txt)+'</pre>',[{label:t("act.copyjson"),onClick:function(){copyText(txt,this);}},{label:t("act.close"),primary:true,onClick:closeModal}],true);}
function dupOf(o,idf){var c=JSON.parse(JSON.stringify(o));if(idf)delete c[idf];c.IsDefault=false;delete c.ApiKey;delete c.ApiKeySet;delete c.AuthSecret;delete c.AuthSecretSet;return c;}
function busyModal(on){var f=el("modalFoot");if(!f)return;var bs=f.querySelectorAll("button");for(var i=0;i<bs.length;i++)bs[i].disabled=on;}
function fmtWhen(iso){if(!iso)return "";var d=new Date(iso);if(isNaN(d))return (""+iso);var s=Math.floor((Date.now()-d.getTime())/1000);var rel;if(s<60)rel="just now";else if(s<3600)rel=Math.floor(s/60)+"m ago";else if(s<86400)rel=Math.floor(s/3600)+"h ago";else rel=Math.floor(s/86400)+"d ago";return '<span title="'+esc(d.toISOString().replace("T"," ").slice(0,19)+" UTC")+'">'+esc(rel)+'</span>';}
/* ---- floating context menu (overlays table, clamped to viewport) ---- */
var _menuEl=null;
function closeMenu(){if(_menuEl){var b=_menuEl._anchor;if(b)b.classList.remove("open");_menuEl.remove();_menuEl=null;}}
function openMenu(anchor,items){
  closeMenu();
  var m=document.createElement("div");m.className="ctxmenu";m._anchor=anchor;anchor.classList.add("open");
  items.forEach(function(it){
    if(it.sep){var s=document.createElement("div");s.className="ctxsep";m.appendChild(s);return;}
    var b=document.createElement("button");
    b.className="ctxitem"+(it.danger?" danger":"")+(it.check!==undefined?" chk"+(it.check?" on":""):"");b.textContent=it.label;
    b.addEventListener("click",function(ev){ev.stopPropagation();
      if(it.keepOpen){it.run();b.classList.toggle("on");}else{closeMenu();it.run();}});
    m.appendChild(b);});
  m.style.visibility="hidden";document.body.appendChild(m);
  var r=anchor.getBoundingClientRect(),mw=m.offsetWidth,mh=m.offsetHeight,vw=window.innerWidth,vh=window.innerHeight,pad=8;
  var top=r.bottom+4;if(top+mh>vh-pad)top=Math.max(pad,r.top-mh-4);
  var left=r.right-mw;if(left+mw>vw-pad)left=vw-pad-mw;if(left<pad)left=pad;
  m.style.top=top+"px";m.style.left=left+"px";m.style.visibility="";
  _menuEl=m;
}
function wireTable(id,opts){
  if(opts.row)el(id).classList.add("rowclick");
  el(id).addEventListener("click",function(e){
    var mb=e.target.closest?e.target.closest("[data-menu]"):null;
    if(mb){e.stopPropagation();openMenu(mb,opts.menu(parseInt(mb.getAttribute("data-menu"),10)));return;}
    var ab=e.target.closest?e.target.closest("[data-add]"):null;
    if(ab){if(opts.add)opts.add();return;}
    if(opts.row){var tr=e.target.closest?e.target.closest("tr[data-row]"):null;if(tr)opts.row(parseInt(tr.getAttribute("data-row"),10));}
  });
}
function saveCollection(path,list,after){busyModal(true);api(path,"PUT",{Items:list}).then(function(r){after((r&&r.Items)||[]);toast(t("toast.saved"));}).catch(function(e){toast(e.message,true);}).finally(function(){busyModal(false);});}

/* ================= Endpoints ================= */
var _ep=[];
function loadEndpointsAdmin(){_reload["endpoints_list"]=loadEndpointsAdmin;gridLoading("endpoints_list");api("/v1.0/api/endpoints/detail").then(function(r){_ep=(r&&r.Items)||[];renderEp();}).catch(function(e){gridError("endpoints_list",e.message);});}
function renderEp(){renderGrid("endpoints_list",
  [{h:t("col.name"),get:function(e){return esc(e.Name)+(e.IsDefault?' <span class="tag on">'+t("tag.default")+'</span>':'')}},
   {h:t("col.adapter"),get:function(e){return esc(e.AdapterType)}},
   {h:t("col.model"),get:function(e){return esc(e.Model||"")}},
   {h:t("col.auth"),w:"60px",tip:"Whether an API key is stored for this endpoint (never shown)",get:function(e){return e.ApiKeySet?'🔑':'<span class="muted">—</span>'}}],
  _ep,{icon:"🔌",msg:t("empty.endpoints"),add:"+ "+t("add.endpoint")});}
function epUsesKey(v){return v.AdapterType==="anthropic"||v.AdapterType==="gemini"||v.AdapterType==="azure-openai";}
function epFields(isEdit){return [
  {type:"section",label:"Connection"},
  {id:"Name",label:"Name",disabled:isEdit,tip:"A unique name you use to select this endpoint (e.g. with --endpoint). Cannot be changed after creation."},
  {id:"AdapterType",label:"Adapter type",type:"select",options:["ollama","openai","openai-compatible","vllm","anthropic","gemini","azure-openai","vertex","bedrock"],tip:"Which backend protocol to speak. Determines the auth fields shown below and how the base URL is used."},
  {id:"BaseUrl",label:"Base URL",placeholder:"http://localhost:11434",tip:"The API root of the model server. Blank uses the provider's public API (for hosted adapters); required for Azure."},
  {type:"section",label:"Model & sampling"},
  {id:"Model",label:"Model",sub:"(Azure: deployment)",tip:"The model identifier sent to the backend. For Azure OpenAI this is the deployment name, not the model name."},
  {id:"MaxTokens",label:"Max tokens",type:"number",tip:"Upper bound on tokens the model may generate in a single response."},
  {id:"Temperature",label:"Temperature",type:"number",step:"0.1",tip:"Sampling randomness. Lower is more deterministic and focused; higher is more varied and creative."},
  {id:"ContextWindow",label:"Context window",type:"number",tip:"The model's total context size in tokens. mux uses this to decide when to compact conversation history."},
  {type:"section",label:"Behavior"},
  {id:"IsDefault",label:"Default endpoint",type:"checkbox",tip:"Use this endpoint automatically when none is specified. Only one endpoint can be the default."},
  {id:"TimeoutMs",label:"Timeout (ms)",type:"number",tip:"How long to wait for the backend to respond before failing the request, in milliseconds."},
  {id:"AutoApproveTools",label:"Auto-approve tools",type:"checkbox",tip:"When active, tool calls run without an approval prompt while this endpoint is selected (unless CLI flags override)."},
  {id:"ShowThinking",label:"Show thinking",type:"checkbox",tip:"Display the model's reasoning ('thinking') output separately from its answer, when the model produces it."},
  {id:"MaxAgentIterations",label:"Max agent iterations",sub:"(blank = global)",type:"number",nullable:true,tip:"Cap on model turns per run for this endpoint. Leave blank to inherit the global setting."},
  {type:"section",label:"Authentication"},
  {id:"ApiKey",label:"API key",type:"password",placeholder:"(unchanged)",showIf:epUsesKey,tip:"Secret key for this provider. Never shown; leave blank to keep the stored key unchanged."},
  {id:"Region",label:"Region",sub:"(cloud region)",showIf:function(v){return v.AdapterType==="vertex"||v.AdapterType==="bedrock";},tip:"Cloud region hosting the model, e.g. us-central1 (Vertex) or us-east-1 (Bedrock)."},
  {id:"Project",label:"Project",sub:"(GCP project id)",showIf:function(v){return v.AdapterType==="vertex";},tip:"The Google Cloud project id that owns the Vertex AI resources."},
  {id:"ApiVersion",label:"API version",showIf:function(v){return v.AdapterType==="azure-openai";},tip:"The Azure OpenAI api-version query value, e.g. 2024-10-21."}];}
function openEp(i,prefill){var isEdit=i>=0,e=isEdit?_ep[i]:(prefill||{AdapterType:"ollama",MaxTokens:8192,Temperature:0.1,ContextWindow:32768,TimeoutMs:120000});
  formModal(isEdit?"Edit endpoint":"Add endpoint",epFields(isEdit),e,function(v){
    if(!v.Name){toast(t("toast.nameReq"),true);return;}
    v.Headers=(isEdit&&_ep[i].Headers)?_ep[i].Headers:[];
    var list=_ep.slice();if(isEdit)list[i]=v;else list.push(v);
    saveCollection("/v1.0/api/endpoints",list,function(items){_ep=items;closeModal();renderEp();loadEndpoints();});});}
function delEp(name){confirmModal('Delete endpoint "'+name+'"?',function(){
  api("/v1.0/api/endpoints?name="+encodeURIComponent(name),"DELETE").then(function(r){_ep=(r&&r.Items)||[];renderEp();toast("Deleted");loadEndpoints();}).catch(function(e){toast(e.message,true);});});}

/* ================= MCP servers ================= */
var _mcp=[];
function loadMcp(){_reload["mcp_list"]=loadMcp;gridLoading("mcp_list");api("/v1.0/api/mcp-servers").then(function(r){_mcp=(r&&r.Items)||[];renderMcp();}).catch(function(e){gridError("mcp_list",e.message);});}
function renderMcp(){renderGrid("mcp_list",
  [{h:t("col.name"),get:function(s){return esc(s.Name)}},{h:t("col.transport"),get:function(s){return esc(s.Transport)}},
   {h:t("col.target"),mono:true,tip:"The launch command (stdio) or URL (http) mux connects to",get:function(s){return esc(s.Transport==="http"?(s.Url||""):(s.Command||"")+" "+((s.Args||[]).join(" ")))}},
   {h:t("col.auth"),w:"70px",tip:"The authentication scheme used to reach this MCP server",get:function(s){return s.AuthType==="none"?'<span class="muted">—</span>':esc(s.AuthType)}}],
  _mcp,{icon:"🧩",msg:t("empty.mcp"),add:"+ "+t("add.server")});}
function mcpFields(isEdit){return [
  {id:"Name",label:"Name",disabled:isEdit,tip:"A unique name for this MCP server. Cannot be changed after creation."},
  {id:"Transport",label:"Transport",type:"select",options:["stdio","http"],tip:"How mux connects: 'stdio' launches a local process; 'http' connects to a running HTTP MCP server."},
  {id:"Command",label:"Command",showIf:function(v){return v.Transport==="stdio";},tip:"The executable to launch for a stdio server (e.g. npx, python, or a binary path)."},
  {id:"Args",label:"Args",sub:"(space-sep)",type:"words",showIf:function(v){return v.Transport==="stdio";},tip:"Command-line arguments passed to the stdio command, separated by spaces."},
  {id:"Env",label:"Env",sub:"(KEY=VALUE per line)",type:"lines",rows:3,showIf:function(v){return v.Transport==="stdio";},tip:"Environment variables for the stdio process, one KEY=VALUE per line. Supports ${VAR} expansion."},
  {id:"Url",label:"URL",placeholder:"https://host/mcp",showIf:function(v){return v.Transport==="http";},tip:"The base URL of the HTTP MCP server to connect to."},
  {id:"McpPath",label:"MCP path",placeholder:"/mcp",showIf:function(v){return v.Transport==="http";},tip:"The streamable-HTTP MCP endpoint path on the server, usually /mcp."},
  {id:"AuthType",label:"Auth",type:"select",options:["none","bearer","apikey"],tip:"How to authenticate to an HTTP MCP server: none, a bearer token, or an API key in a custom header."},
  {id:"AuthHeader",label:"Auth header",placeholder:"X-API-Key",showIf:function(v){return v.AuthType==="apikey";},tip:"The header name the API key is sent in when using apikey auth."},
  {id:"AuthSecret",label:"Auth secret",type:"password",placeholder:"(unchanged)",showIf:function(v){return v.AuthType!=="none";},tip:"The bearer token or API-key value. Never shown; leave blank to keep the stored secret."}];}
function openMcp(i,prefill){var isEdit=i>=0,s=isEdit?_mcp[i]:(prefill||{Transport:"stdio",AuthType:"none",McpPath:"/mcp"});
  formModal(isEdit?"Edit MCP server":"Add MCP server",mcpFields(isEdit),s,function(v){
    if(!v.Name){toast(t("toast.nameReq"),true);return;}
    var list=_mcp.slice();if(isEdit)list[i]=v;else list.push(v);
    saveCollection("/v1.0/api/mcp-servers",list,function(items){_mcp=items;closeModal();renderMcp();});});}
function delMcp(name){confirmModal('Delete MCP server "'+name+'"?',function(){
  api("/v1.0/api/mcp-servers?name="+encodeURIComponent(name),"DELETE").then(function(r){_mcp=(r&&r.Items)||[];renderMcp();toast("Deleted");}).catch(function(e){toast(e.message,true);});});}

/* ================= Prompts ================= */
var _pr=[];
function loadPrompts(){_reload["prompts_list"]=loadPrompts;gridLoading("prompts_list");api("/v1.0/api/prompts").then(function(r){_pr=(r&&r.Items)||[];renderPr();}).catch(function(e){gridError("prompts_list",e.message);});}
function renderPr(){renderGrid("prompts_list",
  [{h:t("col.name"),get:function(p){return esc(p.Name)+(p.IsActive?' <span class="tag on">'+t("tag.active")+'</span>':'')}},
   {h:t("col.systemPrompt"),get:function(p){return esc((p.SystemPrompt||"").slice(0,80)||"(inherits default)")}}],
  _pr,{icon:"📝",msg:t("empty.prompts"),add:"+ "+t("add.profile")});}
function prFields(isEdit){return [{id:"Name",label:"Name",disabled:isEdit,tip:"A name for this prompt profile. Cannot be changed after creation."},{id:"IsActive",label:"Active",type:"checkbox",tip:"Make this the profile mux uses. Only one profile is active at a time."},
  {id:"SystemPrompt",label:"System prompt",sub:"(blank inherits)",type:"textarea",rows:16,tip:"Overrides the built-in system prompt for this profile. Leave blank to inherit the default prompt."}];}
function openPr(i,prefill){var isEdit=i>=0,p=isEdit?_pr[i]:(prefill||{IsActive:false});
  formModal(isEdit?"Edit prompt profile":"Add prompt profile",prFields(isEdit),p,function(v){
    if(!v.Name){toast(t("toast.nameReq"),true);return;}
    var list=_pr.slice();if(isEdit)list[i]=v;else list.push(v);
    saveCollection("/v1.0/api/prompts",list,function(items){_pr=items;closeModal();renderPr();});},"xl");}
function delPr(i){var p=_pr[parseInt(i,10)];confirmModal('Delete prompt profile "'+(p?p.Name:"")+'"?',function(){
  var list=_pr.filter(function(x,ix){return ix!==parseInt(i,10);});
  saveCollection("/v1.0/api/prompts",list,function(items){_pr=items;renderPr();});});}

/* ================= Subagents ================= */
var _sa=[];
function loadSubagents(){_reload["subagents_list"]=loadSubagents;gridLoading("subagents_list");api("/v1.0/api/subagents").then(function(r){_sa=(r&&r.Items)||[];renderSa();}).catch(function(e){gridError("subagents_list",e.message);});}
function renderSa(){renderGrid("subagents_list",
  [{h:t("col.name"),get:function(s){return esc(s.Name)}},{h:t("col.description"),get:function(s){return esc(s.Description||"")}},
   {h:t("col.tools"),mono:true,tip:"The tools this subagent is limited to (or inherit)",get:function(s){return esc((s.AllowedTools||[]).join(", ")||"(inherit)")}}],
  _sa,{icon:"🤖",msg:t("empty.subagents"),add:"+ "+t("add.subagent")});}
var TOOL_NAMES=["read_file","write_file","edit_file","multi_edit","delete_file","file_metadata","list_directory","manage_directory","glob","grep","run_process","web_retrieve","web_search"];
function saFields(isEdit){return [{id:"Name",label:"Name",disabled:isEdit,tip:"The name the model uses to delegate to this subagent via spawn_subagent. Cannot be changed after creation."},{id:"Description",label:"Description",tip:"A short summary of what this subagent does, shown to the model so it can pick the right one."},
  {id:"SystemPrompt",label:"System prompt",type:"textarea",rows:14,tip:"The instructions that define this subagent's behavior. It fully replaces the parent's system prompt for the isolated run."},
  {id:"EndpointName",label:"Endpoint",sub:"(blank inherits)",tip:"Run this subagent on a specific endpoint (e.g. a cheaper model). Blank inherits the parent's endpoint."},
  {id:"AllowedTools",label:"Allowed tools",sub:"(none selected = inherit all)",type:"multiselect",options:TOOL_NAMES,tip:"Restrict the subagent to these tools. Select none to let it use the same tools as the parent."},
  {id:"MaxIterations",label:"Max iterations",sub:"(blank inherits)",type:"number",nullable:true,tip:"Cap the subagent's model turns to keep a delegated task bounded. Blank inherits the parent's cap."}];}
function openSa(i,prefill){var isEdit=i>=0,s=isEdit?_sa[i]:(prefill||{});
  formModal(isEdit?"Edit subagent":"Add subagent",saFields(isEdit),s,function(v){
    if(!v.Name){toast(t("toast.nameReq"),true);return;}
    var list=_sa.slice();if(isEdit)list[i]=v;else list.push(v);
    saveCollection("/v1.0/api/subagents",list,function(items){_sa=items;closeModal();renderSa();});},"xl");}
function delSa(i){var s=_sa[parseInt(i,10)];confirmModal('Delete subagent "'+(s?s.Name:"")+'"?',function(){
  var list=_sa.filter(function(x,ix){return ix!==parseInt(i,10);});
  saveCollection("/v1.0/api/subagents",list,function(items){_sa=items;renderSa();});});}

/* ================= Hooks + custom commands ================= */
var _hooks={Hooks:[],Commands:[]};
function loadHooks(){_reload["hooks_list"]=loadHooks;_reload["cmds_list"]=loadHooks;gridLoading("hooks_list");gridLoading("cmds_list");api("/v1.0/api/hooks").then(function(r){_hooks={Hooks:(r&&r.Hooks)||[],Commands:(r&&r.Commands)||[]};renderHooks();}).catch(function(e){gridError("hooks_list",e.message);gridError("cmds_list",e.message);});}
function saveHooks(after){busyModal(true);api("/v1.0/api/hooks","PUT",_hooks).then(function(r){_hooks={Hooks:(r&&r.Hooks)||[],Commands:(r&&r.Commands)||[]};if(after)after();toast(t("toast.saved"));}).catch(function(e){toast(e.message,true);}).finally(function(){busyModal(false);});}
function renderHooks(){
  renderGrid("hooks_list",
    [{h:t("col.event"),get:function(h){return esc(h.Event)}},{h:t("col.command"),mono:true,get:function(h){return esc((h.Command||"")+" "+((h.Args||[]).join(" ")))}},
     {h:t("col.blocking"),w:"80px",tip:"Whether a non-zero exit vetoes the prompt (user-prompt-submit only)",get:function(h){return h.Blocking?'<span class="tag on">'+t("tag.yes")+'</span>':'<span class="muted">'+t("tag.no")+'</span>'}}],
    _hooks.Hooks,{icon:"🪝",msg:t("empty.hooks"),add:"+ "+t("add.hook")});
  renderGrid("cmds_list",
    [{h:t("col.name"),get:function(c){return "/"+esc(c.Name)}},{h:t("col.command"),mono:true,get:function(c){return esc((c.Command||"")+" "+((c.Args||[]).join(" ")))}},
     {h:t("col.description"),get:function(c){return esc(c.Description||"")}}],
    _hooks.Commands,{icon:"⚡",msg:t("empty.commands"),add:"+ "+t("add.command")});
}
function hookFields(){return [{id:"Name",label:"Name",sub:"(optional)",tip:"An optional label for this hook, shown in listings and notices."},
  {id:"Event",label:"Event",type:"select",options:["session-start","user-prompt-submit","session-end"],tip:"When the hook runs: at session start, when a prompt is submitted (vetoable), or at session end."},
  {id:"Command",label:"Command",tip:"The executable to run out-of-process when the event fires. The event payload is sent to it on stdin."},{id:"Args",label:"Args",sub:"(space-sep)",type:"words",tip:"Arguments passed to the command, separated by spaces."},
  {id:"Blocking",label:"Blocking (veto)",type:"checkbox",tip:"On user-prompt-submit, a non-zero exit from a blocking hook cancels the prompt. Ignored for other events."},{id:"TimeoutMs",label:"Timeout (ms)",type:"number",tip:"How long the hook may run before it is killed, in milliseconds."}];}
function openHook(i,prefill){var isEdit=i>=0,h=isEdit?_hooks.Hooks[i]:(prefill||{Event:"session-start",TimeoutMs:15000});
  formModal(isEdit?"Edit hook":"Add hook",hookFields(),h,function(v){if(!v.Command){toast("Command is required",true);return;}
    if(isEdit)_hooks.Hooks[i]=v;else _hooks.Hooks.push(v);saveHooks(function(){closeModal();renderHooks();});});}
function delHook(i){confirmModal("Delete this hook?",function(){_hooks.Hooks.splice(parseInt(i,10),1);saveHooks(renderHooks);});}
function cmdFields(){return [{id:"Name",label:"Name",sub:"(no slash)",tip:"The slash-command name, without the leading slash. Invoked in the shell as /name."},{id:"Description",label:"Description",tip:"A short description shown next to the command in the interactive menu."},
  {id:"Command",label:"Command",tip:"The executable to run out-of-process when the command is invoked. Its output is posted into the transcript."},{id:"Args",label:"Args",sub:"(space-sep)",type:"words",tip:"Arguments passed to the command, separated by spaces."},{id:"TimeoutMs",label:"Timeout (ms)",type:"number",tip:"How long the command may run before it is killed, in milliseconds."}];}
function openCmd(i,prefill){var isEdit=i>=0,c=isEdit?_hooks.Commands[i]:(prefill||{TimeoutMs:30000});
  formModal(isEdit?"Edit command":"Add command",cmdFields(),c,function(v){if(!v.Name||!v.Command){toast("Name and command are required",true);return;}
    if(isEdit)_hooks.Commands[i]=v;else _hooks.Commands.push(v);saveHooks(function(){closeModal();renderHooks();});});}
function delCmd(i){confirmModal("Delete this command?",function(){_hooks.Commands.splice(parseInt(i,10),1);saveHooks(renderHooks);});}

/* ================= Keybindings ================= */
var _kb=[];
function loadKeybindings(){_reload["keybindings_list"]=loadKeybindings;gridLoading("keybindings_list");api("/v1.0/api/keybindings").then(function(r){_kb=(r&&r.Items)||[];renderKb();}).catch(function(e){gridError("keybindings_list",e.message);});}
function renderKb(){renderGrid("keybindings_list",
  [{h:t("col.commandId"),mono:true,get:function(k){return esc(k.CommandId)}},{h:t("col.chord"),tip:"The key combination bound to the command (blank means unbound)",get:function(k){return k.Chord?esc(k.Chord):'<span class="muted">(unbound)</span>'}}],
  _kb,{icon:"⌨️",msg:t("empty.keybindings"),add:"+ "+t("add.binding")});}
var COMMAND_IDS=["mux.quit","mux.endpoint","mux.clear","mux.sidebar.toggle","mux.save","mux.export","mux.undo","mux.redo","mux.queue","mux.prompts","mux.mcp","mux.skills","mux.sessions","mux.tasks","mux.effort","mux.settings","mux.theme","mux.mouse","mux.borders","mux.thinking","mux.menu","mux.help"];
var KEY_NAMES=(function(){var a=["(none)"],i;for(i=97;i<=122;i++)a.push(String.fromCharCode(i));for(i=0;i<=9;i++)a.push(""+i);for(i=1;i<=12;i++)a.push("f"+i);["enter","escape","space","tab","backspace","delete","insert","home","end","pageup","pagedown","up","down","left","right"].forEach(function(k){a.push(k);});return a;})();
function parseChord(ch){var r={Ctrl:false,Alt:false,Shift:false,Key:"(none)"};if(!ch)return r;(""+ch).toLowerCase().split("+").forEach(function(p){p=p.trim();if(p==="ctrl"||p==="control")r.Ctrl=true;else if(p==="alt")r.Alt=true;else if(p==="shift")r.Shift=true;else if(p)r.Key=p;});return r;}
function buildChord(v){var parts=[];if(v.Ctrl)parts.push("ctrl");if(v.Alt)parts.push("alt");if(v.Shift)parts.push("shift");if(v.Key&&v.Key!=="(none)")parts.push(v.Key);return parts.join("+");}
function kbFields(isEdit,curId){var opts=COMMAND_IDS.slice();if(curId&&opts.indexOf(curId)<0)opts.unshift(curId);return [
  {id:"CommandId",label:"Command",type:"select",options:opts,disabled:isEdit,tip:"The mux command whose keyboard shortcut you are changing."},
  {id:"Ctrl",label:"Ctrl",type:"checkbox",tip:"Hold the Control key as part of the shortcut."},
  {id:"Alt",label:"Alt",type:"checkbox",tip:"Hold the Alt (Option) key as part of the shortcut."},
  {id:"Shift",label:"Shift",type:"checkbox",tip:"Hold the Shift key as part of the shortcut."},
  {id:"Key",label:"Key",type:"select",options:KEY_NAMES,tip:"The main key of the shortcut. Choose (none) to leave the command unbound."}];}
function openKb(i,prefill){var isEdit=i>=0,k=isEdit?_kb[i]:(prefill||{CommandId:"",Chord:""});
  var vals=parseChord(k.Chord);vals.CommandId=k.CommandId||COMMAND_IDS[0];
  formModal(isEdit?"Edit binding":"Add binding",kbFields(isEdit,k.CommandId),vals,function(v){
    if(!v.CommandId){toast("Pick a command",true);return;}
    var dto={CommandId:v.CommandId,Chord:buildChord(v)};
    var list=_kb.slice();if(isEdit)list[i]=dto;else list.push(dto);
    saveCollection("/v1.0/api/keybindings",list,function(items){_kb=items;closeModal();renderKb();});});}
function delKb(i){var k=_kb[parseInt(i,10)];confirmModal('Delete binding for "'+(k?k.CommandId:"")+'"?',function(){
  var list=_kb.filter(function(x,ix){return ix!==parseInt(i,10);});
  saveCollection("/v1.0/api/keybindings",list,function(items){_kb=items;renderKb();});});}

/* ================= Skills ================= */
var _sk=[];
function loadSkills(){_reload["skills_list"]=loadSkills;gridLoading("skills_list");api("/v1.0/api/skills").then(function(r){_sk=(r&&r.Items)||[];renderSk();}).catch(function(e){gridError("skills_list",e.message);});}
function renderSk(){renderGrid("skills_list",
  [{h:t("col.name"),get:function(s){return esc(s.Name)+(s.Valid?"":' <span class="tag off">'+t("tag.invalid")+'</span>')}},
   {h:t("col.description"),get:function(s){return esc(s.Description||"")}},
   {h:t("col.cmds"),w:"60px",tip:"How many runnable commands this skill defines",get:function(s){return ""+s.Commands}},
   {h:t("col.state"),w:"90px",tip:"Whether the skill is enabled and offered to the model",get:function(s){return s.Enabled?'<span class="tag on">'+t("tag.enabled")+'</span>':'<span class="tag off">'+t("tag.disabled")+'</span>'}}],
  _sk,{icon:"🛠️",msg:t("empty.skills"),add:"+ "+t("add.skill")});}
function toggleSk(name){var s=null;for(var i=0;i<_sk.length;i++)if(_sk[i].Name===name)s=_sk[i];if(!s)return;
  api("/v1.0/api/skills/enabled","PUT",{Id:name,Enabled:!s.Enabled}).then(function(r){_sk=(r&&r.Items)||[];renderSk();}).catch(function(e){toast(e.message,true);});}
function viewSk(name){api("/v1.0/api/skills/detail?id="+encodeURIComponent(name)).then(function(s){
  openModal(s.Name+" · SKILL.md",'<pre style="white-space:pre-wrap;margin:0;max-height:60vh">'+esc(s.Body||"(empty)")+'</pre>',[{label:t("act.edit"),onClick:function(){openSk(s.Name);}},{label:t("act.close"),primary:true,onClick:closeModal}],true);}).catch(function(e){toast(e.message,true);});}
function skEditor(name,body,isEdit){
  formModal(isEdit?("Edit skill · "+name):"Create skill",
    [{id:"Name",label:"Name",disabled:isEdit,sub:"(folder id)",tip:"The skill's unique folder id (letters, digits, dashes). Cannot be changed after creation."},
     {id:"Body",label:"SKILL.md",type:"textarea",rows:20,tip:"The full SKILL.md source: YAML frontmatter (name, description, commands) followed by the skill instructions."}],
    {Name:name,Body:body},function(v){
      if(!v.Name){toast(t("toast.nameReq"),true);return;}
      if(isEdit){api("/v1.0/api/skills/body","PUT",{Id:name,Body:v.Body}).then(function(r){_sk=(r&&r.Items)||[];closeModal();renderSk();toast(t("toast.saved"));}).catch(function(e){toast(e.message,true);});}
      else{api("/v1.0/api/skills","POST",{Name:v.Name,Body:v.Body}).then(function(r){_sk=(r&&r.Items)||[];closeModal();renderSk();toast(t("toast.created"));}).catch(function(e){toast(e.message,true);});}
    },"xl");}
function openSk(name){
  if(name)api("/v1.0/api/skills/detail?id="+encodeURIComponent(name)).then(function(s){skEditor(s.Name,s.Body||"",true);}).catch(function(e){toast(e.message,true);});
  else skEditor("","---\nname: my-skill\ndescription: what this skill does\ncommands: []\n---\n\nDescribe the skill's procedure here.\n",false);}
function delSk(name){confirmModal('Delete skill "'+name+'"? This removes its folder.',function(){
  api("/v1.0/api/skills?id="+encodeURIComponent(name),"DELETE").then(function(r){_sk=(r&&r.Items)||[];renderSk();toast("Deleted");}).catch(function(e){toast(e.message,true);});});}

/* ================= Sessions ================= */
var _se=[];
function loadSessions(){_reload["sessions_list"]=loadSessions;gridLoading("sessions_list");api("/v1.0/api/sessions").then(function(r){_se=(r&&r.Items)||[];renderSe();}).catch(function(e){gridError("sessions_list",e.message);});}
function renderSe(){renderGrid("sessions_list",
  [{h:t("col.title"),get:function(s){return esc(s.Title||s.Id)}},{h:t("col.model"),get:function(s){return esc(s.Model||"")}},
   {h:t("col.updated"),tip:"When the session was last saved (hover for the exact UTC time)",get:function(s){return fmtWhen(s.UpdatedUtc)}}],
  _se,{icon:"🗂️",msg:t("empty.sessions")});}
function exportSe(id,fmt){api("/v1.0/api/sessions/export?id="+encodeURIComponent(id)+"&format="+fmt).then(function(r){
  var blob=new Blob([r.Content],{type:fmt==="html"?"text/html":"text/markdown"});
  var a=document.createElement("a");a.href=URL.createObjectURL(blob);a.download=r.Filename;document.body.appendChild(a);a.click();document.body.removeChild(a);
  toast("Exported "+r.Filename);}).catch(function(e){toast(e.message,true);});}
function viewSe(id,fmt){api("/v1.0/api/sessions/export?id="+encodeURIComponent(id)+"&format="+fmt).then(function(r){
  if(fmt==="html"){
    openModal("Session · HTML preview",'<iframe id="exview" sandbox style="width:100%;height:62vh;border:1px solid var(--line);border-radius:8px;background:#fff"></iframe>',[{label:"Download",onClick:function(){exportSe(id,"html");}},{label:t("act.close"),primary:true,onClick:closeModal}],true);
    var f=el("exview");if(f)f.srcdoc=r.Content;
  }else{
    openModal("Session · Markdown preview",'<div class="bubble" style="max-width:none;box-shadow:none;background:transparent;padding:0">'+md(r.Content)+'</div>',[{label:"Download",onClick:function(){exportSe(id,"md");}},{label:t("act.close"),primary:true,onClick:closeModal}],true);
  }
}).catch(function(e){toast(e.message,true);});}
function delSe(id){confirmModal('Delete session "'+id+'"?',function(){
  api("/v1.0/api/sessions?id="+encodeURIComponent(id),"DELETE").then(function(r){_se=(r&&r.Items)||[];renderSe();toast("Deleted");}).catch(function(e){toast(e.message,true);});});}

/* ================= Home / Overview ================= */
function loadHome(){el("home_kpis").innerHTML='<div class="empty"><div class="spinner"></div><div>'+t("tbl.loading")+'</div></div>';
  api("/v1.0/api/overview").then(renderHome).catch(function(e){el("home_kpis").innerHTML='<div class="empty"><div class="eicon">⚠️</div><div>'+esc(e.message)+'</div><button class="btn" data-retryhome>'+t("act.retry")+'</button></div>';});}
function renderHome(d){
  el("home_notices").innerHTML=(d.Notices||[]).map(function(n){var ic=n.Level==="warning"?"⚠":(n.Level==="success"?"✓":"ℹ");return '<div class="notice '+esc(n.Level)+'"><span class="ni">'+ic+'</span><span>'+esc(n.Text)+'</span></div>';}).join("");
  var k=[[t("kpi.endpoints"),d.Endpoints,"endpoints","🔌"],[t("kpi.mcp"),d.McpServers,"mcp","🧩"],[t("kpi.prompts"),d.Prompts,"prompts","📝"],
    [t("kpi.subagents"),d.Subagents,"subagents","🤖"],[t("kpi.skills"),d.SkillsEnabled+" / "+d.SkillsTotal,"skills","🛠️"],
    [t("kpi.hookscmds"),(d.Hooks+d.Commands),"hooks","🪝"],[t("kpi.keybindings"),d.Keybindings,"keybindings","⌨️"],
    [t("kpi.sessions"),d.Sessions,"sessions","🗂️"],[t("kpi.totalMsg"),d.TotalMessages,"sessions","💬"]];
  el("home_kpis").innerHTML=k.map(function(x){return '<button class="kpi" data-goto="'+x[2]+'" title="'+esc(x[0])+'"><span class="kv">'+esc(""+x[1])+'</span><span class="kl">'+x[3]+' '+esc(x[0])+'</span></button>';}).join("");
  el("home_default").innerHTML=d.DefaultEndpoint?('<div class="depname">'+esc(d.DefaultEndpoint)+'</div><div class="depmeta">'+esc(d.DefaultAdapter||"")+' · '+esc(d.DefaultModel||"")+'</div><div style="margin-top:12px"><button class="btn secondary" data-goto="endpoints">'+esc(t("home.manageEp"))+'</button></div>'):('<div class="depmeta">'+esc(t("home.noDefault"))+'</div><div style="margin-top:12px"><button class="btn" data-action="addendpoint">'+esc(t("add.endpoint"))+'</button></div>');
  el("home_env").querySelector("tbody").innerHTML=
    "<tr><td>"+esc(t("home.version"))+"</td><td>"+esc(d.Version)+"</td></tr>"+
    "<tr><td>"+esc(t("home.uptime"))+"</td><td>"+esc(d.Uptime)+"</td></tr>"+
    "<tr><td>"+esc(t("home.activePrompt"))+"</td><td>"+esc(d.ActivePrompt||"—")+"</td></tr>"+
    "<tr><td>"+esc(t("home.auth"))+"</td><td>"+(d.AuthEnabled?esc(t("home.authOn")):esc(t("home.authOff")))+"</td></tr>"+
    "<tr><td>"+esc(t("home.configDir"))+"</td><td class='mono' style='font-size:12px;word-break:break-all'>"+esc(d.ConfigDir)+"</td></tr>";
  var rs=d.RecentSessions||[];
  el("home_recent").innerHTML=rs.length?rs.map(function(s){return '<div class="recent-item"><div class="rt"><div class="rtitle">'+esc(s.Title||s.Id)+'</div><div class="rmeta">'+esc(s.Model||"")+' · '+s.MessageCount+' '+esc(t("home.msg"))+' · '+fmtWhen(s.UpdatedUtc)+'</div></div><button class="minibtn" data-viewses="'+esc(s.Id)+'" title="Preview this session">'+esc(t("act.view"))+'</button></div>';}).join(""):'<div class="empty" style="padding:22px">'+esc(t("empty.sessions"))+'</div>';
  el("home_quick").innerHTML='<button class="btn" data-goto="chat">💬 '+esc(t("quick.newchat"))+'</button><button class="btn secondary" data-action="addendpoint">🔌 '+esc(t("quick.addep"))+'</button><button class="btn secondary" data-goto="sessions">🗂️ '+esc(t("quick.sessions"))+'</button><button class="btn secondary" data-goto="settings">⚙️ '+esc(t("quick.settings"))+'</button>';
}
/* ================= Usage analytics ================= */
var usageState={range:"day",endpoint:"",model:"",tab:"tokens",buckets:[],summary:null,wired:false};
function fmtTok(n){n=n||0;if(n>=1e6)return (n/1e6).toFixed(n>=1e7?0:1)+"M";if(n>=1e3)return (n/1e3).toFixed(n>=1e4?0:1)+"k";return ""+Math.round(n);}
function fmtUsd(n){n=n||0;if(n===0)return "$0";if(n<0.01)return "$"+n.toFixed(4);if(n<1)return "$"+n.toFixed(3);return "$"+n.toFixed(2);}
function fmtMs(n){n=Math.round(n||0);if(n>=1000)return (n/1000).toFixed(2)+"s";return n+"ms";}
function fmtPct(n){return ((n||0)*100).toFixed(1)+"%";}
function fmtWhen(ms){try{return new Date(ms).toLocaleString();}catch(e){return ""+ms;}}
function usageQuery(extra){var p="range="+usageState.range;if(usageState.endpoint)p+="&endpoint="+encodeURIComponent(usageState.endpoint);if(usageState.model)p+="&model="+encodeURIComponent(usageState.model);if(extra)p+=extra;return p;}
function loadUsage(){
  if(!usageState.wired){wireUsage();usageState.wired=true;}
  api("/v1.0/api/usage/filters").then(function(f){
    el("us_disabled").style.display=f.Enabled?"none":"";
    fillUsageSelect("us_endpoint",f.Endpoints,usageState.endpoint,"All endpoints");
    fillUsageSelect("us_model",f.Models,usageState.model,"All models");
  }).catch(function(){});
  refreshUsage();
}
function fillUsageSelect(id,vals,cur,allLabel){var s=el(id);if(!s)return;var o='<option value="">'+esc(allLabel)+'</option>';(vals||[]).forEach(function(v){o+='<option value="'+esc(v)+'"'+(v===cur?" selected":"")+'>'+esc(v)+'</option>';});s.innerHTML=o;s.value=cur||"";}
function wireUsage(){
  Array.prototype.forEach.call(document.querySelectorAll("#us_range button"),function(b){b.addEventListener("click",function(){usageState.range=b.dataset.range;Array.prototype.forEach.call(document.querySelectorAll("#us_range button"),function(x){x.classList.toggle("active",x===b);});refreshUsage();});});
  Array.prototype.forEach.call(document.querySelectorAll("#us_tabs button"),function(b){b.addEventListener("click",function(){usageState.tab=b.dataset.tab;Array.prototype.forEach.call(document.querySelectorAll("#us_tabs button"),function(x){x.classList.toggle("active",x===b);});drawUsageChart();renderUsageKpis();});});
  el("us_endpoint").addEventListener("change",function(){usageState.endpoint=this.value;refreshUsage();});
  el("us_model").addEventListener("change",function(){usageState.model=this.value;refreshUsage();});
}
function refreshUsage(){
  el("us_kpis").innerHTML='<div class="empty"><div class="spinner"></div></div>';
  el("us_chart").innerHTML='<div class="chartempty"><div class="spinner"></div></div>';
  api("/v1.0/api/usage/summary?"+usageQuery()).then(function(s){usageState.summary=s;renderUsageKpis();}).catch(function(){usageState.summary=null;el("us_kpis").innerHTML="";});
  api("/v1.0/api/usage/timeseries?"+usageQuery()).then(function(r){usageState.buckets=r.Items||[];drawUsageChart();}).catch(function(){usageState.buckets=[];drawUsageChart();});
  loadUsageHistory();
}
/* KPI cards track the selected chart: distribution tabs (latency/ttft/stream/throughput) show that
   metric's min/avg/p95/p99/max; the tokens and cost tabs show their own summaries. */
function renderUsageKpis(){
  var s=usageState.summary;if(!s){el("us_kpis").innerHTML="";return;}
  var m=s.Metrics||{},cfg=CHART_TABS[usageState.tab]||CHART_TABS.tokens,cards;
  if(cfg.kind==="dist"){
    var d=cfg.d(m)||{},f=cfg.fmt;
    cards=[["Min",f(d.Min||0)],["Avg",f(d.Avg||0)],["p95",f(d.P95||0)],["p99",f(d.P99||0)],["Max",f(d.Max||0)],["Samples",""+(d.Count||0)]];
  }else if(usageState.tab==="cost"){
    cards=[["Cost",fmtUsd(m.CostUsd)],["Calls",""+(m.Calls||0)],["Avg cost/call",fmtUsd(m.Calls>0?(m.CostUsd/m.Calls):0)],["Error rate",fmtPct(m.ErrorRate)]];
  }else{
    cards=[["Total tokens",fmtTok(m.TotalTokens)],["Prompt",fmtTok(Math.max(0,(m.InputTokens||0)-(m.CachedTokens||0)))],["Cached",fmtTok(m.CachedTokens)],["Output",fmtTok(m.OutputTokens)],["Cost",fmtUsd(m.CostUsd)],["Calls",""+(m.Calls||0)],["Cache hit",fmtPct(m.CacheHitRate)],["Error rate",fmtPct(m.ErrorRate)]];
  }
  el("us_kpis").innerHTML=cards.map(function(c){return '<div class="kpi"><span class="kv">'+esc(c[1])+'</span><span class="kl">'+esc(c[0])+'</span></div>';}).join("");
}
/* Tokens and cost are additive totals, drawn as stacked/solid bars. Latency, TTFT, streaming, and
   throughput are distributions, drawn as candlestick/box marks: a min–max wick, an avg–p95 box, and an
   avg line plus a p99 tick, so each time slice shows the spread of its calls rather than one number. */
var CHART_TABS={
  tokens:{kind:"bar",fmt:fmtTok,note:cachedNote,series:[
    {n:"Prompt",c:"#2563eb",g:function(m){var v=(m.InputTokens||0)-(m.CachedTokens||0);return v<0?0:v;}},
    {n:"Cached",c:"#16a34a",g:function(m){return m.CachedTokens||0;}},
    {n:"Output",c:"#d97706",g:function(m){return m.OutputTokens||0;}}]},
  cost:{kind:"bar",fmt:fmtUsd,series:[{n:"Cost",c:"#7c3aed",g:function(m){return m.CostUsd||0;}}]},
  latency:{kind:"dist",fmt:fmtMs,c:"#2563eb",d:function(m){return m.TotalMsDist||{};}},
  ttft:{kind:"dist",fmt:fmtMs,c:"#7c3aed",d:function(m){return m.TtftMsDist||{};}},
  stream:{kind:"dist",fmt:fmtMs,c:"#0891b2",d:function(m){return m.StreamMsDist||{};}},
  throughput:{kind:"dist",fmt:function(v){return (v||0).toFixed(0)+" tok/s";},c:"#16a34a",d:function(m){return m.ThroughputDist||{};}}
};
function cachedNote(buckets){var any=buckets.some(function(b){return (b.Metrics&&b.Metrics.CachedTokens)>0;});return any?"":"Prompt shows the uncached portion; cached tokens stack on top. Cache metrics populate when the provider reports them.";}
function drawUsageChart(){
  var host=el("us_chart"),buckets=usageState.buckets;
  if(!buckets||!buckets.length){host.innerHTML='<div class="chartempty"><div class="empty"><div class="eicon">📉</div><div>No usage recorded in this window yet.</div></div></div>';return;}
  var cfg=CHART_TABS[usageState.tab]||CHART_TABS.tokens;
  var note=(typeof cfg.note==="function")?cfg.note(buckets):(cfg.note||"");
  var body=(cfg.kind==="dist")?distChart(buckets,cfg,usageState.range):barChart(buckets,cfg,usageState.range);
  host.innerHTML=body+legendHtml(cfg)+(note?'<div class="chartnote">'+esc(note)+'</div>':"");
  wireUsageHover(buckets,cfg,usageState.range);
}
function niceMax(v){if(v<=0)return 1;var p=Math.pow(10,Math.floor(Math.log(v)/Math.LN10));var f=v/p;var nf=f<=1?1:(f<=2?2:(f<=5?5:10));return nf*p;}
function fmtBucketLabel(ms,rangeId){var d=new Date(ms);
  if(rangeId==="hour"||rangeId==="day")return d.toLocaleTimeString([],{hour:"numeric",minute:"2-digit"});
  if(rangeId==="week")return d.toLocaleDateString([],{weekday:"short"})+" "+d.toLocaleTimeString([],{hour:"numeric"});
  return d.toLocaleDateString([],{month:"short",day:"numeric"});}
function legendHtml(cfg){
  var h='<div class="chartlegend">';
  if(cfg.kind==="dist"){
    h+='<span><i style="background:'+cfg.c+';opacity:.55"></i>avg–p95</span>';
    h+='<span><i class="line" style="background:'+cfg.c+';opacity:.5"></i>min–max</span>';
    h+='<span><i class="line avgl"></i>avg</span>';
    h+='<span><i class="line" style="background:#dc2626"></i>p99</span>';
  }else{
    cfg.series.forEach(function(sr){h+='<span><i style="background:'+sr.c+'"></i>'+esc(sr.n)+'</span>';});
  }
  return h+'</div>';
}
/* SVG draws only bars + gridlines in a 1000x100 stretched viewBox; axis labels are crisp HTML positioned
   around the plot (so their size is fixed regardless of chart width). */
function barChart(buckets,cfg,rangeId){
  var n=buckets.length;
  var mx=0;buckets.forEach(function(b){var m=b.Metrics||{};var s=0;cfg.series.forEach(function(sr){s+=Math.max(0,sr.g(m)||0);});if(s>mx)mx=s;});
  var ymax=niceMax(mx);
  var VW=1000,VH=100,slot=VW/Math.max(1,n),bw=Math.max(0.5,slot*(n>60?0.82:0.66)),i;
  var svg='<svg viewBox="0 0 '+VW+' '+VH+'" preserveAspectRatio="none">';
  for(i=1;i<4;i++){var gy=VH-(i/4)*VH;svg+='<line class="ugrid" x1="0" y1="'+gy.toFixed(2)+'" x2="'+VW+'" y2="'+gy.toFixed(2)+'"/>';}
  buckets.forEach(function(b,bi){var m=b.Metrics||{},bx=bi*slot+(slot-bw)/2,acc=0;
    cfg.series.forEach(function(sr){var v=Math.max(0,sr.g(m)||0);if(v<=0)return;var hh=(v/ymax)*VH,by=VH-acc-hh;acc+=hh;
      svg+='<rect class="ubar" x="'+bx.toFixed(2)+'" y="'+by.toFixed(2)+'" width="'+bw.toFixed(2)+'" height="'+hh.toFixed(2)+'" fill="'+sr.c+'"/>';});});
  svg+='</svg>';
  var yl='';for(i=4;i>=0;i--)yl+='<span>'+esc(cfg.fmt(ymax*i/4))+'</span>';
  var step=Math.max(1,Math.ceil(n/8)),xl='';
  for(i=0;i<n;i+=step){var pct=(n<=1?50:((i+0.5)/n)*100);xl+='<span style="left:'+pct.toFixed(2)+'%">'+esc(fmtBucketLabel(buckets[i].BucketStartUnixMs,rangeId))+'</span>';}
  return '<div class="uframe"><div class="uy">'+yl+'</div><div class="uplot">'+svg+'<div class="utip" id="us_tip"></div></div><div class="ux">'+xl+'</div></div>';
}
/* Distribution ("candlestick") chart: one mark per bucket — a thin min–max wick with end caps, a filled
   avg–p95 box, an avg line, and a p99 tick — so the spread of each time slice's calls is visible. Same
   stretched 1000x100 viewBox as the bar chart; strokes use non-scaling-stroke to stay a constant width. */
function distChart(buckets,cfg,rangeId){
  var n=buckets.length,i;
  var mx=0;buckets.forEach(function(b){var d=cfg.d(b.Metrics||{})||{};if((d.Max||0)>mx)mx=d.Max||0;});
  var ymax=niceMax(mx);
  var VW=1000,VH=100,slot=VW/Math.max(1,n),bw=Math.max(2,slot*(n>60?0.5:0.42));
  function Y(v){var y=VH-((v||0)/ymax)*VH;return y<0?0:(y>VH?VH:y);}
  var svg='<svg viewBox="0 0 '+VW+' '+VH+'" preserveAspectRatio="none">';
  for(i=1;i<4;i++){var gy=VH-(i/4)*VH;svg+='<line class="ugrid" x1="0" y1="'+gy.toFixed(2)+'" x2="'+VW+'" y2="'+gy.toFixed(2)+'"/>';}
  buckets.forEach(function(b,bi){var d=cfg.d(b.Metrics||{})||{};if(!(d.Count>0))return;
    var bx=bi*slot+(slot-bw)/2,cx=bx+bw/2,xr=bx+bw;
    var yMin=Y(d.Min),yMax=Y(d.Max),yAvg=Y(d.Avg),yP95=Y(d.P95),yP99=Y(d.P99);
    var capL=bx+bw*0.22,capR=bx+bw*0.78;
    svg+='<line class="ucwick" x1="'+cx.toFixed(2)+'" y1="'+yMax.toFixed(2)+'" x2="'+cx.toFixed(2)+'" y2="'+yMin.toFixed(2)+'" stroke="'+cfg.c+'"/>';
    svg+='<line class="uccap" x1="'+capL.toFixed(2)+'" y1="'+yMax.toFixed(2)+'" x2="'+capR.toFixed(2)+'" y2="'+yMax.toFixed(2)+'" stroke="'+cfg.c+'"/>';
    svg+='<line class="uccap" x1="'+capL.toFixed(2)+'" y1="'+yMin.toFixed(2)+'" x2="'+capR.toFixed(2)+'" y2="'+yMin.toFixed(2)+'" stroke="'+cfg.c+'"/>';
    var boxH=Math.max(0.6,yAvg-yP95);
    svg+='<rect class="ucbox" x="'+bx.toFixed(2)+'" y="'+yP95.toFixed(2)+'" width="'+bw.toFixed(2)+'" height="'+boxH.toFixed(2)+'" fill="'+cfg.c+'"/>';
    svg+='<line class="ucavg" x1="'+bx.toFixed(2)+'" y1="'+yAvg.toFixed(2)+'" x2="'+xr.toFixed(2)+'" y2="'+yAvg.toFixed(2)+'"/>';
    svg+='<line class="ucp99" x1="'+bx.toFixed(2)+'" y1="'+yP99.toFixed(2)+'" x2="'+xr.toFixed(2)+'" y2="'+yP99.toFixed(2)+'"/>';});
  svg+='</svg>';
  var yl='';for(i=4;i>=0;i--)yl+='<span>'+esc(cfg.fmt(ymax*i/4))+'</span>';
  var step=Math.max(1,Math.ceil(n/8)),xl='';
  for(i=0;i<n;i+=step){var pct=(n<=1?50:((i+0.5)/n)*100);xl+='<span style="left:'+pct.toFixed(2)+'%">'+esc(fmtBucketLabel(buckets[i].BucketStartUnixMs,rangeId))+'</span>';}
  return '<div class="uframe"><div class="uy">'+yl+'</div><div class="uplot">'+svg+'<div class="utip" id="us_tip"></div></div><div class="ux">'+xl+'</div></div>';
}
/* Custom hover tooltip: maps the cursor's x-fraction to a bucket, lists its values, and is clamped so the
   box always stays fully inside the plot area (never spilling outside the chart). */
function usageTipHtml(b,cfg,rangeId){
  var m=b.Metrics||{},h='<div class="tt">'+esc(fmtBucketLabel(b.BucketStartUnixMs,rangeId))+'</div>';
  if(cfg.kind==="dist"){
    var d=cfg.d(m)||{},f=cfg.fmt;
    if(!(d.Count>0)){h+='<div class="tr"><span class="tk">No calls</span></div>';return h;}
    [["Max",d.Max],["p99",d.P99],["p95",d.P95],["Avg",d.Avg],["Min",d.Min]].forEach(function(r){
      h+='<div class="tr"><span class="tk">'+r[0]+'</span><b>'+esc(f(r[1]||0))+'</b></div>';});
    h+='<div class="tr tot"><span class="tk">Samples</span><b>'+(d.Count||0)+'</b></div>';
    return h;
  }
  var total=0,additive=cfg.series.length>1;
  cfg.series.forEach(function(sr){
    var seg=Math.max(0,sr.g(m)||0);total+=seg;if(sr.tg)additive=false;
    var val=sr.tg?sr.tg(m):seg;
    h+='<div class="tr"><span class="tk"><i style="background:'+sr.c+'"></i>'+esc(sr.n)+'</span><b>'+esc(cfg.fmt(val))+'</b></div>';});
  if(additive)h+='<div class="tr tot"><span class="tk">Total</span><b>'+esc(cfg.fmt(total))+'</b></div>';
  return h;
}
function wireUsageHover(buckets,cfg,rangeId){
  var host=el("us_chart"),plot=host?host.querySelector(".uplot"):null,tip=el("us_tip"),n=buckets.length;
  if(!plot||!tip||!n)return;
  function hide(){tip.classList.remove("on");}
  plot.addEventListener("mouseleave",hide);
  plot.addEventListener("mousemove",function(ev){
    var r=plot.getBoundingClientRect(),x=ev.clientX-r.left,y=ev.clientY-r.top;
    if(x<0||y<0||x>r.width||y>r.height){hide();return;}
    var idx=Math.floor((x/r.width)*n);if(idx<0)idx=0;if(idx>=n)idx=n-1;
    tip.innerHTML=usageTipHtml(buckets[idx],cfg,rangeId);tip.classList.add("on");
    var pad=8,tw=tip.offsetWidth,th=tip.offsetHeight;
    var tx=x+14;if(tx+tw>r.width-pad)tx=x-14-tw;if(tx<pad)tx=pad;if(tx+tw>r.width-pad)tx=Math.max(pad,r.width-pad-tw);
    var ty=y-th-12;if(ty<pad)ty=y+16;if(ty+th>r.height-pad)ty=Math.max(pad,r.height-pad-th);
    tip.style.left=Math.round(tx)+"px";tip.style.top=Math.round(ty)+"px";});
}
/* History table — the shared data grid (sort/filter/paginate/columns), row-click opens a detail modal. */
var _uhist=[],_uhistTotal=0,_sessTitles={};
function sessName(id){if(!id)return "—";return _sessTitles[id]||(id.length>8?id.slice(0,8):id);}
function loadUsageHistory(){_reload["usage_history_list"]=loadUsageHistory;gridLoading("usage_history_list");
  api("/v1.0/api/sessions").then(function(r){var it=(r&&r.Items)||[];_sessTitles={};it.forEach(function(s){_sessTitles[s.Id]=s.Title||s.Id;});}).catch(function(){}).then(function(){
    return api("/v1.0/api/usage/events?"+usageQuery("&page=1&pageSize=500"));
  }).then(function(pg){_uhist=(pg&&pg.Items)||[];_uhistTotal=(pg&&pg.TotalCount)||0;renderUsageHistory();}).catch(function(e){gridError("usage_history_list",e.message);});}
function renderUsageHistory(){
  renderGrid("usage_history_list",[
    {h:"When",tip:"Call completion time",get:function(r){return esc(fmtWhen(r.TimestampUnixMs));}},
    {h:"Conversation",tip:"The conversation this call belongs to",get:function(r){return esc(sessName(r.SessionId));}},
    {h:"Endpoint",get:function(r){return esc(r.EndpointName);}},
    {h:"In",mono:true,tip:"Input tokens",get:function(r){return fmtTok(r.InputTokens);}},
    {h:"Cached",mono:true,tip:"Cache-read tokens",get:function(r){return fmtTok(r.CachedTokens);}},
    {h:"Out",mono:true,tip:"Output tokens",get:function(r){return fmtTok(r.OutputTokens);}},
    {h:"TTFT",mono:true,tip:"Time to first token",get:function(r){return r.TimeToFirstTokenMs!=null?fmtMs(r.TimeToFirstTokenMs):"—";}},
    {h:"Latency",mono:true,tip:"Total request duration",get:function(r){return r.TotalMs!=null?fmtMs(r.TotalMs):"—";}},
    {h:"tok/s",mono:true,tip:"Output throughput",get:function(r){return r.TokensPerSecond!=null?r.TokensPerSecond.toFixed(0):"—";}},
    {h:"Cost",mono:true,get:function(r){return fmtUsd(r.CostUsd);}},
    {h:"Status",get:function(r){return r.Success?'<span class="ubadge ok">ok</span>':'<span class="ubadge err">'+esc(r.ErrorCode||"error")+'</span>';}}
  ],_uhist,{icon:"🗒️",msg:"No calls recorded in this window."});
}
function delUsageRow(i){var r=_uhist[i];if(!r)return;confirmModal("Delete this usage record? This cannot be undone.",function(){
  api("/v1.0/api/usage/events?id="+encodeURIComponent(r.Id),"DELETE").then(function(){toast("Deleted");refreshUsage();}).catch(function(e){toast(e.message,true);});});}
function viewUsageRow(i){var r=_uhist[i];if(!r)return;
  function sec(title,pairs){var h='<div class="udetail-sec"><h5>'+esc(title)+'</h5><div class="udetail-kv">';pairs.forEach(function(kv){h+='<div><div class="k">'+esc(kv[0])+'</div><div class="v">'+esc(""+kv[1])+'</div></div>';});return h+'</div></div>';}
  var badge=r.Success?'<span class="ubadge ok">success</span>':'<span class="ubadge err">'+esc(r.ErrorCode||"error")+'</span>';
  var body='<div class="udetail">'+
    '<div class="udetail-hd"><div><div class="mdl">'+esc(r.Model)+'</div><div class="when">'+esc(fmtWhen(r.TimestampUnixMs))+'</div></div>'+badge+'</div>'+
    sec("Identity",[["Endpoint",r.EndpointName],["Provider",r.AdapterType||"—"],["Call kind",r.CallKind],["Command",r.Command||"—"],["Session",r.SessionId||"—"],["Host",r.BaseHost||"—"]])+
    sec("Tokens",[["Input",fmtTok(r.InputTokens)],["Cached",fmtTok(r.CachedTokens)],["Output",fmtTok(r.OutputTokens)],["Reasoning",fmtTok(r.ReasoningTokens)],["Total",fmtTok(r.TotalTokens)]])+
    sec("Timing",[["Time to first token",r.TimeToFirstTokenMs!=null?fmtMs(r.TimeToFirstTokenMs):"—"],["Streaming time",r.StreamingMs!=null?fmtMs(r.StreamingMs):"—"],["Total latency",r.TotalMs!=null?fmtMs(r.TotalMs):"—"],["Throughput",r.TokensPerSecond!=null?(r.TokensPerSecond.toFixed(1)+" tok/s"):"—"],["Finish reason",r.FinishReason||"—"]])+
    sec("Cost",[["Derived cost",fmtUsd(r.CostUsd)]])+
  '</div>';
  openModal("Call details",body,[{label:t("act.viewjson"),onClick:function(){viewJson("Usage event",r);}},{label:t("act.close"),primary:true,onClick:closeModal}],true);
}
/* ================= Pricing ================= */
var _pr2=[],_prVersion="";
function loadPricing(){_reload["pricing_list"]=loadPricing;gridLoading("pricing_list");
  api("/v1.0/api/usage/pricing").then(function(tb){_prVersion=(tb&&tb.version)||"";_pr2=[];var models=(tb&&tb.models)||{};
    for(var k in models){if(Object.prototype.hasOwnProperty.call(models,k)){var m=models[k]||{};_pr2.push({Model:k,Input:m.inputPerMTok||0,Cached:m.cachedInputPerMTok||0,Output:m.outputPerMTok||0});}}
    _pr2.sort(function(a,b){return a.Model<b.Model?-1:(a.Model>b.Model?1:0);});renderPricing();
  }).catch(function(e){gridError("pricing_list",e.message);});}
function renderPricing(){renderGrid("pricing_list",[
  {h:"Model",mono:true,get:function(r){return esc(r.Model);}},
  {h:"Input $/Mtok",mono:true,tip:"USD per million uncached prompt tokens",get:function(r){return "$"+(+r.Input).toFixed(2);}},
  {h:"Cached $/Mtok",mono:true,tip:"USD per million cache-read tokens",get:function(r){return "$"+(+r.Cached).toFixed(2);}},
  {h:"Output $/Mtok",mono:true,tip:"USD per million completion tokens",get:function(r){return "$"+(+r.Output).toFixed(2);}}
],_pr2,{icon:"💲",msg:"No model rates yet. Unknown models cost nothing until you add a rate.",add:"+ Add model"});}
function prFields(isEdit){return [
  {id:"Model",label:"Model",disabled:isEdit,tip:"The model identifier as reported by the provider — the same value as the endpoint's model field."},
  {id:"Input",label:"Input rate",sub:"(USD / Mtok)",type:"number",step:"0.01",tip:"Price per million uncached prompt tokens."},
  {id:"Cached",label:"Cached input rate",sub:"(USD / Mtok)",type:"number",step:"0.01",tip:"Price per million cache-read prompt tokens — usually a fraction of the input rate."},
  {id:"Output",label:"Output rate",sub:"(USD / Mtok)",type:"number",step:"0.01",tip:"Price per million completion tokens."}];}
function openPr(i,prefill){var isEdit=i>=0,e=isEdit?_pr2[i]:(prefill||{Input:0,Cached:0,Output:0});
  formModal(isEdit?"Edit model pricing":"Add model pricing",prFields(isEdit),e,function(v){
    if(!v.Model){toast(t("toast.nameReq"),true);return;}
    var list=_pr2.slice();
    if(isEdit)list[i]=v;else{for(var j=0;j<list.length;j++){if((""+list[j].Model).toLowerCase()===(""+v.Model).toLowerCase()){toast('"'+v.Model+'" already has a rate.',true);return;}}list.push(v);}
    savePricingList(list);});}
function delPr(i){var m=_pr2[i];if(!m)return;confirmModal('Remove pricing for "'+m.Model+'"?',function(){var list=_pr2.slice();list.splice(i,1);savePricingList(list);});}
function savePricingList(list){var models={};list.forEach(function(r){if(r.Model)models[r.Model]={inputPerMTok:+r.Input||0,cachedInputPerMTok:+r.Cached||0,outputPerMTok:+r.Output||0};});
  busyModal(true);api("/v1.0/api/usage/pricing","PUT",{version:_prVersion,models:models}).then(function(tb){_prVersion=(tb&&tb.version)||_prVersion;closeModal();loadPricing();toast(t("toast.saved"));}).catch(function(e){toast(e.message,true);}).finally(function(){busyModal(false);});
}
var VIEW_LOADERS={home:loadHome,chat:loadChat,endpoints:loadEndpointsAdmin,mcp:loadMcp,prompts:loadPrompts,subagents:loadSubagents,hooks:loadHooks,commands:loadHooks,keybindings:loadKeybindings,skills:loadSkills,sessions:loadSessions,usage:loadUsage,pricing:loadPricing,settings:loadSettings};
function loadStatus(){api("/v1.0/api/health").then(function(h){
  var p=el("statusPill");if(p){p.textContent=(h.Status||"—");p.className="badge status"+(h.Status==="healthy"?" ok":"");}
  var v=el("badgeVersion");if(v)v.textContent=h.Version?("v"+h.Version):"";
  var u=el("badgeUptime");if(u)u.textContent=h.Uptime?("up "+h.Uptime):"";
}).catch(function(){var p=el("statusPill");if(p){p.textContent="offline";p.className="badge status err";}});}
function switchView(view){
  document.querySelectorAll(".nav-item").forEach(function(n){n.classList.toggle("active",n.dataset.view===view);});
  document.querySelectorAll(".view").forEach(function(v){v.classList.remove("active");});
  el("view-"+view).classList.add("active");
  el("viewTitle").textContent=viewTitle(view);
  document.title="mux · "+viewTitle(view);
  el("app").classList.remove("navopen");
  if(VIEW_LOADERS[view])VIEW_LOADERS[view]();
}

function applyTheme(t){document.documentElement.setAttribute("data-theme",t);localStorage.setItem("mux.theme",t);
  el("brandLogo").src=(t==="dark")?"__LOGO_DARK__":"__LOGO_LIGHT__";
  el("themeIcon").textContent=(t==="dark")?"☀️":"🌙";}

document.querySelectorAll(".nav-item").forEach(function(n){n.addEventListener("click",function(){switchView(n.dataset.view);});});
el("themeBtn").addEventListener("click",function(){applyTheme(document.documentElement.getAttribute("data-theme")==="dark"?"light":"dark");});
el("sendBtn").addEventListener("click",function(){if(busy)stopChat();else sendChat();});
el("newChatBtn").addEventListener("click",newChat);
el("delMultiBtn").addEventListener("click",deleteMultiple);
el("refreshConvosBtn").addEventListener("click",function(){loadConvos();toast("Refreshed");});
el("endpointSelect").addEventListener("change",function(){currentModel="";warmModel();});
el("saveSettingsBtn").addEventListener("click",saveSettings);
el("reloadSettingsBtn").addEventListener("click",loadSettings);
/* modal close wiring */
el("modalX").addEventListener("click",closeModal);
el("modalOverlay").addEventListener("click",function(e){if(e.target===el("modalOverlay"))closeModal();});
document.addEventListener("keydown",function(e){if(e.key==="Escape"){closeMenu();if(el("modalOverlay").classList.contains("show"))closeModal();}});
document.addEventListener("click",closeMenu);
window.addEventListener("scroll",closeMenu,true);
window.addEventListener("resize",closeMenu);
/* retry on load-error panels */
document.addEventListener("click",function(e){var r=e.target.closest?e.target.closest("[data-retry]"):null;if(r){var fn=_reload[r.getAttribute("data-retry")];if(fn)fn();}});
/* copy server url + mobile drawer nav */
on("copyUrlBtn",function(){copyText(location.host,el("copyUrlBtn"));});
on("hamburger",function(){el("app").classList.toggle("navopen");});
on("navscrim",function(){el("app").classList.remove("navopen");});
/* focus trap inside the modal */
document.addEventListener("keydown",function(e){
  if(e.key!=="Tab"||!el("modalOverlay").classList.contains("show"))return;
  var f=el("modalBox").querySelectorAll('a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled])');
  if(!f.length)return;var first=f[0],last=f[f.length-1];
  if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}
  else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}
});
/* per-domain add/reload buttons */
function on(id,fn){var e=el(id);if(e)e.addEventListener("click",fn);}
on("endpoints_add",function(){openEp(-1);});on("endpoints_reload",loadEndpointsAdmin);
on("mcp_add",function(){openMcp(-1);});on("mcp_reload",loadMcp);
on("prompts_add",function(){openPr(-1);});on("prompts_reload",loadPrompts);
on("subagents_add",function(){openSa(-1);});on("subagents_reload",loadSubagents);
on("hooks_add",function(){openHook(-1);});on("hooks_reload",loadHooks);
on("cmds_add",function(){openCmd(-1);});on("commands_reload",loadHooks);
on("keybindings_add",function(){openKb(-1);});on("keybindings_reload",loadKeybindings);
on("skills_add",function(){openSk(null);});on("skills_reload",loadSkills);on("sessions_reload",loadSessions);
on("usage_refresh",refreshUsage);on("pricing_add",function(){openPr(-1);});on("pricing_reload",loadPricing);
/* row context-menus + row-click-to-edit + empty-state add */
wireTable("endpoints_list",{menu:function(i){return [{label:t("act.edit"),run:function(){openEp(i);}},{label:t("act.duplicate"),run:function(){openEp(-1,dupOf(_ep[i],"Name"));}},{label:t("act.viewjson"),run:function(){viewJson("Endpoint · "+_ep[i].Name,_ep[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delEp(_ep[i].Name);}}];},row:function(i){openEp(i);},add:function(){openEp(-1);}});
wireTable("mcp_list",{menu:function(i){return [{label:t("act.edit"),run:function(){openMcp(i);}},{label:t("act.duplicate"),run:function(){openMcp(-1,dupOf(_mcp[i],"Name"));}},{label:t("act.viewjson"),run:function(){viewJson("MCP server · "+_mcp[i].Name,_mcp[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delMcp(_mcp[i].Name);}}];},row:function(i){openMcp(i);},add:function(){openMcp(-1);}});
wireTable("prompts_list",{menu:function(i){return [{label:t("act.edit"),run:function(){openPr(i);}},{label:t("act.duplicate"),run:function(){openPr(-1,dupOf(_pr[i],"Name"));}},{label:t("act.viewjson"),run:function(){viewJson("Prompt profile · "+_pr[i].Name,_pr[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delPr(i);}}];},row:function(i){openPr(i);},add:function(){openPr(-1);}});
wireTable("subagents_list",{menu:function(i){return [{label:t("act.edit"),run:function(){openSa(i);}},{label:t("act.duplicate"),run:function(){openSa(-1,dupOf(_sa[i],"Name"));}},{label:t("act.viewjson"),run:function(){viewJson("Subagent · "+_sa[i].Name,_sa[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delSa(i);}}];},row:function(i){openSa(i);},add:function(){openSa(-1);}});
wireTable("hooks_list",{menu:function(i){return [{label:t("act.edit"),run:function(){openHook(i);}},{label:t("act.duplicate"),run:function(){openHook(-1,dupOf(_hooks.Hooks[i],null));}},{label:t("act.viewjson"),run:function(){viewJson("Hook",_hooks.Hooks[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delHook(i);}}];},row:function(i){openHook(i);},add:function(){openHook(-1);}});
wireTable("cmds_list",{menu:function(i){return [{label:t("act.edit"),run:function(){openCmd(i);}},{label:t("act.duplicate"),run:function(){openCmd(-1,dupOf(_hooks.Commands[i],"Name"));}},{label:t("act.viewjson"),run:function(){viewJson("Command · /"+_hooks.Commands[i].Name,_hooks.Commands[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delCmd(i);}}];},row:function(i){openCmd(i);},add:function(){openCmd(-1);}});
wireTable("keybindings_list",{menu:function(i){return [{label:t("act.edit"),run:function(){openKb(i);}},{label:t("act.duplicate"),run:function(){openKb(-1,dupOf(_kb[i],"CommandId"));}},{label:t("act.viewjson"),run:function(){viewJson("Keybinding · "+_kb[i].CommandId,_kb[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delKb(i);}}];},row:function(i){openKb(i);},add:function(){openKb(-1);}});
wireTable("skills_list",{menu:function(i){var s=_sk[i];return [{label:t("act.edit"),run:function(){openSk(s.Name);}},{label:t("act.view"),run:function(){viewSk(s.Name);}},{label:s.Enabled?t("act.disable"):t("act.enable"),run:function(){toggleSk(s.Name);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delSk(s.Name);}}];},row:function(i){var s=_sk[i];if(s)openSk(s.Name);},add:function(){openSk(null);}});
wireTable("sessions_list",{menu:function(i){var id=_se[i].Id;return [{label:"View Markdown",run:function(){viewSe(id,"md");}},{label:"View HTML",run:function(){viewSe(id,"html");}},{sep:true},{label:"Download Markdown",run:function(){exportSe(id,"md");}},{label:"Download HTML",run:function(){exportSe(id,"html");}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delSe(id);}}];},row:function(i){viewSe(_se[i].Id,"md");}});
wireTable("usage_history_list",{menu:function(i){return [{label:t("act.view"),run:function(){viewUsageRow(i);}},{label:t("act.viewjson"),run:function(){viewJson("Usage event",_uhist[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delUsageRow(i);}}];},row:function(i){viewUsageRow(i);}});
wireTable("pricing_list",{menu:function(i){return [{label:t("act.edit"),run:function(){openPr(i);}},{label:t("act.duplicate"),run:function(){openPr(-1,{Model:"",Input:_pr2[i].Input,Cached:_pr2[i].Cached,Output:_pr2[i].Output});}},{label:t("act.viewjson"),run:function(){viewJson("Pricing · "+_pr2[i].Model,_pr2[i]);}},{sep:true},{label:t("act.delete"),danger:true,run:function(){delPr(i);}}];},row:function(i){openPr(i);},add:function(){openPr(-1);}});
var comp=el("composer");
comp.addEventListener("keydown",function(e){if(e.key==="Enter"&&!e.shiftKey){e.preventDefault();sendChat();}});
comp.addEventListener("input",function(){comp.style.height="44px";comp.style.height=Math.min(comp.scrollHeight,180)+"px";});

/* Home view delegation: KPI tiles, quick actions, recent-session preview, retry */
el("view-home").addEventListener("click",function(e){
  var g=e.target.closest?e.target.closest("[data-goto]"):null;if(g){switchView(g.getAttribute("data-goto"));return;}
  var a=e.target.closest?e.target.closest("[data-action]"):null;if(a){if(a.getAttribute("data-action")==="addendpoint"){switchView("endpoints");openEp(-1);}return;}
  var v=e.target.closest?e.target.closest("[data-viewses]"):null;if(v){viewSe(v.getAttribute("data-viewses"),"md");return;}
  var r=e.target.closest?e.target.closest("[data-retryhome]"):null;if(r){loadHome();return;}
});
applyTheme(localStorage.getItem("mux.theme")||"light");
el("serverUrl").textContent=location.host;
/* i18n: apply saved locale to the static chrome, wire the language picker */
el("langSel").value=LOCALE;
el("langSel").addEventListener("change",function(){setLocale(el("langSel").value);});
applyI18n();
iconizeRefreshButtons();
document.title="mux · "+viewTitle("home");
loadStatus();setInterval(loadStatus,15000);
loadEndpoints();
loadHome();
</script>
</body>
</html>
""";
    }
}
