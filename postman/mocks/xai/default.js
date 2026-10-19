// Local fixtures based on postman/xai.openapi.json. This server models the Responses contract,
// not the model under test; actual PolyPrompt clients and Mux tools execute outside this process.
const http = require('http');
const saved = new Map();
let sequence = 0;
const model = { id: 'test-model', object: 'model', owned_by: 'xai', created: 1700000000 };
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const send = (status, value) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(value)); };
  if (!req.headers.authorization?.startsWith('Bearer ')) return send(401, { error: { message: 'Bearer authentication required' } });
  try {
    let data = ''; for await (const chunk of req) data += chunk;
    const intercepted = req.body;
    const body = intercepted && typeof intercepted === 'object' ? intercepted : typeof intercepted === 'string' ? JSON.parse(intercepted) : data ? JSON.parse(data) : {};
    if (req.method === 'GET' && url.pathname === '/v1/models') return send(200, { object: 'list', data: [model] });
    if (req.method === 'GET' && /^\/v1\/(language|embedding|image-generation|video-generation)-models$/.test(url.pathname)) return send(200, { models: [model] });
    if (req.method === 'GET' && /^\/v1\/models\//.test(url.pathname)) return send(200, model);
    if (/^\/v1\/responses\/[^/]+\/input_items$/.test(url.pathname)) {
      const id = decodeURIComponent(url.pathname.split('/')[3]); const record = saved.get(id);
      return record ? send(200, { object: 'list', data: record.input, has_more: false }) : send(404, { error: { message: 'Unknown stored response' } });
    }
    if (/^\/v1\/responses\/[^/]+$/.test(url.pathname) && req.method !== 'POST') {
      const id = decodeURIComponent(url.pathname.split('/')[3]);
      if (!saved.has(id)) return send(404, { error: { message: 'Unknown stored response' } });
      if (req.method === 'DELETE') { saved.delete(id); return send(200, { id, deleted: true, object: 'response' }); }
      return send(200, saved.get(id).response);
    }
    if (req.method === 'POST' && url.pathname === '/v1/responses/compact')
      return send(200, { id: 'cmp-test', object: 'response.compaction', model: body.model, output: [{ type: 'compaction', encrypted_content: 'compacted-fixture' }] });
    if (req.method === 'POST' && url.pathname === '/v1/embeddings') {
      const inputs = typeof body.input === 'string' || typeof body.input?.[0] === 'number' ? [body.input] : body.input;
      return send(200, { object: 'list', model: body.model, data: inputs.map((_, index) => ({ index, object: 'embedding', embedding: body.encoding_format === 'base64' ? 'AACAPwAAAEAAAEBA' : [1, 2, 3] })), usage: { prompt_tokens: 2, total_tokens: 2 } });
    }
    if (req.method !== 'POST' || url.pathname !== '/v1/responses') return send(501, { error: { message: 'Outside this local Responses fixture' } });
    if (!body.model || !('input' in body) || 'max_tokens' in body) return send(400, { error: { message: 'Invalid native Responses request' } });
    if (body.previous_response_id && !saved.has(body.previous_response_id)) return send(404, { error: { message: 'Unknown previous_response_id' } });
    const input = Array.isArray(body.input) ? body.input : [{ role: 'user', content: body.input }];
    const results = input.filter(i => i.type === 'function_call_output');
    const functions = (body.tools || []).filter(t => t.type === 'function');
    const smoke = JSON.stringify(input).includes('mux-xai-smoke') || JSON.stringify(input).includes('mux-xai-truncated');
    if (JSON.stringify(input).includes('mux-xai-reload') && (!input.some(i => i.type === 'reasoning' && i.encrypted_content === 'fixture-reasoning') || results.length < 2))
      return send(400, { error: { message: 'Persisted native history was not restored' } });
    if (smoke && results.length > 0 && !input.some(i => i.type === 'reasoning' && i.encrypted_content === 'fixture-reasoning'))
      return send(400, { error: { message: 'Encrypted reasoning was not replayed' } });
    const output = [{ type: 'reasoning', id: 'reason-' + sequence, encrypted_content: 'fixture-reasoning', summary: [{ type: 'summary_text', text: 'Use actual tool evidence.' }] }];
    const round = results.length;
    if (smoke && functions.length && round < 2) {
      const name = round === 0 ? 'list_directory' : 'read_file';
      output.push({ type: 'function_call', id: 'fc-' + round, call_id: 'call-' + round, name, arguments: JSON.stringify(round === 0 ? { path: '.' } : { file_path: '.console-smoke.txt' }), status: 'completed' });
    } else {
      const text = smoke && round > 1 ? 'Actual file: ' + results[1].output : 'Hello from the Responses fixture.';
      output.push({ type: 'message', id: 'msg-' + sequence, role: 'assistant', status: 'completed', content: [{ type: 'output_text', text, annotations: [] }] });
    }
    const response = { id: 'resp-' + (++sequence), object: 'response', model: body.model, created_at: 1700000000, status: 'completed', output, store: body.store ?? true,
      usage: { input_tokens: 20, output_tokens: 10, total_tokens: 30, input_tokens_details: { cached_tokens: 2 }, output_tokens_details: { reasoning_tokens: 4 } } };
    if (response.store) saved.set(response.id, { response, input });
    if (!body.stream) return send(200, response);
    res.writeHead(200, { 'Content-Type': 'text/event-stream' });
    const event = (type, extra) => res.write('event: ' + type + '\ndata: ' + JSON.stringify({ type, ...extra }) + '\n\n');
    event('response.created', { response: { id: response.id, model: response.model, status: 'in_progress', output: [] } });
    for (let index = 0; index < output.length; index++) {
      const item = output[index];
      event('response.output_item.added', { output_index: index, item: item.type === 'function_call' ? { ...item, arguments: '' } : item });
      if (item.type === 'reasoning') event('response.reasoning_summary_text.delta', { output_index: index, delta: item.summary[0].text });
      if (item.type === 'message') event('response.output_text.delta', { output_index: index, delta: item.content[0].text });
      if (item.type === 'function_call') {
        event('response.function_call_arguments.delta', { output_index: index, item_id: item.id, delta: item.arguments.slice(0, 3) });
        event('response.function_call_arguments.delta', { output_index: index, item_id: item.id, delta: item.arguments.slice(3) });
        event('response.function_call_arguments.done', { output_index: index, item_id: item.id, arguments: item.arguments });
      }
      event('response.output_item.done', { output_index: index, item });
    }
    if (JSON.stringify(input).includes('mux-xai-truncated')) { res.end(); return; }
    event('response.completed', { response }); res.end('data: [DONE]\n\n');
  } catch (error) { send(500, { error: { message: String(error.message || error) } }); }
});
server.listen(process.env.PORT || 4912, '127.0.0.1');
