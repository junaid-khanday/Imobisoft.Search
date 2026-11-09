/*
    Places the search page inside the site's own content area.

    The package renders search into a page of the site's - that is how it gets the site's header,
    footer and navigation - but it can only be added at the end of that page's <body>, because a
    library adding markup to a view it does not own has nowhere else to put it. So it arrives hidden
    at the foot of the page and this moves it to where the page's own content is, then shows it.
    Nothing flashes at the bottom first, and a visitor with scripting off still gets the page: the
    markup is there either way, and a <noscript> rule reveals it where it stands.

    On a page the package recognised as the search page the site already had, data-replace also
    arrives: the site's own search form and results, which the page has already rendered, are stood
    down so the visitor is looking at one search rather than two.
*/
(function () {
    'use strict';

    var HOST_ID = 'imobisoft-search-page';

    /*
        The package's own markup, which must never be stood down by data-replace. The injected page
        is a search form and a list of results, so a selector written for the site's would match it
        too - and the one element the page cannot do without is the one it would remove first.
    */
    var OURS = '[class*="imobisoft"], [id*="imobisoft"], [class*="isb-"], [id*="isb-"]';

    function reveal(host) {
        host.removeAttribute('hidden');
    }

    /*
        Undoes a stand-down on anything the search page ended up inside.

        A site's content area and its search results can be the same element - a <main
        class="search-results"> - and data-replace is applied before the page is placed, so the
        element matched as the site's search can turn out to be the one holding ours. Hiding that
        would hide the search page with it, which is the one thing this must never do.
    */
    function restore(host) {
        for (var node = host.parentElement; node; node = node.parentElement) {
            if (!node.hasAttribute('data-imobisoft-replaced')) {
                continue;
            }

            node.removeAttribute('data-imobisoft-replaced');
            node.removeAttribute('hidden');
            node.removeAttribute('aria-hidden');
            node.style.removeProperty('display');
        }
    }

    /*
        Stands the site's own search down: hidden from the page and from assistive technology, but
        left in the document. Not removed, because a site's own script may still hold a reference to
        its form and removing it would throw where hiding it does not.
    */
    function standDown(element) {
        element.setAttribute('hidden', 'hidden');
        element.setAttribute('aria-hidden', 'true');
        element.setAttribute('data-imobisoft-replaced', 'true');

        // Against a site stylesheet with a rule more specific than [hidden], which is most of them.
        element.style.setProperty('display', 'none', 'important');
    }

    /*
        Whether this is the site's own search markup rather than the package's, or something holding
        the package's. Removing an ancestor of the injected page would take the page with it.
    */
    function theirs(element, host) {
        if (element === host || element.contains(host) || host.contains(element)) {
            return false;
        }

        if (element.matches(OURS) || element.closest(OURS)) {
            return false;
        }

        // Never the page's furniture: a header holding the site's own search box is navigation, and
        // the box in it is the one the package has already turned into its own.
        return !element.closest('header, nav, footer, [role="banner"], [role="navigation"]');
    }

    /*
        Stands down whatever the server named as the site's own search on this page. Scoped to the
        document rather than to the content area on purpose: a site's search form often sits just
        outside the element its results are in, and both belong to the same search.
    */
    function replace(host) {
        var selectors = (host.getAttribute('data-replace') || '').split(',');

        for (var i = 0; i < selectors.length; i++) {
            var selector = selectors[i].trim();

            if (!selector) {
                continue;
            }

            var matches;

            try {
                matches = document.querySelectorAll(selector);
            } catch (err) {
                // A selector typed in the backoffice can be invalid, and so can one this script
                // ships - [action*="search" i] is not understood by every browser. Skip it rather
                // than giving up on the rest of the list.
                continue;
            }

            for (var m = 0; m < matches.length; m++) {
                if (theirs(matches[m], host)) {
                    standDown(matches[m]);
                }
            }
        }
    }

    /*
        Whether this element is the page's content area rather than its furniture.

        A <main> wrapped around the site's header is not where a page goes, and sites do that more
        often than you would think - a header partial that opens its own <main> leaves the document
        with two of them, the first holding the header and the nav. Picking that one would put the
        search page inside the header. An element holding a header, a nav or a footer is not the
        content area, whatever it is called.
    */
    function isContent(element) {
        return !element.querySelector('header, nav, footer, [role="banner"], [role="navigation"]');
    }

    function place(host) {
        var selectors = (host.getAttribute('data-container') || '').split(',');

        for (var i = 0; i < selectors.length; i++) {
            var selector = selectors[i].trim();

            if (!selector) {
                continue;
            }

            var matches;

            try {
                matches = document.querySelectorAll(selector);
            } catch (err) {
                // A selector typed in the backoffice can be invalid. Skip it rather than giving up
                // on the rest of the list.
                continue;
            }

            var fallback = null;

            for (var m = 0; m < matches.length; m++) {
                var target = matches[m];

                // Never into itself or into something it already holds, which would detach the page
                // from the document.
                if (target === host || host.contains(target)) {
                    continue;
                }

                if (isContent(target)) {
                    target.appendChild(host);

                    return;
                }

                // Kept in case this selector matches nothing better. A selector someone typed in
                // the backoffice is an instruction, so it is still honoured ahead of the next one
                // in the list - just not ahead of a content area the same selector also found.
                if (!fallback) {
                    fallback = target;
                }
            }

            if (fallback) {
                fallback.appendChild(host);

                return;
            }
        }

        // Nothing matched. The page stays where it is rather than not being shown: a search page at
        // the foot of the site's page is still a working search page, and the container selector in
        // Search → Settings is there to fix it.
    }

    function start() {
        var host = document.getElementById(HOST_ID);

        if (!host) {
            return;
        }

        // Before placing, so the content area is measured and filled once rather than reflowing
        // from "the site's search plus ours" to "ours".
        replace(host);

        place(host);
        restore(host);
        reveal(host);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
