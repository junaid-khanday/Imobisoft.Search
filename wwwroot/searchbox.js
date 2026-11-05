/*
    Turns a site's existing search link into a search box that opens where the visitor already is.

    The package injects this file on every page (see HeaderSearchBoxTagHelperComponent), so a
    consuming site needs no markup, no view change and no code. What it does on load:

      1. finds the links in a site's header or nav that point at a search page,
      2. leaves each link exactly as it is - the site's own styling, text and icon stay - and hangs
         a search box off it,
      3. the first click opens that box instead of navigating,
      4. the next click, with a term typed, submits to the link's own URL as ?q=<term>.

    Without this file the link still works: it goes straight to the search page, as it always did.

    Opt a single link out with data-imobisoft-search="false". Opt one in - a link outside the
    containers that are searched, or whose URL does not look like a search page - by adding a bare
    data-imobisoft-search attribute to it.
*/
(function () {
    'use strict';

    var script = document.currentScript;
    if (!script) {
        return;
    }

    var counter = 0;

    var config = {
        containers: script.getAttribute('data-containers') || 'header, nav, [role="banner"], [role="navigation"]',
        placeholder: script.getAttribute('data-placeholder') || 'Search...',
        queryName: script.getAttribute('data-query') || 'q',
        label: script.getAttribute('data-label') || 'Search',
        icon: script.getAttribute('data-icon') || '',
        iconSize: parseInt(script.getAttribute('data-icon-size'), 10) || 18,
        stylesheet: script.getAttribute('data-stylesheet') || '',
        paths: pathList('data-paths'),
        excluded: pathList('data-excluded')
    };

    function pathList(name) {
        return (script.getAttribute(name) || '')
            .split(',')
            .map(function (value) { return normalisePath(value); })
            .filter(Boolean);
    }

    function normalisePath(path) {
        var value = String(path || '').trim().toLowerCase();

        var marker = value.search(/[?#]/);
        if (marker > -1) {
            value = value.slice(0, marker);
        }

        if (value.length > 1 && value.charAt(value.length - 1) === '/') {
            value = value.slice(0, -1);
        }

        return value;
    }

    /*
        A path is a search page when the last segment is the word "search" on its own or joined to
        other words by hyphens: /search, /imobisoft-search, /search-results all match. This looks at
        hyphen-separated words rather than for the substring, which is what keeps /research and
        /research-centre out of it.
    */
    function looksLikeSearchPage(path) {
        var value = normalisePath(path);
        if (!value) {
            return false;
        }

        if (config.paths.indexOf(value) > -1) {
            return true;
        }

        var segments = value.split('/').filter(Boolean);
        if (!segments.length) {
            return false;
        }

        return segments[segments.length - 1].split('-').indexOf('search') > -1;
    }

    // The stylesheet is normally in the page already. This is the fallback for a layout with no
    // <head> for the package to write into.
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

    /*
        The icon on the box's own button. A site that named a file of its own gets an <img>, which
        renders a .svg, .png, .webp or .gif alike and keeps the file's own colours. With nothing
        named, the magnifier below is drawn inline so it needs no request and inherits the button's
        colour through currentColor.

        The file is always rendered as an image, never as markup, so a path entered in the
        backoffice cannot put script into the page.
    */
    function buildIcon() {
        if (config.icon) {
            var img = document.createElement('img');
            img.className = 'imsx-go-icon';
            img.src = config.icon;
            img.alt = '';
            img.setAttribute('aria-hidden', 'true');
            img.width = config.iconSize;
            img.height = config.iconSize;
            return img;
        }

        var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('fill', 'none');
        svg.setAttribute('stroke', 'currentColor');
        svg.setAttribute('stroke-width', '2');
        svg.setAttribute('stroke-linecap', 'round');
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');

        var circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        circle.setAttribute('cx', '11');
        circle.setAttribute('cy', '11');
        circle.setAttribute('r', '7');

        var handle = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        handle.setAttribute('x1', '16.5');
        handle.setAttribute('y1', '16.5');
        handle.setAttribute('x2', '21');
        handle.setAttribute('y2', '21');

        svg.appendChild(circle);
        svg.appendChild(handle);

        return svg;
    }

    function collect() {
        var links = [];

        function add(link) {
            if (links.indexOf(link) < 0) {
                links.push(link);
            }
        }

        document.querySelectorAll(config.containers).forEach(function (container) {
            container.querySelectorAll('a[href]').forEach(add);
        });

        document.querySelectorAll('a[href][data-imobisoft-search]').forEach(add);

        return links;
    }

    function resolve(link) {
        var href = link.getAttribute('href') || '';

        // An in-page jump has the current path, which on a search page would match every heading
        // link on it.
        if (!href || href.charAt(0) === '#') {
            return null;
        }

        try {
            var target = new URL(href, window.location.href);

            if (target.protocol !== 'http:' && target.protocol !== 'https:') {
                return null;
            }

            // Only this site's own search page can be driven by a box here.
            if (target.origin !== window.location.origin) {
                return null;
            }

            return target;
        } catch (e) {
            return null;
        }
    }

    function shouldEnhance(link, target) {
        if (!target || link.dataset.imobisoftSearchBound) {
            return false;
        }

        if (link.getAttribute('data-imobisoft-search') === 'false') {
            return false;
        }

        // An excluded page never gets a box, however well it matches - including when a link to it
        // has been opted in. "Never" has to mean never, or the setting is not worth having.
        if (config.excluded.indexOf(normalisePath(target.pathname)) > -1) {
            return false;
        }

        if (link.hasAttribute('data-imobisoft-search')) {
            return true;
        }

        return looksLikeSearchPage(target.pathname);
    }

    function enhance(link, target) {
        link.dataset.imobisoftSearchBound = 'true';

        var inputId = 'imsx-' + (++counter);

        var wrap = document.createElement('span');
        wrap.className = 'imsx';
        wrap.setAttribute('data-imobisoft-searchbox', '');
        wrap.setAttribute('data-open', 'false');
        wrap.style.setProperty('--imsx-icon-size', config.iconSize + 'px');

        var form = document.createElement('form');
        form.className = 'imsx-form';
        form.setAttribute('role', 'search');
        form.method = 'get';
        form.action = target.pathname;

        // Visibility is owned here rather than in the stylesheet, so the panel cannot be caught
        // showing while searchbox.css is still on its way.
        form.style.display = 'none';

        // A GET submit replaces the whole query string, so anything already on the link is put
        // back as hidden fields instead of being silently dropped.
        target.searchParams.forEach(function (value, key) {
            if (key === config.queryName) {
                return;
            }

            var hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = key;
            hidden.value = value;
            form.appendChild(hidden);
        });

        var input = document.createElement('input');
        input.type = 'search';
        input.id = inputId;
        input.name = config.queryName;
        input.className = 'imsx-input';
        input.placeholder = config.placeholder;
        input.autocomplete = 'off';
        input.setAttribute('aria-label', (link.textContent || '').trim() || config.label);

        var go = document.createElement('button');
        go.type = 'submit';
        go.className = 'imsx-go';
        go.setAttribute('aria-label', config.label);
        go.appendChild(buildIcon());

        form.appendChild(input);
        form.appendChild(go);

        // The link stays put and keeps every class, icon and label the site gave it: it is still
        // the control the visitor presses, it just no longer leaves the page on the first press.
        link.parentNode.insertBefore(wrap, link);
        wrap.appendChild(link);
        wrap.appendChild(form);

        link.setAttribute('aria-expanded', 'false');
        link.setAttribute('aria-controls', inputId);

        function isOpen() {
            return wrap.getAttribute('data-open') === 'true';
        }

        function setOpen(open) {
            wrap.setAttribute('data-open', open ? 'true' : 'false');
            link.setAttribute('aria-expanded', open ? 'true' : 'false');
            form.style.display = open ? 'flex' : 'none';

            // A link in the left half of the window has no room for a right-aligned panel.
            if (open) {
                var box = wrap.getBoundingClientRect();
                wrap.setAttribute('data-align', box.left < window.innerWidth / 2 ? 'left' : 'right');
            }
        }

        link.addEventListener('click', function (e) {
            // Modified clicks are left alone, so "open in new tab" still reaches the search page.
            if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) {
                return;
            }

            e.preventDefault();

            // First press opens the box: the visitor stays on the page they are on.
            if (!isOpen()) {
                setOpen(true);
                input.focus();
                return;
            }

            // Open with nothing typed - a second press is a dismiss, not a search for "".
            if (!input.value.trim()) {
                setOpen(false);
                return;
            }

            // Open with a term: this press is the search, and it lands on the results page.
            form.submit();
        });

        // Enter inside the box submits natively, so the empty case is guarded here too.
        form.addEventListener('submit', function (e) {
            if (!input.value.trim()) {
                e.preventDefault();
                input.focus();
            }
        });

        input.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' || e.key === 'Esc') {
                setOpen(false);
                link.focus();
            }
        });

        // An empty box left open is just clutter. A typed term is kept, in case the click was
        // somewhere harmless.
        document.addEventListener('click', function (e) {
            if (!isOpen() || wrap.contains(e.target) || input.value.trim()) {
                return;
            }

            setOpen(false);
        });

        // On the results page itself the box opens showing the term that produced it, ready to be
        // changed rather than retyped.
        if (normalisePath(window.location.pathname) === normalisePath(target.pathname)) {
            var current = new URLSearchParams(window.location.search).get(config.queryName);
            if (current) {
                input.value = current;
                setOpen(true);
            }
        }
    }

    function run() {
        ensureStylesheet();

        collect().forEach(function (link) {
            var target = resolve(link);
            if (shouldEnhance(link, target)) {
                enhance(link, target);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', run);
    } else {
        run();
    }

    window.imobisoftSearchBox = {
        // For a site whose header is rendered after load: asks for another pass.
        refresh: run,

        // Answers whether a path is one the box would attach itself to, which is the quickest way
        // to check a site's own URLs against the rule.
        matches: looksLikeSearchPage
    };
})();
