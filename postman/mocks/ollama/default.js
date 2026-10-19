// Small native Ollama contract fixture for the Mux launcher smoke test.
// Shapes follow docs.ollama.com/api; inference is deliberately deterministic.
const http = require('http');
http.createServer(async (req, res) => {
  const path = new URL(req.url, 'http://localhost').pathname;
  let body = req.body;
  if (!body) {
    let data = '';
    for await (const chunk of req) data += chunk;
    try { body = data ? JSON.parse(data) : {}; } catch { res.writeHead(400); return res.end('{"error":"invalid JSON"}'); }
  }
  const model = 'deepseek-v4.1-flash:cloud';
  const json = (status, value) => { res.writeHead(status, {'Content-Type':'application/json'}); res.end(JSON.stringify(value)); };
  if (path === '/' && req.method === 'HEAD') { res.writeHead(200); return res.end(); }
  if (path === '/api/experimental/model-recommendations') return json(200, {models:[]});
  if (path === '/api/version') return json(200, {version:'0.23.0'});
  if (path === '/api/status') return json(200, {cloud:{disabled:false}});
  if (path === '/api/me') return json(200, {name:'launcher-contract-test', plan:'pro'});
  if (path === '/api/tags') return json(200, {models:[{name:model, model, remote_host:'https://ollama.com', remote_model:'deepseek-v4.1-flash', details:{family:'deepseek',parameter_size:'763B'}}]});
  if (path === '/api/ps') return json(200, {models:[]});
  if (path === '/api/show') return json(200, {capabilities:['completion','tools'], remote_host:'https://ollama.com', remote_model:'deepseek-v4.1-flash', model_info:{'general.architecture':'deepseek','deepseek.context_length':1048576}});
  if (path === '/api/chat' && req.method === 'POST') {
    if (body.model !== model || !Array.isArray(body.messages)) return json(400,{error:'expected selected model and messages'});
    const reply = {model,created_at:new Date().toISOString(),message:{role:'assistant',content:'MUX_LAUNCH_OK'},done:true,done_reason:'stop',prompt_eval_count:20,eval_count:5};
    if (body.stream === false) return json(200,reply);
    res.writeHead(200,{'Content-Type':'application/x-ndjson'});
    res.end(JSON.stringify({...reply,done:false,message:{role:'assistant',content:'MUX_LAUNCH_OK'}})+'\n'+JSON.stringify({...reply,message:{role:'assistant',content:''}})+'\n');return;
  }
  return json(501,{error:'Endpoint not modeled by the launcher contract fixture'});
}).listen(process.env.PORT || 4917, '127.0.0.1');
