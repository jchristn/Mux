namespace Test.Shared.Support
{
    using System;
    using System.IO;

    /// <summary>
    /// Writes a tiny stdio MCP server in JavaScript for mux's MCP client tests. The first argument picks a mode:
    /// <c>normal</c> (a set of tools with known behaviors), <c>noise</c> (normal, but non-JSON lines are printed
    /// before each response), <c>exit</c> (writes to stderr and exits at once), <c>badinit</c> (answers initialize
    /// with a JSON-RPC error), and <c>oddtools</c> (tools/list includes malformed entries).
    /// </summary>
    public static class NodeMcpServerScript
    {
        #region Private-Members

        private const string Script = @"
const mode = process.argv[2] || 'normal';
if (mode === 'exit') { process.stderr.write('fatal: config missing\n'); process.exit(4); }
const readline = require('readline');
const rl = readline.createInterface({ input: process.stdin });
function send(obj) {
  if (mode === 'noise') process.stdout.write('not json at all\n');
  process.stdout.write(JSON.stringify(obj) + '\n');
}
const tools = [
  { name: 'echo', description: 'Echo text', inputSchema: { type: 'object', properties: { text: { type: 'string' } } } },
  { name: 'env', description: 'Read an environment variable', inputSchema: { type: 'object', properties: { name: { type: 'string' } } } },
  { name: 'argv', description: 'Return argv', inputSchema: { type: 'object', properties: {} } },
  { name: 'pid', description: 'Return the process id', inputSchema: { type: 'object', properties: {} } },
  { name: 'crash', description: 'Exit the process', inputSchema: { type: 'object', properties: {} } },
  { name: 'rpcerror', description: 'Answer with a JSON-RPC error', inputSchema: { type: 'object', properties: {} } },
  { name: 'iserror', description: 'Answer with isError', inputSchema: { type: 'object', properties: {} } }
];
const odd = [
  { name: 'good', description: 'fine', inputSchema: { type: 'object', properties: {} } },
  { description: 'no name' },
  { name: 42, description: 'numeric name' },
  { name: '', description: 'blank name' },
  { name: 'noschema' },
  'just a string'
];
rl.on('line', (line) => {
  let msg;
  try { msg = JSON.parse(line); } catch (e) { return; }
  if (msg.id === undefined) return;
  if (msg.method === 'initialize') {
    if (mode === 'badinit') { send({ jsonrpc: '2.0', id: msg.id, error: { code: -32001, message: 'license expired' } }); return; }
    send({ jsonrpc: '2.0', id: msg.id, result: { protocolVersion: (msg.params && msg.params.protocolVersion) || '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'node-test', version: '1' } } });
    return;
  }
  if (msg.method === 'ping') { send({ jsonrpc: '2.0', id: msg.id, result: {} }); return; }
  if (msg.method === 'tools/list') { send({ jsonrpc: '2.0', id: msg.id, result: { tools: mode === 'oddtools' ? odd : tools } }); return; }
  if (msg.method === 'tools/call') {
    const name = msg.params.name; const args = msg.params.arguments || {};
    const text = (t) => send({ jsonrpc: '2.0', id: msg.id, result: { content: [{ type: 'text', text: String(t) }] } });
    if (name === 'echo' || name === 'good' || name === 'noschema') return text('echo:' + (args.text || ''));
    if (name === 'env') return text('env:' + (process.env[args.name] || '<unset>'));
    if (name === 'argv') return text(JSON.stringify(process.argv.slice(2)));
    if (name === 'pid') return text(process.pid);
    if (name === 'crash') { process.exit(9); }
    if (name === 'rpcerror') { send({ jsonrpc: '2.0', id: msg.id, error: { code: -32602, message: 'quota exceeded for tool' } }); return; }
    if (name === 'iserror') { send({ jsonrpc: '2.0', id: msg.id, result: { isError: true, content: [{ type: 'text', text: 'tool said no' }] } }); return; }
    send({ jsonrpc: '2.0', id: msg.id, error: { code: -32601, message: 'unknown tool ' + name } });
    return;
  }
  send({ jsonrpc: '2.0', id: msg.id, error: { code: -32601, message: 'method not found' } });
});
";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Writes the script to a temporary file and returns its path.
        /// </summary>
        /// <returns>The script path.</returns>
        public static string Write()
        {
            string path = Path.Combine(Path.GetTempPath(), "mux-node-mcp-" + Guid.NewGuid().ToString("N") + ".js");
            File.WriteAllText(path, Script);
            return path;
        }

        #endregion
    }
}
