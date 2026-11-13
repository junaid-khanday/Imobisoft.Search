/*
    AI on a search: the text tool buttons, the answer card above the results, and the suggested
    searches below them.

    The package renders the results list with its AI settings on it as data attributes; this script
    reads those and builds from them. The page the visitor is on is never left.

    Where the markup does not mention a feature at all, the server is asked instead. That is not
    belt and braces - the search page is a view sites are invited to take over, and a site's own
    copy carries whichever attributes it was copied with. Reading configuration only from the markup
    would mean every new AI feature arrived switched off on every site that has ever customised the
    page, with no way to tell from the backoffice that it had.

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
        // The two the page asks for by itself. Absent is not the same as false: the search page is
        // a view sites are invited to take over, and a site's own copy carries whichever attributes
        // it was copied with. Absent means "this markup predates these features, ask the server";
        // false means "the server said no".
        var answerAttr = list.getAttribute('data-imobisoft-ai-answer');
        var relatedAttr = list.getAttribute('data-imobisoft-ai-related');

        return {
            endpoint: list.getAttribute('data-imobisoft-ai-endpoint') || '',
            actions: split(list.getAttribute('data-imobisoft-ai-actions')),
            languages: split(list.getAttribute('data-imobisoft-ai-languages')),
            profile: list.getAttribute('data-imobisoft-ai-profile') || '',
            term: list.getAttribute('data-imobisoft-ai-term') || '',
            questionLimit: parseInt(list.getAttribute('data-imobisoft-ai-question-limit'), 10) || 300,

            answer: answerAttr === 'true',
            related: relatedAttr === 'true',
            askServer: answerAttr === null && relatedAttr === null,

            // Where a suggested search goes. The page's own form posts here, so a suggestion has
            // to as well or it would leave the page the visitor is searching on.
            page: list.getAttribute('data-imobisoft-ai-page') || window.location.pathname
        };
    }

    /*
        Asks the server what is switched on, for a page whose markup is too old to say.

        Resolves to null on any failure. Nothing here is load bearing: a page that cannot reach this
        keeps the buttons its own markup asked for, which is what it had before.
    */
    function fetchState(config) {
        var url = config.endpoint + '/state?q=' + encodeURIComponent(config.term)
            + '&profile=' + encodeURIComponent(config.profile);

        return fetch(url, { headers: { 'Accept': 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : null; })
            .catch(function () { return null; });
    }

    /*
        States what was done to the visitor's query, and links to the search they actually typed.

        Normally the page renders this itself. This is for a site whose own copy of the page has no
        markup for it: a query quietly answered as a different query is the failure mode of query
        understanding, and the way out of it must not depend on which version of a view a site
        happens to have copied.
    */
    function noticeFor(slot, config, notice) {
        if (!notice || !notice.original) {
            return;
        }

        // The page may already have said it. Two notices saying the same thing is worse than one.
        if (document.querySelector('.imobisoft-ai-notice, .imai-notice')) {
            return;
        }

        var rewritten = notice.term && notice.term !== notice.original;
        var filters = notice.filters || [];

        if (!rewritten && filters.length === 0) {
            return;
        }

        var wrap = document.createElement('div');
        wrap.className = 'imai-notice';

        var said = document.createElement('span');

        if (rewritten) {
            said.appendChild(document.createTextNode('Showing results for “'));

            var shown = document.createElement('strong');
            shown.textContent = notice.term;
            said.appendChild(shown);

            said.appendChild(document.createTextNode('”'));
        } else {
            said.appendChild(document.createTextNode('Showing results narrowed from your search'));
        }

        filters.forEach(function (applied) {
            said.appendChild(document.createTextNode(' · '));

            var strong = document.createElement('strong');
            strong.textContent = applied;
            said.appendChild(strong);
        });

        wrap.appendChild(said);

        var undo = document.createElement('a');
        undo.className = 'imai-notice-undo';
        undo.href = config.page + '?q=' + encodeURIComponent(notice.original) + '&ai=0';
        undo.textContent = 'Search instead for “' + notice.original + '”';
        wrap.appendChild(undo);

        slot.appendChild(wrap);
    }

    /*
        One call to the endpoint. Shared by the buttons and by the two panels the page fetches for
        itself, so there is one place that knows the shape of the request and of the reply.
    */
    function ask(config, body, signal) {
        return fetch(config.endpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(body),
            signal: signal
        })
            .then(function (response) {
                return response.json()
                    .catch(function () { return {}; })
                    .then(function (data) { return { ok: response.ok, data: data }; });
            });
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


    /*
        The answer card: this site's answer to the search, above the results.

        Fetched rather than rendered with the page, so the results are never held up by a provider.
        It removes itself when the server has nothing to say - an empty reply is the server agreeing
        that the list of results is the better answer, and a panel announcing that it has no answer
        is worse than no panel.
    */
    function answerCard(slot, config) {
        var card = document.createElement('section');
        card.className = 'imai-answer is-loading';
        card.setAttribute('aria-live', 'polite');
        card.setAttribute('aria-busy', 'true');

        var heading = document.createElement('div');
        heading.className = 'imai-answer-head';
        heading.textContent = 'AI answer';
        card.appendChild(heading);

        var panel = document.createElement('div');
        panel.className = 'imai-answer-body';

        var waiting = document.createElement('p');
        waiting.className = 'imai-waiting';
        waiting.textContent = 'Looking for an answer on this site…';
        panel.appendChild(waiting);

        card.appendChild(panel);
        slot.appendChild(card);

        ask(config, { action: 'overview', term: config.term, profile: config.profile })
            .then(function (result) {
                card.classList.remove('is-loading');
                card.removeAttribute('aria-busy');

                if (!result.ok || !result.data || !result.data.text) {
                    card.remove();
                    return;
                }

                render(panel, result.data.text);

                if (result.data.sources && result.data.sources.length > 0) {
                    panel.appendChild(sourceList(result.data.sources));
                }

                var foot = document.createElement('div');
                foot.className = 'imai-answer-foot';
                foot.textContent = result.data.cached
                    ? 'AI generated from pages on this site · reused from an earlier search'
                    : 'AI generated from pages on this site · may contain mistakes';
                card.appendChild(foot);
            })
            .catch(function () {
                // Nothing is broken from the visitor's point of view: the results are on the page
                // and this was an extra. It goes away rather than reporting itself.
                card.remove();
            });
    }

    /*
        Searches worth trying next, as links back into this same page. Drawn from the pages this
        search actually found, so a suggestion leads somewhere rather than to an empty result list.
    */
    function relatedSearches(list, config) {
        ask(config, { action: 'relatedSearches', term: config.term, profile: config.profile })
            .then(function (result) {
                if (!result.ok || !result.data || !result.data.text) {
                    return;
                }

                var terms = String(result.data.text)
                    .split('\n')
                    .map(function (line) {
                        // A model told not to use bullets or numbering will occasionally use them
                        // anyway, and a stray "- " in a link is worse than a wasted character.
                        return line.replace(/^\s*[-*•\d.)\s]+/, '').replace(/^["']|["']$/g, '').trim();
                    })
                    .filter(function (line) {
                        return line.length > 1 && line.length <= 80;
                    })
                    .slice(0, 5);

                if (terms.length === 0) {
                    return;
                }

                var wrap = document.createElement('nav');
                wrap.className = 'imai-related';
                wrap.setAttribute('aria-label', 'Related searches');

                var caption = document.createElement('span');
                caption.className = 'imai-related-label';
                caption.textContent = 'Try searching for';
                wrap.appendChild(caption);

                terms.forEach(function (term) {
                    var link = document.createElement('a');
                    link.className = 'imai-related-link';
                    link.href = config.page + '?q=' + encodeURIComponent(term);
                    link.textContent = term;
                    wrap.appendChild(link);
                });

                // After the results: these are where to go once this list has been read, not
                // instead of reading it.
                list.parentNode.insertBefore(wrap, list.nextSibling);
            })
            .catch(function () {
                // Same as the answer card - an extra that quietly does not appear.
            });
    }

    /*
        Three empty slots above the results, in reading order, created before anything is put in
        them.

        Without them the order on the page would depend on what arrives first: the notice, the card
        and the button bar are each inserted immediately before the results list, so whichever is
        built last would end up nearest it. On a page whose markup predates these features the card
        and the notice arrive a round trip after the bar, which would put them underneath it. Fixed
        slots mean the page reads the same whatever order things turn up in.
    */
    function slotsAbove(list) {
        var top = document.createElement('div');
        top.className = 'imai-top';

        var slots = {
            notice: document.createElement('div'),
            answer: document.createElement('div'),
            bar: document.createElement('div')
        };

        top.appendChild(slots.notice);
        top.appendChild(slots.answer);
        top.appendChild(slots.bar);

        list.parentNode.insertBefore(top, list);

        return slots;
    }

    function init(list) {
        if (list.dataset.imobisoftAiReady) {
            return;
        }

        list.dataset.imobisoftAiReady = 'true';

        var config = configFor(list);

        if (!config.endpoint) {
            return;
        }

        var slots = slotsAbove(list);

        // The buttons the markup asked for are built straight away. They worked before the state
        // endpoint existed and must not start depending on it.
        if (config.actions.length > 0) {
            var mount = document.createElement('div');
            mount.className = 'imai-bar';
            slots.bar.appendChild(mount);

            decorate(mount, config);
        }

        if (!config.askServer) {
            if (config.answer && config.term) {
                answerCard(slots.answer, config);
            }

            if (config.related && config.term) {
                relatedSearches(list, config);
            }

            return;
        }

        // This page's markup predates the answer card, so it cannot say whether one is wanted.
        fetchState(config).then(function (state) {
            if (!state || !state.enabled) {
                // AI is off for this profile, or no key has been added. The buttons the markup
                // asked for stay as they are; there is simply nothing further to add.
                return;
            }

            if (state.questionLimit) {
                config.questionLimit = state.questionLimit;
            }

            if (state.answer && config.term) {
                answerCard(slots.answer, config);
            }

            noticeFor(slots.notice, config, state.notice);

            if (state.related && config.term) {
                relatedSearches(list, config);
            }
        });
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
