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

            return Template
                .Replace("__MUX_API_KEY__", JsString(apiKey ?? string.Empty))
                .Replace("__MUX_VERSION__", HtmlEscape(version))
                .Replace("__LOGO_DARK__", logoDark)
                .Replace("__LOGO_LIGHT__", logoLight);
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
<link rel="icon" href="__LOGO_LIGHT__">
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
.nav-group{padding:8px 10px}
.nav-label{color:var(--sidebar-muted);font-size:11px;text-transform:uppercase;letter-spacing:.06em;padding:8px 10px 4px}
.nav-item{display:flex;align-items:center;gap:10px;padding:9px 12px;border-radius:6px;color:var(--sidebar-text);cursor:pointer;font-size:14px;user-select:none}
.nav-item:hover{background:rgba(255,255,255,.06)}
.nav-item.active{background:var(--accent);color:#fff}
.sidebar-foot{margin-top:auto;padding:12px 18px;color:var(--sidebar-muted);font-size:12px}
.main{display:flex;flex-direction:column;min-width:0;height:100vh}
.topbar{height:56px;flex:none;display:flex;align-items:center;justify-content:space-between;padding:0 20px;border-bottom:1px solid var(--line);background:var(--panel)}
.topbar h1{font-size:16px;margin:0;font-weight:600}
.topbar .right{display:flex;align-items:center;gap:14px;color:var(--muted);font-size:13px}
.iconbtn{background:none;border:1px solid var(--line);color:var(--text);border-radius:6px;padding:6px 10px;cursor:pointer;font-size:13px}
.iconbtn:hover{border-color:var(--accent)}
.view{flex:1;min-height:0;overflow:auto;display:none}
.view.active{display:flex;flex-direction:column}
/* chat */
.chat{display:grid;grid-template-rows:auto 1fr auto;height:100%;min-height:0}
.chat-toolbar{display:flex;align-items:center;gap:12px;padding:12px 20px;border-bottom:1px solid var(--line);background:var(--panel);flex-wrap:wrap}
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
.msg.user .bubble{background:color-mix(in srgb,var(--accent) 18%,var(--panel))}
.bubble pre{background:#2f343a;color:#f6f8fa;border:1px solid #4b5563;border-radius:6px;padding:12px;overflow:auto;font-size:13px}
.bubble code{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:13px}
.bubble :not(pre)>code{background:color-mix(in srgb,var(--text) 8%,transparent);border:1px solid var(--line);border-radius:5px;padding:1px 5px}
.bubble p{margin:.4em 0}
.bubble h1,.bubble h2,.bubble h3{margin:.5em 0 .3em}
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
@media(max-width:820px){.app{grid-template-columns:1fr}.sidebar{display:none}.field{grid-template-columns:1fr}}
</style>
</head>
<body>
<div class="app">
  <aside class="sidebar">
    <div class="brand"><img id="brandLogo" src="__LOGO_LIGHT__" alt="mux"><span>mux</span></div>
    <div class="nav-group">
      <div class="nav-label">Chat</div>
      <div class="nav-item active" data-view="chat">💬 Chat</div>
    </div>
    <div class="nav-group">
      <div class="nav-label">System</div>
      <div class="nav-item" data-view="settings">⚙️ Settings</div>
      <div class="nav-item" data-view="info">📊 Server Info</div>
    </div>
    <div class="sidebar-foot">mux v__MUX_VERSION__</div>
  </aside>
  <div class="main">
    <div class="topbar">
      <h1 id="viewTitle">Chat</h1>
      <div class="right">
        <span id="serverUrl"></span>
        <a class="iconbtn" href="https://github.com/jchristn/Mux" target="_blank" rel="noopener">GitHub</a>
        <button class="iconbtn" id="themeBtn" title="Toggle theme">🌙 Theme</button>
      </div>
    </div>

    <!-- Chat -->
    <div class="view active" id="view-chat">
      <div class="chat">
        <div class="chat-toolbar">
          <span><label>Endpoint</label></span>
          <select id="endpointSelect" style="min-width:260px"></select>
          <span class="grow"></span>
          <button class="btn secondary" id="newChatBtn">+ New chat</button>
        </div>
        <div class="messages" id="messages">
          <div class="empty" id="chatEmpty">Pick an endpoint and start chatting with your model.</div>
        </div>
        <div>
          <div class="composer">
            <textarea id="composer" rows="1" placeholder="Message your model… (Enter to send, Shift+Enter for newline)"></textarea>
            <button class="btn send" id="sendBtn" title="Send">➤</button>
          </div>
          <div class="disclaimer">mux runs your chosen model — verify important results.</div>
        </div>
      </div>
    </div>

    <!-- Settings -->
    <div class="view" id="view-settings">
      <div class="pad">
        <div class="card">
          <h2>Agent</h2>
          <p class="hint">Run limits and approval behavior for the agent loop.</p>
          <div class="field"><label>Default approval policy</label>
            <select id="s_defaultApprovalPolicy"><option>ask</option><option>auto</option><option>deny</option></select></div>
          <div class="field"><label>Max agent iterations <span class="sub">(1–100)</span></label><input type="number" id="s_maxAgentIterations" min="1" max="100"></div>
          <div class="field"><label>Max concurrency <span class="sub">(1–32)</span></label><input type="number" id="s_maxConcurrency" min="1" max="32"></div>
          <div class="field"><label>Default enqueue behavior</label>
            <select id="s_defaultEnqueueBehavior"><option>ask</option><option>run_now</option><option>queue_after</option><option>add_to_focused</option></select></div>
          <div class="field"><label>Tool timeout (ms)</label><input type="number" id="s_toolTimeoutMs"></div>
          <div class="field"><label>Process timeout (ms)</label><input type="number" id="s_processTimeoutMs"></div>
        </div>
        <div class="card">
          <h2>Context</h2>
          <p class="hint">Automatic compaction and context-pressure handling.</p>
          <div class="field"><label>Auto-compact</label><input type="checkbox" id="s_autoCompactEnabled"></div>
          <div class="field"><label>Compaction strategy</label>
            <select id="s_compactionStrategy"><option>summary</option><option>trim</option></select></div>
          <div class="field"><label>Preserve turns <span class="sub">(1–10)</span></label><input type="number" id="s_compactionPreserveTurns" min="1" max="10"></div>
          <div class="field"><label>Warning threshold % <span class="sub">(50–95)</span></label><input type="number" id="s_contextWarningThresholdPercent" min="50" max="95"></div>
        </div>
        <div class="card">
          <h2>Features</h2>
          <div class="field"><label>Skills enabled</label><input type="checkbox" id="s_skillsEnabled"></div>
          <div class="field"><label>Task planning</label><input type="checkbox" id="s_taskPlanningEnabled"></div>
          <div class="field"><label>Task parallelism</label><input type="checkbox" id="s_taskParallelismEnabled"></div>
          <div class="field"><label>Ignore cert errors</label><input type="checkbox" id="s_ignoreCertErrors"></div>
          <div class="field"><label>Show boundary lines</label><input type="checkbox" id="s_showBoundaryLines"></div>
        </div>
        <div class="card">
          <h2>REST server <span class="pill">restart required</span></h2>
          <p class="hint">Host/port/SSL changes take effect on the next <code>mux serve</code>. The API key is masked; leave blank to keep the current key.</p>
          <div class="field"><label>Tray auto-start</label><input type="checkbox" id="s_rest_enabled"></div>
          <div class="field"><label>Hostname</label><input type="text" id="s_rest_hostname"></div>
          <div class="field"><label>Port</label><input type="number" id="s_rest_port" min="1" max="65535"></div>
          <div class="field"><label>SSL</label><input type="checkbox" id="s_rest_ssl"></div>
          <div class="field"><label>CORS allow-origin</label><input type="text" id="s_rest_corsAllowOrigin"></div>
          <div class="field"><label>API key <span class="sub" id="apiKeyState"></span></label><input type="password" id="s_rest_apiKey" placeholder="(unchanged)"></div>
        </div>
        <div class="row"><button class="btn" id="saveSettingsBtn">Save settings</button><button class="btn secondary" id="reloadSettingsBtn">Reload</button></div>
      </div>
    </div>

    <!-- Server Info -->
    <div class="view" id="view-info">
      <div class="pad">
        <div class="card">
          <h2>Server</h2>
          <table class="info" id="infoTable"><tbody></tbody></table>
        </div>
        <div class="card">
          <h2>Endpoints</h2>
          <table class="info" id="endpointsTable"><tbody></tbody></table>
        </div>
      </div>
    </div>
  </div>
</div>
<div class="toast" id="toast"></div>
<script>
var API_KEY="__MUX_API_KEY__";
var messages=[];
var busy=false;

function el(id){return document.getElementById(id);}
function esc(s){return (s||"").replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;");}

function toast(msg,isErr){var t=el("toast");t.textContent=msg;t.className="toast show"+(isErr?" err":"");setTimeout(function(){t.className="toast";},2600);}

function api(path,method,body){
  var headers={"Content-Type":"application/json"};
  if(API_KEY) headers["X-Api-Key"]=API_KEY;
  return fetch(path,{method:method||"GET",headers:headers,body:body?JSON.stringify(body):undefined})
    .then(function(r){return r.text().then(function(t){var j=null;try{j=t?JSON.parse(t):null;}catch(e){}
      if(!r.ok){var m=(j&&j.Message)||(j&&j.message)||("HTTP "+r.status);throw new Error(m);}return j;});});
}

/* minimal, safe markdown: escape first, then render fences/inline/bold/headings/links */
function md(text){
  var out=esc(text||"");
  out=out.replace(/```([\s\S]*?)```/g,function(m,c){return "<pre><code>"+c.replace(/^\n/,"")+"</code></pre>";});
  out=out.replace(/`([^`]+)`/g,"<code>$1</code>");
  out=out.replace(/\*\*([^*]+)\*\*/g,"<strong>$1</strong>");
  out=out.replace(/\[([^\]]+)\]\((https?:[^)]+)\)/g,'<a href="$2" target="_blank" rel="noopener">$1</a>');
  out=out.replace(/^### (.*)$/gm,"<h3>$1</h3>").replace(/^## (.*)$/gm,"<h2>$1</h2>").replace(/^# (.*)$/gm,"<h1>$1</h1>");
  out=out.replace(/\n{2,}/g,"</p><p>").replace(/\n/g,"<br>");
  return "<p>"+out+"</p>";
}

function renderMessages(){
  var box=el("messages");
  if(messages.length===0){box.innerHTML='<div class="empty">Pick an endpoint and start chatting with your model.</div>';return;}
  var html="";
  for(var i=0;i<messages.length;i++){
    var m=messages[i];
    var inner=m.typing?'<div class="thinking"><span></span><span></span><span></span></div>':(m.role==="assistant"?md(m.content):"<p>"+esc(m.content).replace(/\n/g,"<br>")+"</p>");
    html+='<div class="msg '+m.role+'"><div class="bubble">'+inner+'</div>';
    if(m.role==="assistant"&&m.model){html+='<div class="meta">'+esc(m.model)+'</div>';}
    html+='</div>';
  }
  box.innerHTML=html;
  box.scrollTop=box.scrollHeight;
}

function sendChat(){
  if(busy)return;
  var text=el("composer").value.trim();
  var endpoint=el("endpointSelect").value;
  if(!text)return;
  if(!endpoint){toast("No endpoint selected",true);return;}
  messages.push({role:"user",content:text});
  el("composer").value="";el("composer").style.height="44px";
  var typing={role:"assistant",content:"",typing:true};
  messages.push(typing);renderMessages();busy=true;el("sendBtn").textContent="…";
  var payload={endpoint:endpoint,messages:messages.filter(function(m){return !m.typing;}).map(function(m){return {role:m.role,content:m.content};})};
  api("/v1.0/api/chat","POST",payload).then(function(reply){
    typing.typing=false;typing.content=(reply&&reply.Content)||"";typing.model=(reply&&reply.Model)||"";
    renderMessages();
  }).catch(function(e){
    typing.typing=false;typing.content="⚠️ "+e.message;typing.role="assistant";renderMessages();toast(e.message,true);
  }).finally(function(){busy=false;el("sendBtn").textContent="➤";});
}

function loadEndpoints(){
  api("/v1.0/api/endpoints").then(function(res){
    var items=(res&&res.Items)||[];var sel=el("endpointSelect");sel.innerHTML="";
    if(items.length===0){var o=document.createElement("option");o.textContent="(no endpoints configured)";o.value="";sel.appendChild(o);return;}
    items.forEach(function(ep){var o=document.createElement("option");o.value=ep.Name;o.textContent=ep.Name+"  ·  "+ep.Model;if(ep.IsDefault)o.selected=true;sel.appendChild(o);});
  }).catch(function(e){toast("Failed to load endpoints: "+e.message,true);});
}

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
  api("/v1.0/api/settings","PUT",dto).then(function(){toast("Settings saved");loadSettings();}).catch(function(e){toast(e.message,true);});
}

function loadInfo(){
  api("/v1.0/api/health").then(function(h){
    el("infoTable").querySelector("tbody").innerHTML=
      "<tr><td>Status</td><td><span class='pill'>"+esc(h.Status)+"</span></td></tr>"+
      "<tr><td>Version</td><td>"+esc(h.Version)+"</td></tr>"+
      "<tr><td>PID</td><td>"+h.Pid+"</td></tr>"+
      "<tr><td>Uptime</td><td>"+esc(h.Uptime)+"</td></tr>"+
      "<tr><td>Started (UTC)</td><td>"+esc(h.StartedUtc)+"</td></tr>";
  }).catch(function(e){toast(e.message,true);});
  api("/v1.0/api/endpoints").then(function(res){
    var items=(res&&res.Items)||[];var rows="";
    items.forEach(function(ep){rows+="<tr><td>"+esc(ep.Name)+(ep.IsDefault?" <span class='pill'>default</span>":"")+"</td><td>"+esc(ep.AdapterType)+" · "+esc(ep.Model)+"</td></tr>";});
    el("endpointsTable").querySelector("tbody").innerHTML=rows||"<tr><td colspan=2 class='empty'>No endpoints configured.</td></tr>";
  }).catch(function(){});
}

function switchView(view){
  var titles={chat:"Chat",settings:"Settings",info:"Server Info"};
  document.querySelectorAll(".nav-item").forEach(function(n){n.classList.toggle("active",n.dataset.view===view);});
  document.querySelectorAll(".view").forEach(function(v){v.classList.remove("active");});
  el("view-"+view).classList.add("active");
  el("viewTitle").textContent=titles[view]||view;
  if(view==="settings")loadSettings();
  if(view==="info")loadInfo();
}

function applyTheme(t){document.documentElement.setAttribute("data-theme",t);localStorage.setItem("mux.theme",t);
  el("brandLogo").src=(t==="dark")?"__LOGO_DARK__":"__LOGO_LIGHT__";
  el("themeBtn").textContent=(t==="dark")?"☀️ Theme":"🌙 Theme";}

document.querySelectorAll(".nav-item").forEach(function(n){n.addEventListener("click",function(){switchView(n.dataset.view);});});
el("themeBtn").addEventListener("click",function(){applyTheme(document.documentElement.getAttribute("data-theme")==="dark"?"light":"dark");});
el("sendBtn").addEventListener("click",sendChat);
el("newChatBtn").addEventListener("click",function(){messages=[];renderMessages();});
el("saveSettingsBtn").addEventListener("click",saveSettings);
el("reloadSettingsBtn").addEventListener("click",loadSettings);
var comp=el("composer");
comp.addEventListener("keydown",function(e){if(e.key==="Enter"&&!e.shiftKey){e.preventDefault();sendChat();}});
comp.addEventListener("input",function(){comp.style.height="44px";comp.style.height=Math.min(comp.scrollHeight,180)+"px";});

applyTheme(localStorage.getItem("mux.theme")||"light");
el("serverUrl").textContent=location.host;
loadEndpoints();
</script>
</body>
</html>
""";
    }
}
