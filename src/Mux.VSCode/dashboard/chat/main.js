// The chat panel's webview script. It renders the transcript the extension host streams to it and sends the
// user's messages and approval decisions back. It is dependency-free; the only HTML it injects is the output
// of the Markdown renderer (which escapes untrusted model text first) — all other text goes through
// textContent, so a reply can never inject markup.

(function () {
    const vscode = acquireVsCodeApi();
    const strings = window.MUX_STRINGS || {};
    const transcript = document.getElementById('transcript');
    const notice = document.getElementById('notice');
    const form = document.getElementById('composer');
    const input = document.getElementById('input');
    const sendButton = document.getElementById('send');

    let assistant = null; // { wrapper, body } while a reply is streaming
    let assistantRaw = '';
    let busy = false;

    function t(key) {
        return strings[key] || key;
    }

    function renderMd(text) {
        try {
            return typeof renderMarkdown === 'function' ? renderMarkdown(text) : null;
        } catch (e) {
            return null;
        }
    }

    function clearTranscript() {
        transcript.textContent = '';
        assistant = null;
        renderEmpty();
    }

    function renderEmpty() {
        if (transcript.childElementCount > 0) {
            return;
        }
        const empty = el('div', 'empty');
        const mark = el('div', 'empty-mark');
        mark.textContent = 'm';
        empty.appendChild(mark);
        empty.appendChild(withText(el('div', 'empty-title'), t('empty.title')));
        empty.appendChild(withText(el('div', 'empty-hint'), t('empty.hint')));
        transcript.appendChild(empty);
    }

    function removeEmpty() {
        const e = transcript.querySelector('.empty');
        if (e) {
            e.remove();
        }
    }

    // Builds a message row: a role label plus a body. Returns the body so callers can fill it.
    function createMessage(role) {
        removeEmpty();
        const wrapper = el('div', 'msg role-' + role);
        const roleLabel = role === 'user' ? t('role.you') : role === 'assistant' ? t('role.mux') : t('role.error');
        wrapper.appendChild(withText(el('div', 'msg-role'), roleLabel));
        const body = el('div', 'msg-body');
        wrapper.appendChild(body);
        transcript.appendChild(wrapper);
        scrollToEnd();
        return { wrapper: wrapper, body: body };
    }

    function fillBody(body, role, text) {
        const rendered = role === 'assistant' ? renderMd(text) : null;
        if (rendered !== null) {
            body.classList.add('markdown');
            body.innerHTML = rendered;
        } else {
            body.textContent = text;
        }
    }

    function addMessage(role, text) {
        const m = createMessage(role);
        fillBody(m.body, role, text);
        return m;
    }

    function ensureAssistant() {
        if (!assistant) {
            assistant = createMessage('assistant');
            assistant.wrapper.classList.add('streaming');
            assistantRaw = '';
        }
        return assistant;
    }

    // Replaces the streaming plain-text body with rendered Markdown once the turn completes, and appends the
    // per-turn stats footer (time and tokens), revealed in full on hover — matching the web and desktop apps.
    function finalizeAssistant(stats) {
        if (!assistant) {
            return;
        }
        assistant.wrapper.classList.remove('streaming');
        fillBody(assistant.body, 'assistant', assistantRaw);
        if (stats) {
            assistant.wrapper.appendChild(buildStats(stats));
        }
        assistant = null;
        assistantRaw = '';
    }

    function buildStats(stats) {
        const wrap = el('div', 'msg-stats');
        const totalTokens = stats.TotalTokens || (stats.InputTokens || 0) + (stats.OutputTokens || 0);
        const summary = el('div', 'stats-summary');
        summary.textContent = '⚡ ' + fmtMs(stats.TotalMs) + ' · ' + fmtNum(totalTokens) + ' ' + t('stats.tokens');

        const detail = el('div', 'stats-detail');
        detail.appendChild(statRow(t('stats.ttft'), fmtMs(stats.TtftMs)));
        detail.appendChild(statRow(t('stats.streaming'), fmtMs(stats.StreamingMs)));
        detail.appendChild(statRow(t('stats.total'), fmtMs(stats.TotalMs)));
        detail.appendChild(statDivider());
        detail.appendChild(statRow(t('stats.input'), fmtNum(stats.InputTokens || 0)));
        detail.appendChild(statRow(t('stats.output'), fmtNum(stats.OutputTokens || 0)));
        detail.appendChild(statRow(t('stats.totalTokens'), fmtNum(totalTokens)));

        wrap.appendChild(summary);
        wrap.appendChild(detail);
        return wrap;
    }

    function statRow(label, value) {
        const row = el('div', 'stat-row');
        row.appendChild(withText(el('span', 'stat-label'), label));
        row.appendChild(withText(el('span', 'stat-value'), value));
        return row;
    }

    function statDivider() {
        return el('div', 'stat-divider');
    }

    function addHelp(help) {
        removeEmpty();
        const card = el('div', 'help-card');
        card.appendChild(withText(el('div', 'help-title'), help.title));
        (help.items || []).forEach(function (it) {
            const row = el('div', 'help-row');
            row.appendChild(withText(el('span', 'help-cmd'), it.cmd));
            row.appendChild(withText(el('span', 'help-desc'), it.desc));
            card.appendChild(row);
        });
        transcript.appendChild(card);
        scrollToEnd();
    }

    function addTool(tool) {
        removeEmpty();
        let e = transcript.querySelector('[data-tool="' + tool.Id + '"]');
        if (!e) {
            e = el('div', 'tool');
            e.setAttribute('data-tool', tool.Id);
            const glyph = el('span', 'tool-glyph');
            const name = el('span', 'tool-name');
            const time = el('span', 'tool-time');
            e.appendChild(glyph);
            e.appendChild(name);
            e.appendChild(time);
            transcript.appendChild(e);
        }
        const state = tool.Status === 'ok' ? 'ok' : tool.Status === 'fail' ? 'fail' : 'running';
        e.className = 'tool ' + state;
        e.querySelector('.tool-glyph').textContent = state === 'ok' ? '✓' : state === 'fail' ? '✗' : '⟳';
        e.querySelector('.tool-name').textContent = tool.Name;
        e.querySelector('.tool-time').textContent = tool.ElapsedMs ? fmtMs(tool.ElapsedMs) : '';
        scrollToEnd();
    }

    function addApproval(req) {
        removeEmpty();
        const e = el('div', 'approval');
        e.appendChild(withText(el('div', 'approval-title'), t('approval.title').replace('{0}', req.Name)));
        if (req.Arguments) {
            const args = el('pre', 'approval-args');
            args.textContent = req.Arguments;
            e.appendChild(args);
        }
        const row = el('div', 'approval-actions');
        row.appendChild(button(t('approval.approve'), 'primary', () => decide(req, 'y', e)));
        row.appendChild(button(t('approval.always'), 'secondary', () => decide(req, 'always', e)));
        row.appendChild(button(t('approval.deny'), 'secondary', () => decide(req, 'n', e)));
        e.appendChild(row);
        transcript.appendChild(e);
        scrollToEnd();
    }

    function decide(req, decision, e) {
        vscode.postMessage({ type: 'approve', runId: req.RunId, toolCallId: req.ToolCallId, decision: decision });
        e.classList.add('resolved');
        Array.prototype.forEach.call(e.querySelectorAll('button'), function (b) {
            b.disabled = true;
        });
    }

    function setBusy(value) {
        busy = value;
        sendButton.textContent = value ? t('composer.stop') : t('composer.send');
        sendButton.classList.toggle('stop', value);
        if (!value) {
            assistant = null;
        }
    }

    // ---- helpers ----
    function el(tag, cls) {
        const node = document.createElement(tag);
        if (cls) {
            node.className = cls;
        }
        return node;
    }

    function withText(node, text) {
        node.textContent = text;
        return node;
    }

    function button(text, kind, onClick) {
        const b = el('button', kind);
        b.type = 'button';
        b.textContent = text;
        b.addEventListener('click', onClick);
        return b;
    }

    function scrollToEnd() {
        transcript.scrollTop = transcript.scrollHeight;
    }

    function fmtMs(ms) {
        if (ms === undefined || ms === null || ms < 0) {
            return '—';
        }
        return ms < 1000 ? Math.round(ms) + ' ms' : (ms / 1000).toFixed(1) + ' s';
    }

    function fmtNum(n) {
        return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    }

    form.addEventListener('submit', function (e) {
        e.preventDefault();
        if (busy) {
            vscode.postMessage({ type: 'stop' });
            return;
        }
        const text = input.value.trim();
        if (!text) {
            return;
        }
        addMessage('user', text);
        input.value = '';
        autosize();
        vscode.postMessage({ type: 'send', text: text });
    });

    function autosize() {
        input.style.height = 'auto';
        input.style.height = Math.min(input.scrollHeight, 200) + 'px';
    }
    input.addEventListener('input', autosize);
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            form.requestSubmit();
        }
    });

    window.addEventListener('message', function (event) {
        const message = event.data;
        switch (message.type) {
            case 'reset':
                clearTranscript();
                break;
            case 'load':
                transcript.textContent = '';
                assistant = null;
                (message.messages || []).forEach(function (m) {
                    addMessage(m.role, m.content);
                });
                renderEmpty();
                break;
            case 'echo':
                addMessage('user', message.text);
                break;
            case 'token':
                assistantRaw += message.text;
                ensureAssistant().body.textContent = assistantRaw;
                scrollToEnd();
                break;
            case 'thinking':
                break;
            case 'tool':
                addTool(message.tool);
                break;
            case 'approval':
                addApproval(message.request);
                break;
            case 'notice':
                notice.textContent = message.message;
                break;
            case 'help':
                addHelp(message);
                break;
            case 'done':
                finalizeAssistant(message.stats);
                notice.textContent = '';
                break;
            case 'stopped':
                finalizeAssistant(null);
                notice.textContent = t('stopped');
                break;
            case 'error':
                addMessage('error', message.message);
                break;
            case 'busy':
                setBusy(message.busy);
                break;
            default:
                break;
        }
    });

    renderEmpty();
})();
