import { McpValidation } from '../api/types';

/**
 * Formats an MCP validation result as plain text for a details document: the outcome line, then either the
 * discovered tools or the failure cause followed by the diagnostic lines (URL, HTTP status, headers, response
 * body, client error, stderr). Pure, so it is unit-testable without VS Code.
 */
export function formatMcpValidation(result: McpValidation): string {
    const lines: string[] = [];
    const outcome = result.Connected ? 'Connected' : 'Failed';
    lines.push(`MCP server "${result.Name}": ${outcome} (${result.Method || 'unknown transport'}, ${result.ElapsedMs ?? 0} ms)`);
    lines.push('');
    if (result.Connected) {
        const tools = result.Tools ?? [];
        lines.push(`Tools (${tools.length}):`);
        for (const tool of tools) {
            lines.push(`  ${tool}`);
        }
    } else {
        lines.push(result.Error ?? 'The MCP server did not connect.');
        if (result.Details) {
            lines.push('');
            lines.push(...result.Details.replace(/\r\n/g, '\n').split('\n'));
        }
    }

    return lines.join('\n') + '\n';
}
