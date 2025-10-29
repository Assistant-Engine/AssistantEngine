window.GLOBAL = window.GLOBAL || {};
const GLOBAL = window.GLOBAL;
GLOBAL.DotNetReferences = GLOBAL.DotNetReferences || {};
GLOBAL.OpenDropdownId = GLOBAL.OpenDropdownId ?? null;

// Set the DotNetReference for a component (using the dropdownId as key)
GLOBAL.SetDotnetReference = function (dropdownId, pDotNetReference) {
    GLOBAL.DotNetReferences[dropdownId] = pDotNetReference;
};


GLOBAL.SetOpenDropdownId = function (dropdownId) {
    if (GLOBAL.OpenDropdownId && GLOBAL.OpenDropdownId !== dropdownId) {
        const prevRef = GLOBAL.DotNetReferences[GLOBAL.OpenDropdownId];
        if (prevRef && typeof prevRef.invokeMethodAsync === "function") {
            prevRef.invokeMethodAsync('HandleOutsideClick', GLOBAL.OpenDropdownId);
        }
    }
    GLOBAL.OpenDropdownId = dropdownId;
};


// Listen for click events to detect outside clicks
(function () {
    window.addEventListener("click", function (e) {
        if (GLOBAL.OpenDropdownId && !document.getElementById(GLOBAL.OpenDropdownId).contains(e.target)) {
            // Invoke HandleOutsideClick for the active dropdown
            GLOBAL.DotNetReferences[GLOBAL.OpenDropdownId].invokeMethodAsync('HandleOutsideClick', GLOBAL.OpenDropdownId);
            GLOBAL.OpenDropdownId = null; // Reset after the click
        }
    });
})();
GLOBAL.RemoveDotnetReference = function (dropdownId) {
    delete GLOBAL.DotNetReferences[dropdownId];
};
// keep alongside your other GLOBAL helpers
GLOBAL.SetOllamaRef = function (dotnetRef) { GLOBAL.OllamaRef = dotnetRef; };
/*
// Delegate clicks for injected /library/<model> links
$(document).on('click', 'a[href^="/library/"]', function (e) {
    // allow new-tab / modified clicks to behave normally
    if (e.which === 2 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

    const href = $(this).attr('href');
    const m = href && href.match(/^\/library\/([^?#/]+)$/); // captures deepseek-r1:latest
    if (!m) return;

    e.preventDefault();
    const model = decodeURIComponent(m[1]);

    if (GLOBAL.OllamaRef && typeof GLOBAL.OllamaRef.invokeMethodAsync === 'function') {
        GLOBAL.OllamaRef.invokeMethodAsync('PullModelFromLink', model);
        // optional: navigate after starting pull
        // window.location.href = href;
    } else {
        console.warn('GLOBAL.OllamaRef not set');
    }
});*/
GLOBAL.SetOllamaRef = function (dotnetRef) {
    GLOBAL.OllamaRef = dotnetRef;
};

window.GLOBAL.HighlightAllPrism = function () {

    // no need for jQuery here unless you want DOM-ready
    Prism.highlightAll();
};

window.GLOBAL.SetTheme = theme => {
    // using jQuery, since you prefer it:
    $('html').attr('data-theme', theme);
};
window.GLOBAL.toastrInterop = {
    success: (msg, title) => toastr.success(msg, title),
    error: (msg, title) => toastr.error(msg, title),
    info: (msg, title) => toastr.info(msg, title),
    warning: (msg, title) => toastr.warning(msg, title)
};


(function () {
    if (window.__ollamaBound) return; window.__ollamaBound = true;
    document.addEventListener('click', function (e) {
        const a = e.target.closest('a'); if (!a) return;
        if (e.button === 1 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        const m = (a.getAttribute('href') || '').match(/\/library\/([^?#/]+)/);
        if (!m) return;
        e.preventDefault();
        const model = decodeURIComponent(m[1]);
        if (GLOBAL.OllamaRef?.invokeMethodAsync) {
            GLOBAL.OllamaRef.invokeMethodAsync('PullModelFromLink', model);
        } else {
            console.warn('OllamaRef not set');
        }
    }, true); 
})();



document.addEventListener('DOMContentLoaded', () => {
    $(document).on("click", ".ollama-download", function (e) {
        // if the button is *inside* the <a>, do nothing, normal link works
        if ($(this).closest("a").length > 0) return;

        // otherwise find the nearest <a> upward or sideways and follow it
        var $link = $(this).closest("div, li, tr").find("a").first();
        if ($link.length === 0) return;



    // build and dispatch a real click event on the anchor element
    var ev = new MouseEvent("click", {
        bubbles: true,
        cancelable: true,
        view: window
    });

        $link[0].dispatchEvent(ev);
    });
    $(document).on('click', '#toggleThemeBtn', function () {
        const cur = $('html').attr('data-theme') || 'light';
        const next = (cur === 'light') ? 'dark' : 'light';

        $('html').attr('data-theme', next);

        // toggle icon classes
        $(this).find('i').toggleClass('fi-br-moon fi-br-sun');

        // flip any text that has data-theme-text (e.g. "Light" <-> "Dark")
        $('[data-theme-text]').each(function () {

            // text node swap
            let txt = $(this).text();
            txt = txt.replace(/light/gi, '__TMP__')
                .replace(/Dark/gi, 'Light')
                .replace(/__TMP__/gi, 'Dark');
            $(this).text(txt);

            // optional: also flip title attr if present
            let tip = $(this).attr('title');
            if (tip) {
                tip = tip.replace(/light/gi, '__TMP__')
                    .replace(/Dark/gi, 'Light')
                    .replace(/__TMP__/gi, 'Dark');
                $(this).attr('title', tip);
            }
        });


        localStorage.setItem('ae-theme', next);

        if (window.DotNet && DotNet.invokeMethodAsync) {
            DotNet.invokeMethodAsync('AssistantEngine.UI', 'OnThemeChanged', next);
        }
    });



    // "think" toggle
    $(document).on('click', 'think .toggle', function (e) {
        e.stopPropagation();
        var $t = $(this).closest('think'),
            open = $t.hasClass('open');
        $t.toggleClass('open');
        $t.find('.more').toggle(!open);
        $(this).text(open ? 'Show more' : 'Show less');
    });

    // Any other jQuery .on('click') handlers that reference Blazor-rendered elements go here
    /*$(document).off('click.ollama').on('click.ollama', 'a[href^="/library/"]', function (e) {
        // allow new-tab / modified clicks to behave normally
        if (e.which === 2 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

        const href = $(this).attr('href');
        const m = href && href.match(/^\/library\/([^?#/]+)$/); // e.g. deepseek-r1:latest
        if (!m) return;

        e.preventDefault();

        const model = decodeURIComponent(m[1]);

        // per-link debounce: don’t fire twice rapidly
        const $a = $(this);
        if ($a.data('ollama-handled')) return;
        $a.data('ollama-handled', true);
        setTimeout(() => $a.removeData('ollama-handled'), 1500);

        if (GLOBAL.OllamaRef && typeof GLOBAL.OllamaRef.invokeMethodAsync === 'function') {
            console.log("Invoking PullModelFromLink for", model);
            GLOBAL.OllamaRef.invokeMethodAsync('PullModelFromLink', model);
        } else {
            console.warn('GLOBAL.OllamaRef not set');
        }
    });*/
});