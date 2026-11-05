/*
    Turns a site's existing search link into a search box that opens where the visitor already is.

    The package injects this file on every page (see HeaderSearchBoxTagHelperComponent), so a
    consuming site needs no markup, no view change and no code. What it does on load:

      1. finds the links in a site's header or nav that point at a search page,
      2. leaves each link exactly as it is - the site's own styling, text and icon stay - and fetches
         the search box from the package, rendered from Views/Partials/Search/HeaderSearchBox.cshtml,
      3. drops that markup in beside the link and runs whatever script it brought with it,
      4. the first click opens the box instead of navigating; the next, with a term typed, submits.

    The box's markup, styling and behaviour all live in that one view, so editing it - in the project
    or from the Search section - is what changes the header box.

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

    var config = {
        containers: script.getAttribute('data-containers') || 'header, nav, [role="banner"], [role="navigation"]',
        queryName: script.getAttribute('data-query') || 'q',
        boxEndpoint: script.getAttribute('data-box-endpoint') || '',
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

    /*
        Inserts the header box next to the site's search link.

        The markup comes from the server, rendered from Views/Partials/Search/HeaderSearchBox.cshtml
        - that is what makes editing that one file change the header box. This script used to build
        the box in the DOM, which meant the view only mattered to a site that placed the view
        component by hand.

        The view owns its own markup, styling and behaviour. All this does is fetch it, put it in
        place, run whatever script it brought with it, and point the site's own link at it.
    */
    function enhance(link, target) {
        link.dataset.imobisoftSearchBound = 'true';

        var url = config.boxEndpoint
            + '?resultsUrl=' + encodeURIComponent(target.pathname + (target.search || ''))
            + '&term=' + encodeURIComponent(currentTerm());

        fetch(url, { headers: { 'Accept': 'text/html' } })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.text();
            })
            .then(function (html) { insert(link, html); })
            .catch(function (err) {
                // The link is left exactly as it was, so search still works the old way.
                console.error('Imobisoft.Search: could not load the header search box.', err);
            });
    }

    function currentTerm() {
        try {
            return new URLSearchParams(window.location.search).get(config.queryName) || '';
        } catch (e) {
            return '';
        }
    }

    /*
        The element the bar covers. The nearest ancestor matching the configured containers - the
        site's own header or nav - so the bar is exactly as wide as the header is. Falls back to the
        link's parent when the link was opted in from outside any of them.
    */
    function hostFor(link) {
        if (typeof link.closest === 'function' && config.containers) {
            try {
                var match = link.closest(config.containers);
                if (match) {
                    return match;
                }
            } catch (e) {
                // A selector list the browser dislikes is not worth failing over.
            }
        }

        return link.parentNode;
    }

    function insert(link, html) {
        var holder = document.createElement('span');
        holder.className = 'imsx-holder';
        holder.innerHTML = html;

        var root = holder.querySelector('[data-imobisoft-searchbox]');

        if (!root) {
            console.error('Imobisoft.Search: the header search box view rendered no '
                + '[data-imobisoft-searchbox] element, so there is nothing to show or toggle.');
            return;
        }

        // Mounted into the header itself rather than beside the link, so the bar can span the full
        // width of it and cover what is there. Inline beside the link it could only ever be as wide
        // as the gap next to the link, and would push the nav around.
        var host = hostFor(link);

        // The overlay is positioned against the header, so the header has to be a positioning
        // context. Most are not, and setting it is harmless - it changes nothing on its own.
        try {
            if (window.getComputedStyle && window.getComputedStyle(host).position === 'static') {
                host.style.position = 'relative';
            }
        } catch (e) {
            host.style.position = 'relative';
        }

        holder.setAttribute('data-open', 'false');
        host.appendChild(holder);

        // Scripts set through innerHTML never execute, so each one is re-created. The view carries
        // its own behaviour, so without this the box would render and then do nothing.
        holder.querySelectorAll('script').forEach(function (original) {
            var copy = document.createElement('script');

            Array.prototype.forEach.call(original.attributes, function (attr) {
                copy.setAttribute(attr.name, attr.value);
            });

            copy.text = original.textContent;
            original.parentNode.replaceChild(copy, original);
        });

        bind(link, root);

        // The box arrives after load, so the type-ahead has to be told there is a new input.
        if (window.imobisoftSearchSuggest && typeof window.imobisoftSearchSuggest.refresh === 'function') {
            window.imobisoftSearchSuggest.refresh();
        }
    }

    /*
        Points the site's existing link at the box: first press opens it, a second press with a term
        typed submits, and a second press on an empty box closes it again. The view is expected to
        hide itself while data-open is "false" - the one shipped does, in its own stylesheet.
    */
    function bind(link, root) {
        var form = root.querySelector('[data-isb-form]') || root.querySelector('form');
        var input = root.querySelector('[data-isb-input]') || root.querySelector('input[type="search"], input[type="text"]');

        function isOpen() {
            return root.getAttribute('data-open') === 'true';
        }

        // The wrapper is the overlay, so left showing it would keep covering the header and
        // swallowing clicks even with the bar itself hidden.
        function mirror(open) {
            if (root.parentNode && root.parentNode.classList
                && root.parentNode.classList.contains('imsx-holder')) {
                root.parentNode.setAttribute('data-open', open ? 'true' : 'false');
            }

            link.setAttribute('aria-expanded', open ? 'true' : 'false');
        }

        function setOpen(open) {
            root.setAttribute('data-open', open ? 'true' : 'false');
            mirror(open);
        }

        link.setAttribute('aria-expanded', isOpen() ? 'true' : 'false');

        if (input && input.id) {
            link.setAttribute('aria-controls', input.id);
        }

        link.addEventListener('click', function (e) {
            // Modified clicks are left alone, so "open in new tab" still reaches the search page.
            if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) {
                return;
            }

            e.preventDefault();

            if (!isOpen()) {
                setOpen(true);
                if (input) { input.focus(); }
                return;
            }

            if (input && !input.value.trim()) {
                setOpen(false);
                return;
            }

            if (form) { form.submit(); }
        });

        // An empty box left open is clutter, so clicking away closes it. A typed term is kept.
        document.addEventListener('click', function (e) {
            if (!isOpen() || root.contains(e.target) || link.contains(e.target)) {
                return;
            }

            if (!input || !input.value.trim()) {
                setOpen(false);
            }
        });

        /*
            Mirrors data-open from the view onto the overlay, whoever set it.

            This is what keeps the contract for a view down to one thing: toggle data-open on your
            own root. Without it a view would also have to know about the package's overlay and
            close that too - and a view saved before the overlay existed would leave it covering the
            header, hiding the site's nav and swallowing clicks even with the bar itself hidden.

            It mirrors only, and never writes data-open back onto the root it is watching: setting an
            attribute counts as a change even when the value is the same, so writing back would
            re-trigger this observer forever and take the page's main thread with it.
        */
        if (typeof MutationObserver === 'function') {
            new MutationObserver(function () {
                mirror(isOpen());
            }).observe(root, { attributes: true, attributeFilter: ['data-open'] });
        }

        // On the results page the box opens showing the term that produced it.
        if (input && input.value.trim()) {
            setOpen(true);
        }
    }

    function run() {
        if (!config.boxEndpoint) {
            // Without the endpoint there is nothing to render the box from, and building one here
            // would put the look of it back in JavaScript where a view cannot reach it.
            return;
        }

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
