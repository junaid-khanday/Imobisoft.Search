/*
    Live text tools on a search: summarise, shorten, key points, translate, and ask.

    The package renders the results list with its AI settings on it as data attributes; this script
    reads those and puts one row of buttons above the results. The page the visitor is on is never
    left.

    Every action is about the search, not about one result: the browser sends the action and the
    search term, the server searches the site with it, reads the pages that match, and works on
    those. Ask additionally carries a question the visitor typed, and retrieves on the question
    instead of the term. No page text and no prompt is ever sent by the browser - the instructions
    are built server-side and the content comes from the search index, so this endpoint cannot be
    turned into a general chatbot.
*/
(function () {
    'use strict';

    var LABELS = {
        summarise: 'Summarise',
        shorten: 'Shorten',
        keyPoints: 'Key points',
        translate: 'Translate',
        ask: 'Ask a question'
    };

    var WORKING = {
        summarise: 'Summarising…',
        shorten: 'Shortening…',
        keyPoints: 'Pulling out the key points…',
        translate: 'Translating…',
        ask: 'Searching the site for an answer…'
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
            profile: list.getAttribute('data-imobisoft-ai-profile') || '',
            term: list.getAttribute('data-imobisoft-ai-term') || '',
            questionLimit: parseInt(list.getAttribute('data-imobisoft-ai-question-limit'), 10) || 300
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

    // Titles and links come from the server's own search results, but they are still rendered as
    // text and href only - never as markup.
    function sourceList(sources) {
        var wrap = document.createElement('div');
        wrap.className = 'imai-sources';

        var caption = document.createElement('span');
        caption.className = 'imai-sources-label';
        caption.textContent = sources.length === 1 ? 'Answered from:' : 'Answered from these pages:';
        wrap.appendChild(caption);

        var list = document.createElement('ul');

        sources.forEach(function (source) {
            var item = document.createElement('li');

            if (source.url) {
                var link = document.createElement('a');
                link.href = source.url;
                link.textContent = source.title || source.url;
                item.appendChild(link);
            } else {
                item.textContent = source.title || '';
            }

            list.appendChild(item);
        });

        wrap.appendChild(list);

        return wrap;
    }

    function error(panel, message) {
        panel.textContent = '';
        panel.classList.add('is-error');

        var paragraph = document.createElement('p');
        paragraph.textContent = message;
        panel.appendChild(paragraph);
    }

    function decorate(card, config) {
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
        var originalButton = null;
        var buttons = [];
        var inFlight = null;

        // Set once the visitor translates. From then on every action answers in the chosen language:
        // summarising back into the page's own language after a translation reads as a bug. The
        // dropdown stays live, so changing it changes what the next action answers in.
        var languageActive = false;

        function activeLanguage() {
            return languageActive && languageSelect ? languageSelect.value : null;
        }

        function showOriginal(on) {
            if (originalButton) {
                originalButton.style.display = on ? '' : 'none';
            }
        }

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

            var body = { action: action, term: config.term, profile: config.profile };

            if (action === 'ask' && askInput) {
                body.question = askInput.value.trim();
            }

            if (action === 'translate' && languageSelect) {
                body.language = languageSelect.value;
                languageActive = true;
                showOriginal(true);
            } else {
                // Carries a translation the visitor already asked for into this action too. Left
                // off, the server answers in the page's own language.
                var language = activeLanguage();
                if (language) {
                    body.language = language;
                }
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

                        // A question is answered from across the site, so show which pages it drew
                        // on - an answer with no visible provenance is hard to trust, and these are
                        // the pages the server actually read.
                        if (result.data.sources && result.data.sources.length > 0) {
                            panel.appendChild(sourceList(result.data.sources));
                        }

                        var parts = ['AI generated'];
                        var shownIn = action === 'translate' ? (languageSelect ? languageSelect.value : null) : activeLanguage();
                        if (shownIn) {
                            parts.push('in ' + shownIn);
                        }
                        parts.push(result.data.cached ? 'reused from an earlier request' : 'may contain mistakes');

                        note.textContent = parts.join(' · ');
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

        // The question row, built only when the action is enabled. It stays hidden until the Ask
        // button is pressed, so a result card is not covered in form fields by default.
        var askRow = null;
        var askInput = null;

        function buildAskRow() {
            askRow = document.createElement('form');
            askRow.className = 'imai-ask';
            askRow.style.display = 'none';

            askInput = document.createElement('input');
            askInput.type = 'text';
            askInput.className = 'imai-ask-input';
            askInput.maxLength = config.questionLimit;
            askInput.placeholder = 'Ask a question about this site…';
            askInput.setAttribute('aria-label', 'Ask a question about this site');

            var send = document.createElement('button');
            send.type = 'submit';
            send.className = 'imai-btn';
            send.textContent = 'Ask';

            askRow.appendChild(askInput);
            askRow.appendChild(send);

            askRow.addEventListener('submit', function (e) {
                e.preventDefault();

                if (askInput.value.trim().length === 0) {
                    askInput.focus();
                    return;
                }

                run('ask', send);
            });

            return askRow;
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

                // Lets the visitor undo a sticky language and go back to the page's own. Hidden
                // until there is something to undo.
                originalButton = document.createElement('button');
                originalButton.type = 'button';
                originalButton.className = 'imai-btn';
                originalButton.textContent = 'Original language';
                originalButton.style.display = 'none';
                originalButton.addEventListener('click', function () {
                    languageActive = false;
                    showOriginal(false);
                    panel.style.display = 'none';
                    foot.style.display = 'none';
                    reset();
                });

                bar.appendChild(originalButton);
            }

            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'imai-btn';
            button.textContent = LABELS[action];

            if (action === 'ask') {
                // Opens the question box rather than calling anything: there is nothing to ask yet.
                buildAskRow();
                button.addEventListener('click', function () {
                    var opening = askRow.style.display === 'none';
                    askRow.style.display = opening ? 'flex' : 'none';
                    button.classList.toggle('is-active', opening);
                    if (opening) {
                        askInput.focus();
                    }
                });
            } else {
                button.addEventListener('click', function () { run(action, button); });
            }

            buttons.push(button);
            bar.appendChild(button);
        });

        if (buttons.length === 0) {
            return;
        }

        card.appendChild(bar);

        if (askRow) {
            card.appendChild(askRow);
        }

        card.appendChild(panel);
        card.appendChild(foot);
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

        // One bar for the search, placed above the results. Every action now answers about the
        // search term across the whole site, so a bar on each result would repeat itself - and it
        // needs no re-scan when Load More appends more results.
        var mount = document.createElement('div');
        mount.className = 'imai-bar';
        list.parentNode.insertBefore(mount, list);

        decorate(mount, config);
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
