/*
    Live text tools on search results: summarise, shorten, key points, translate.

    The package renders the results list with its AI settings on it as data attributes; this script
    reads those, adds a row of buttons under each result, and calls the package's own endpoint when
    one is pressed. The page the visitor is on is never left.

    What goes over the wire is the result's node key and an action name - never page text and never
    a prompt. The server reads the text from the search index itself and builds the instructions, so
    this endpoint cannot be used to ask the site's AI account something else.
*/
(function () {
    'use strict';

    var LABELS = {
        summarise: 'Summarise',
        shorten: 'Shorten',
        keyPoints: 'Key points',
        translate: 'Translate'
    };

    var WORKING = {
        summarise: 'Summarising…',
        shorten: 'Shortening…',
        keyPoints: 'Pulling out the key points…',
        translate: 'Translating…'
    };

    function split(value) {
        return (value || '')
            .split(',')
            .map(function (item) { return item.trim(); })
            .filter(Boolean);
    }

    function configFor(list) {
        return {
            endpoint: list.getAttribute('data-imobisoft-ai-endpoint') || '',
            actions: split(list.getAttribute('data-imobisoft-ai-actions')),
            languages: split(list.getAttribute('data-imobisoft-ai-languages')),
            profile: list.getAttribute('data-imobisoft-ai-profile') || ''
        };
    }

    // Rendered as text, never as markup: the answer is third-party output, so it is written with
    // textContent and the only structure it gets is the paragraph and list splitting below.
    function render(panel, text) {
        panel.textContent = '';
        panel.classList.remove('is-error');

        var blocks = String(text).split(/\n{2,}/);

        blocks.forEach(function (block) {
            var lines = block.split('\n').filter(function (line) { return line.trim().length > 0; });
            var bulleted = lines.length > 0 && lines.every(function (line) { return /^\s*[-*•]\s+/.test(line); });

            if (bulleted) {
                var list = document.createElement('ul');

                lines.forEach(function (line) {
                    var item = document.createElement('li');
                    item.textContent = line.replace(/^\s*[-*•]\s+/, '');
                    list.appendChild(item);
                });

                panel.appendChild(list);
                return;
            }

            var paragraph = document.createElement('p');
            paragraph.textContent = block.replace(/\s*\n\s*/g, ' ').trim();

            if (paragraph.textContent.length > 0) {
                panel.appendChild(paragraph);
            }
        });
    }

    function error(panel, message) {
        panel.textContent = '';
        panel.classList.add('is-error');

        var paragraph = document.createElement('p');
        paragraph.textContent = message;
        panel.appendChild(paragraph);
    }

    function decorate(card, config) {
        var key = card.getAttribute('data-imobisoft-key');

        if (!key || card.dataset.imobisoftAiBound) {
            return;
        }

        card.dataset.imobisoftAiBound = 'true';

        var bar = document.createElement('div');
        bar.className = 'imai';

        var caption = document.createElement('span');
        caption.className = 'imai-label';
        caption.textContent = 'AI';
        bar.appendChild(caption);

        var panel = document.createElement('div');
        panel.className = 'imai-panel';
        panel.style.display = 'none';
        panel.setAttribute('role', 'status');
        panel.setAttribute('aria-live', 'polite');

        var foot = document.createElement('div');
        foot.className = 'imai-foot';
        foot.style.display = 'none';

        var note = document.createElement('span');
        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'imai-close';
        close.textContent = 'Hide';
        foot.appendChild(note);
        foot.appendChild(close);

        var languageSelect = null;
        var buttons = [];
        var inFlight = null;

        function reset() {
            buttons.forEach(function (button) {
                button.disabled = false;
                button.classList.remove('is-active');
            });
        }

        close.addEventListener('click', function () {
            panel.style.display = 'none';
            foot.style.display = 'none';
            reset();
        });

        function run(action, button) {
            // A second press while one is running would spend twice for one answer.
            if (inFlight) {
                inFlight.abort();
            }

            var controller = new AbortController();
            inFlight = controller;

            reset();
            button.classList.add('is-active');
            buttons.forEach(function (other) { other.disabled = true; });

            panel.style.display = 'block';
            panel.classList.remove('is-error');
            panel.textContent = '';

            var waiting = document.createElement('p');
            waiting.className = 'imai-waiting';
            waiting.textContent = WORKING[action] || 'Working…';
            panel.appendChild(waiting);

            foot.style.display = 'none';

            var body = { action: action, key: key, profile: config.profile };

            if (action === 'translate' && languageSelect) {
                body.language = languageSelect.value;
            }

            fetch(config.endpoint, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify(body),
                signal: controller.signal
            })
                .then(function (response) {
                    return response.json()
                        .catch(function () { return {}; })
                        .then(function (data) { return { ok: response.ok, data: data }; });
                })
                .then(function (result) {
                    if (controller.signal.aborted) {
                        return;
                    }

                    if (result.ok && result.data && result.data.text) {
                        render(panel, result.data.text);
                        note.textContent = result.data.cached
                            ? 'AI generated · reused from an earlier request'
                            : 'AI generated · may contain mistakes';
                    } else {
                        error(panel, (result.data && result.data.error) || 'That could not be done just now.');
                        note.textContent = '';
                    }

                    foot.style.display = 'flex';
                })
                .catch(function (err) {
                    if (err && err.name === 'AbortError') {
                        return;
                    }

                    error(panel, 'Could not reach the site to do that.');
                    note.textContent = '';
                    foot.style.display = 'flex';
                })
                .finally(function () {
                    if (inFlight === controller) {
                        inFlight = null;
                        buttons.forEach(function (other) { other.disabled = false; });
                    }
                });
        }

        config.actions.forEach(function (action) {
            if (!LABELS[action]) {
                return;
            }

            if (action === 'translate') {
                if (config.languages.length === 0) {
                    return;
                }

                languageSelect = document.createElement('select');
                languageSelect.className = 'imai-select';
                languageSelect.setAttribute('aria-label', 'Translate into');

                config.languages.forEach(function (language) {
                    var option = document.createElement('option');
                    option.value = language;
                    option.textContent = language;
                    languageSelect.appendChild(option);
                });

                bar.appendChild(languageSelect);
            }

            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'imai-btn';
            button.textContent = LABELS[action];
            button.addEventListener('click', function () { run(action, button); });

            buttons.push(button);
            bar.appendChild(button);
        });

        if (buttons.length === 0) {
            return;
        }

        card.appendChild(bar);
        card.appendChild(panel);
        card.appendChild(foot);
    }

    function scan(list, config) {
        list.querySelectorAll('[data-imobisoft-key]').forEach(function (card) {
            decorate(card, config);
        });
    }

    function init(list) {
        if (list.dataset.imobisoftAiReady) {
            return;
        }

        list.dataset.imobisoftAiReady = 'true';

        var config = configFor(list);

        if (!config.endpoint || config.actions.length === 0) {
            return;
        }

        scan(list, config);

        // "Load more" appends results after load, so new cards have to be picked up as they arrive.
        if (typeof MutationObserver === 'function') {
            new MutationObserver(function () { scan(list, config); })
                .observe(list, { childList: true });
        }
    }

    function run() {
        document.querySelectorAll('[data-imobisoft-ai-endpoint]').forEach(init);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', run);
    } else {
        run();
    }

    // Exposed so a site that renders results itself can ask for another pass.
    window.imobisoftSearchAi = { refresh: run };
})();
