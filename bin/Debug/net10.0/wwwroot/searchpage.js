/*
    Places the search page inside the site's own content area.

    The package renders search into a page of the site's - that is how it gets the site's header,
    footer and navigation - but it can only be added at the end of that page's <body>, because a
    library adding markup to a view it does not own has nowhere else to put it. So it arrives hidden
    at the foot of the page and this moves it to where the page's own content is, then shows it.
    Nothing flashes at the bottom first, and a visitor with scripting off still gets the page: the
    markup is there either way, and a <noscript> rule reveals it where it stands.
*/
(function () {
    'use strict';

    var HOST_ID = 'imobisoft-search-page';

    function reveal(host) {
        host.removeAttribute('hidden');
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

        place(host);
        reveal(host);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
