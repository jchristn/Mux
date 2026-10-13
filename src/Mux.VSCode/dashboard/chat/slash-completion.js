// Shared by the VS Code webview and the embedded web dashboard. Command lists are supplied by each client.
(function (root) {
    'use strict';
    function match(text, caret, aliases, limit) {
        limit = limit === undefined ? 12 : limit;
        if (!text || text[0] !== '/' || caret !== text.length || /\s/.test(text) || limit < 1) return [];
        var prefix = text.slice(1).toLowerCase(), seen = Object.create(null);
        return aliases.filter(function (name) {
            var key = name.toLowerCase();
            if (!name || key.indexOf(prefix) !== 0 || seen[key]) return false;
            seen[key] = true; return true;
        }).sort(function (a, b) { return a.toLowerCase() < b.toLowerCase() ? -1 : a.toLowerCase() > b.toLowerCase() ? 1 : 0; })
            .slice(0, limit).map(function (name) { return '/' + name; });
    }
    function matchArgument(text, caret, command, values, limit) {
        limit = limit === undefined ? 12 : limit;
        if (!text || caret !== text.length || /[\r\n]/.test(text) || limit < 1) return [];
        var split = text.search(/[ \t]/);
        if (split < 0 || text.slice(0, split).toLowerCase() !== '/' + command.toLowerCase()) return [];
        var prefix = text.slice(split + 1).replace(/^\s+/, '').toLowerCase(), seen = Object.create(null);
        if (prefix && /\s$/.test(prefix)) return [];
        return values.filter(function (name) {
            var key = name.toLowerCase();
            if (!name || key.indexOf(prefix) !== 0 || seen[key]) return false;
            seen[key] = true; return true;
        }).sort(function (a, b) { return a.toLowerCase() < b.toLowerCase() ? -1 : a.toLowerCase() > b.toLowerCase() ? 1 : 0; })
            .slice(0, limit).map(function (name) { return '/' + command + ' ' + name; });
    }
    function attach(input, list, aliases, argumentsFor) {
        var matches = [], index = 0, query = '', dismissed = null, composing = false;
        list.setAttribute('role', 'listbox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-controls', list.id);
        function render() {
            list.textContent = '';
            list.hidden = !matches.length;
            input.setAttribute('aria-expanded', matches.length ? 'true' : 'false');
            input.removeAttribute('aria-activedescendant');
            matches.forEach(function (command, i) {
                var option = document.createElement('button');
                option.type = 'button'; option.tabIndex = -1;
                option.id = list.id + '-option-' + i;
                option.className = 'slash-option' + (i === index ? ' selected' : '');
                option.textContent = command;
                option.setAttribute('role', 'option');
                option.setAttribute('aria-selected', i === index ? 'true' : 'false');
                option.addEventListener('mousedown', function (event) { event.preventDefault(); });
                option.addEventListener('click', function () { accept(i); });
                list.appendChild(option);
                if (i === index) {
                    input.setAttribute('aria-activedescendant', option.id);
                    if (option.scrollIntoView) option.scrollIntoView({ block: 'nearest' });
                }
            });
        }
        function refresh() {
            if (input.value !== query) { query = input.value; index = 0; dismissed = null; }
            matches = composing || dismissed === query || input.selectionStart !== input.selectionEnd ? []
                : match(query, input.selectionStart, typeof aliases === 'function' ? aliases() : aliases);
            if (!composing && dismissed !== query && input.selectionStart === input.selectionEnd && argumentsFor) {
                var argumentMatches = argumentsFor(query, input.selectionStart);
                if (argumentMatches.length) matches = argumentMatches;
            }
            if (index >= matches.length) index = 0;
            render();
        }
        function accept(i) {
            input.value = matches[i] + ' ';
            input.setSelectionRange(input.value.length, input.value.length);
            input.focus();
            input.dispatchEvent(new Event('input', { bubbles: true }));
            refresh();
        }
        input.addEventListener('keydown', function (event) {
            if (event.isComposing || composing || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
            refresh();
            if (!matches.length) return;
            if (event.key === 'Enter' && /^\/endpoint\s+$/i.test(input.value)) return;
            var handled = true;
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                index = (index + (event.key === 'ArrowDown' ? 1 : matches.length - 1)) % matches.length; render();
            } else if (event.key === 'Escape') { dismissed = query; matches = []; render(); }
            else if (event.key === 'Enter' && /^\/endpoint\s+\S/i.test(matches[index])) {
                // Enter accepts the selected endpoint and lets the composer execute it in the same keypress.
                accept(index); handled = false;
            }
            else if (event.key === 'Tab' || (event.key === 'Enter' && input.value.toLowerCase() !== matches[index].toLowerCase())) accept(index);
            else handled = false;
            if (handled) { event.preventDefault(); event.stopImmediatePropagation(); }
        }, true);
        input.addEventListener('input', refresh);
        input.addEventListener('keyup', refresh);
        input.addEventListener('click', refresh);
        input.addEventListener('focus', refresh);
        input.addEventListener('blur', function () { list.hidden = true; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); });
        input.addEventListener('compositionstart', function () { composing = true; refresh(); });
        input.addEventListener('compositionend', function () { composing = false; refresh(); });
        refresh();
        return { refresh: refresh };
    }
    var api = { match: match, matchArgument: matchArgument, attach: attach };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    root.MuxSlashCompletion = api;
})(typeof window !== 'undefined' ? window : globalThis);
