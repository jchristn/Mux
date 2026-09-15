// The chat panel's webview script. It renders the transcript the extension host streams to it and sends the
// user's messages and approval decisions back. It stays dependency-free and never assigns untrusted content
// through innerHTML — message text goes through textContent — so a model reply can't inject markup.

(function () {
    const vscode = acquireVsCodeApi();
    const strings = window.MUX_STRINGS || {};
    const transcript = document.getElementById('transcript');
    const notice = document.getElementById('notice');
    const form = document.getElementById('composer');
    const input = document.getElementById('input');
    const sendButton = document.getElementById('send');

    let assistantEl = null;
    let assistantRaw = '';
    let busy = false;

    function renderMd(text) {
        try {
            return typeof renderMarkdown === 'function' ? renderMarkdown(text) : null;
        } catch (e) {
            return null;
        }
    }

    function t(key) {
        return strings[key] || key;
    }

    function clearTranscript() {
        transcript.textContent = '';
        assistantEl = null;
        renderEmpty();
    }

    function renderEmpty() {
        if (transcript.childElementCount > 0) {
            return;
        }
        const empty = document.createElement('div');
        empty.className = 'empty';
        const title = document.createElement('div');
        title.className = 'empty-title';
        title.textContent = t('empty.title');
        const hint = document.createElement('div');
        hint.className = 'empty-hint';
        hint.textContent = t('empty.hint');
        empty.appendChild(title);
        empty.appendChild(hint);
        transcript.appendChild(empty);
    }

    function removeEmpty() {
        const empty = transcript.querySelector('.empty');
        if (empty) {
            empty.remove();
        }
    }

    function addMessage(role, text) {
        removeEmpty();
        const el = document.createElement('div');
        el.className = 'message ' + role;
        // Assistant messages render as Markdown; user/error stay plain text for safety and fidelity.
        const rendered = role === 'assistant' ? renderMd(text) : null;
        if (rendered !== null) {
            el.classList.add('markdown');
            el.innerHTML = rendered;
        } else {
            el.textContent = text;
        }
        transcript.appendChild(el);
        transcript.scrollTop = transcript.scrollHeight;
        return el;
    }

    function ensureAssistant() {
        if (!assistantEl) {
            removeEmpty();
            assistantEl = document.createElement('div');
            assistantEl.className = 'message assistant';
            assistantRaw = '';
            transcript.appendChild(assistantEl);
        }
        return assistantEl;
    }

    // Replaces the streaming plain-text bubble with rendered Markdown once the turn completes.
    function finalizeAssistant() {
        if (!assistantEl) {
            return;
        }
        const rendered = renderMd(assistantRaw);
        if (rendered !== null) {
            assistantEl.classList.add('markdown');
            assistantEl.innerHTML = rendered;
        }
        assistantEl = null;
        assistantRaw = '';
    }

    function addTool(tool) {
        removeEmpty();
        let el = transcript.querySelector('[data-tool="' + tool.Id + '"]');
        if (!el) {
            el = document.createElement('div');
            el.className = 'tool';
            el.setAttribute('data-tool', tool.Id);
            transcript.appendChild(el);
        }
        const status = tool.Status === 'ok' ? '✓' : tool.Status === 'fail' ? '✗' : '…';
        el.textContent = status + ' ' + tool.Name + (tool.ElapsedMs ? ' (' + tool.ElapsedMs + ' ms)' : '');
        transcript.scrollTop = transcript.scrollHeight;
    }

    function addApproval(req) {
        removeEmpty();
        const el = document.createElement('div');
        el.className = 'approval';
        const label = document.createElement('div');
        label.textContent = t('approval.title').replace('{0}', req.Name);
        el.appendChild(label);
        if (req.Arguments) {
            const args = document.createElement('pre');
            args.textContent = req.Arguments;
            el.appendChild(args);
        }
        const row = document.createElement('div');
        row.className = 'approval-actions';
        row.appendChild(button(t('approval.approve'), () => decide(req, 'y', el)));
        row.appendChild(button(t('approval.always'), () => decide(req, 'always', el)));
        row.appendChild(button(t('approval.deny'), () => decide(req, 'n', el)));
        el.appendChild(row);
        transcript.appendChild(el);
        transcript.scrollTop = transcript.scrollHeight;
    }

    function decide(req, decision, el) {
        vscode.postMessage({ type: 'approve', runId: req.RunId, toolCallId: req.ToolCallId, decision: decision });
        el.classList.add('resolved');
        Array.prototype.forEach.call(el.querySelectorAll('button'), function (b) {
            b.disabled = true;
        });
    }

    function button(text, onClick) {
        const b = document.createElement('button');
        b.type = 'button';
        b.textContent = text;
        b.addEventListener('click', onClick);
        return b;
    }

    function setBusy(value) {
        busy = value;
        sendButton.textContent = value ? t('composer.stop') : t('composer.send');
        if (!value) {
            assistantEl = null;
        }
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
        vscode.postMessage({ type: 'send', text: text });
    });

    window.addEventListener('message', function (event) {
        const message = event.data;
        switch (message.type) {
            case 'reset':
                clearTranscript();
                break;
            case 'load':
                transcript.textContent = '';
                assistantEl = null;
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
                // Stream as plain text for responsiveness; Markdown is rendered on completion.
                ensureAssistant().textContent = assistantRaw;
                transcript.scrollTop = transcript.scrollHeight;
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
            case 'done':
                finalizeAssistant();
                notice.textContent = '';
                break;
            case 'stopped':
                finalizeAssistant();
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
