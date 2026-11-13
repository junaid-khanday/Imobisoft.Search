/*
    Type-ahead for the site's search boxes.

    Type "ap" and a list drops down with the words the site actually has - the same engine the
    backoffice test panel uses, reached through the package's own endpoint.

    It attaches to any search input the package rendered, and to any input a site marks with
    data-imobisoft-suggest, so a search box in a site's own template can opt in without code.

    Nothing here decides what counts as a suggestion: the endpoint answers from what the search
    profile can already see in the index, so this shows only what a search would have found anyway.
*/
(function () {
    'use strict';

    var script = document.currentScript;
    if (!script) {
        return;
    }

    var config = {
        endpoint: script.getAttribute('data-suggest-endpoint') || '',
        minimum: parseInt(script.getAttribute('data-suggest-minimum'), 10) || 2,
        take: parseInt(script.getAttribute('data-suggest-take'), 10) || 8,
        stylesheet: script.getAttribute('data-stylesheet') || '',

        // Inputs to attach to. The package's own boxes carry data-isb-input or the search page's id;
        // a site opts its own box in with data-imobisoft-suggest.
        selector: '[data-isb-input], [data-imobisoft-suggest], #imobisoft-search-page-input'
    };

    // Enough of a wait that a typist is not firing a request per keystroke, short enough that the
    // list feels like it is keeping up.
    var DEBOUNCE_MS = 160;

    function attach(input) {
        if (!config.endpoint || input.dataset.imobisoftSuggestBound) {
            return;
        }

        input.dataset.imobisoftSuggestBound = 'true';
        input.setAttribute('autocomplete', 'off');

        var list = document.createElement('ul');
        list.className = 'imsg-list';
        list.setAttribute('role', 'listbox');
        list.style.display = 'none';

        // Positioned against the input's own container, so the list lines up with the box wherever
        // the box happens to be.
        var anchor = input.parentNode;

        try {
            if (window.getComputedStyle && window.getComputedStyle(anchor).position === 'static') {
                anchor.style.position = 'relative';
            }
        } catch (e) {
            anchor.style.position = 'relative';
        }

        anchor.appendChild(list);

        var items = [];
        var active = -1;
        var timer = null;
        var inFlight = null;
        var lastQuery = '';

        function hide() {
            list.style.display = 'none';
            list.textContent = '';
            items = [];
            active = -1;
            input.setAttribute('aria-expanded', 'false');
        }

        function choose(index) {
            if (index < 0 || index >= items.length) {
                return;
            }

            input.value = items[index].term;
            hide();

            // Submitting is what the visitor wanted - the suggestion is a search, not a spelling fix.
            var form = input.form || input.closest('form');

            if (form) {
                form.submit();
            }
        }

        function highlight(index) {
            active = index;

            Array.prototype.forEach.call(list.children, function (child, i) {
                child.classList.toggle('is-active', i === index);
                child.setAttribute('aria-selected', i === index ? 'true' : 'false');
            });
        }

        // Rendered as text: suggestions come from page content, so they are never treated as markup.
        function show(suggestions) {
            list.textContent = '';
            items = suggestions;

            if (suggestions.length === 0) {
                hide();
                return;
            }

            suggestions.forEach(function (suggestion, index) {
                var item = document.createElement('li');
                item.className = 'imsg-item';
                item.setAttribute('role', 'option');
                item.setAttribute('aria-selected', 'false');
                item.textContent = suggestion.term;

                item.addEventListener('mousedown', function (e) {
                    // mousedown rather than click: the input blurring first would hide the list
                    // before a click ever landed.
                    e.preventDefault();
                    choose(index);
                });

                item.addEventListener('mouseenter', function () { highlight(index); });

                list.appendChild(item);
            });

            list.style.display = 'block';
            input.setAttribute('aria-expanded', 'true');
        }

        function query() {
            var typed = input.value.trim();

            if (typed.length < config.minimum) {
                hide();
                return;
            }

            if (typed === lastQuery) {
                return;
            }

            lastQuery = typed;

            if (inFlight) {
                inFlight.abort();
            }

            var controller = new AbortController();
            inFlight = controller;

            fetch(config.endpoint + '?term=' + encodeURIComponent(typed) + '&take=' + config.take,
                { headers: { 'Accept': 'application/json' }, signal: controller.signal })
                .then(function (response) { return response.ok ? response.json() : []; })
                .then(function (data) {
                    if (!controller.signal.aborted) {
                        show(Array.isArray(data) ? data : []);
                    }
                })
                .catch(function (err) {
                    if (!err || err.name !== 'AbortError') {
                        hide();
                    }
                })
                .finally(function () {
                    if (inFlight === controller) {
                        inFlight = null;
                    }
                });
        }

        input.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(query, DEBOUNCE_MS);
        });

        input.addEventListener('keydown', function (e) {
            if (list.style.display === 'none') {
                return;
            }

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                highlight(active + 1 >= items.length ? 0 : active + 1);
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                highlight(active - 1 < 0 ? items.length - 1 : active - 1);
            } else if (e.key === 'Enter' && active > -1) {
                // Only when something is highlighted; otherwise the form submits what was typed.
                e.preventDefault();
                choose(active);
            } else if (e.key === 'Escape' || e.key === 'Esc') {
                hide();
            }
        });

        input.addEventListener('blur', function () {
            // Delayed, so a mousedown on an item is still able to land.
            setTimeout(hide, 120);
        });
    }

    // The list's styling lives in searchbox.css, normally already in the page. This is the fallback
    // for a layout with no <head> for the package to have written the link into, and for a site with
    // the header box turned off, where the box script that would otherwise do this is not there.
    function ensureStylesheet() {
        if (!config.stylesheet || document.getElementById('imobisoft-searchbox-css')) {
            return;
        }

        var link = document.createElement('link');
        link.id = 'imobisoft-searchbox-css';
        link.rel = 'stylesheet';
        link.href = config.stylesheet;
        document.head.appendChild(link);
    }

    function run() {
        ensureStylesheet();
        document.querySelectorAll(config.selector).forEach(attach);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', run);
    } else {
        run();
    }

    // The header box arrives after load, so a pass can be asked for once it is in place.
    window.imobisoftSearchSuggest = { refresh: run };
})();
