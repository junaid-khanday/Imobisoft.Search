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

    /*
        Whether the visitor is already on a search page.

        The box is still put on the header here - the site's header is the same header on every page
        and its search link has to keep working - but it arrives closed and empty. The search page
        has a search box of its own, holding the term that produced the results, so a second bar
        across the header showing the same term is both redundant and in the way: it covers the
        header, and with a term in it neither clicking away nor pressing the link again will close
        it.
    */
    function onSearchPage() {
        return looksLikeSearchPage(window.location.pathname);
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

        // Opted in by attribute, and not necessarily a link: plenty of sites open their search
        // from a button, or from an <a> with no href that their own script listens to.
        document.querySelectorAll('[data-imobisoft-search]').forEach(add);

        return links;
    }

    /// Whether this element was opted in by hand, rather than found by looking like a search link.
    function optedIn(element) {
        return element.hasAttribute('data-imobisoft-search')
            && element.getAttribute('data-imobisoft-search') !== 'false';
    }

    /*
        The search page an element points at.

        An element opted in by attribute does not have to point anywhere - a header button has no
        href to read. The box still needs a page to submit to, so it falls back to the first search
        page the package was told about, which is the site's own search page where one is set and
        the package's own otherwise.
    */
    function resolve(link) {
        var href = link.getAttribute('href') || '';

        if (!href && optedIn(link)) {
            return configuredTarget();
        }

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

    function configuredTarget() {
        if (config.paths.length === 0) {
            return null;
        }

        try {
            return new URL(config.paths[0], window.location.href);
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

        if (optedIn(link)) {
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
    function enhance(links, target, host) {
        // A box already in this header is the box these links drive. Nothing is fetched and nothing
        // is added: one header, one bar.
        var existing = boxIn(host);

        if (existing) {
            links.forEach(function (link) { bind(link, existing); });

            return;
        }

        var url = config.boxEndpoint
            + '?resultsUrl=' + encodeURIComponent(target.pathname + (target.search || ''))

            // Nothing prefilled on the search page itself: an empty box stays closed, and closes
            // again when the visitor clicks away from it.
            + '&term=' + encodeURIComponent(onSearchPage() ? '' : currentTerm());

        fetch(url, { headers: { 'Accept': 'text/html' } })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.text();
            })
            .then(function (html) { insert(links, html, host); })
            .catch(function (err) {
                // The links are left exactly as they were, so search still works the old way.
                console.error('Imobisoft.Search: could not load the header search box.', err);
            });
    }

    /// The box already placed in this header, or null when it has none yet.
    function boxIn(host) {
        if (!host || typeof host.querySelector !== 'function') {
            return null;
        }

        var holder = host.querySelector(':scope > .imsx-holder');

        return holder ? holder.querySelector('[data-imobisoft-searchbox]') : null;
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

    function insert(links, html, host) {
        var holder = document.createElement('span');
        holder.className = 'imsx-holder';
        holder.innerHTML = html;

        var root = holder.querySelector('[data-imobisoft-searchbox]');

        if (!root) {
            console.error('Imobisoft.Search: the header search box view rendered no '
                + '[data-imobisoft-searchbox] element, so there is nothing to show or toggle.');
            return;
        }

        // The box is mounted into the header itself rather than beside the link, so the bar can span
        // the full width of it and cover what is there. Inline beside the link it could only ever be
        // as wide as the gap next to the link, and would push the nav around.

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

        // Every search link in this header drives the one box.
        links.forEach(function (link) { bind(link, root); });

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

        // On a page of the site's own that was reached from a search, the box opens showing the term
        // that produced it. Not on the search page itself, where the page's own box is already
        // showing that term - see onSearchPage.
        if (input && input.value.trim() && !onSearchPage()) {
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

        /*
            Grouped by the header each link sits in, because a header gets one box however many
            search links it holds. A site with a "Search" in its nav and a "Search results" in a
            secondary one - or, as happens while a search page is being set up, two search pages
            both in the navigation - would otherwise get a bar for each, stacked over the same
            header and all open at once.

            Grouped before anything is fetched rather than checked as each box lands: the fetches
            are concurrent, so two links in one header would both find no box there yet and both
            add one.

            A separate header still gets its own box - a site with a desktop header and a small
            screen one needs the box in both, and only ever shows one of them at a time.
        */
        var groups = [];

        collect().forEach(function (link) {
            var target = resolve(link);

            if (!shouldEnhance(link, target)) {
                return;
            }

            link.dataset.imobisoftSearchBound = 'true';

            var host = hostFor(link);
            var group = null;

            for (var i = 0; i < groups.length; i++) {
                if (groups[i].host === host) {
                    group = groups[i];
                    break;
                }
            }

            if (!group) {
                // The first search link in a header is the one its box is built for, so the box
                // submits to that link's search page.
                group = { host: host, target: target, links: [] };
                groups.push(group);
            }

            group.links.push(link);
        });

        groups.forEach(function (group) {
            enhance(group.links, group.target, group.host);
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
