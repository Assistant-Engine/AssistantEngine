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

(function () {
    window.addEventListener("click", function (e) {
        if (GLOBAL.OpenDropdownId && !document.getElementById(GLOBAL.OpenDropdownId).contains(e.target)) {
            GLOBAL.DotNetReferences[GLOBAL.OpenDropdownId].invokeMethodAsync('HandleOutsideClick', GLOBAL.OpenDropdownId);
            GLOBAL.OpenDropdownId = null; // Reset after the click
        }
    });
})();
GLOBAL.RemoveDotnetReference = function (dropdownId) {
    delete GLOBAL.DotNetReferences[dropdownId];
};

GLOBAL.SetOllamaRef = function (dotnetRef) { GLOBAL.OllamaRef = dotnetRef; };

GLOBAL.SetOllamaRef = function (dotnetRef) {
    GLOBAL.OllamaRef = dotnetRef;
};

(function () {
    let t = null;
    window.GLOBAL.HighlightAllPrism = function () {
        if (t) clearTimeout(t);
        t = setTimeout(function () {
            if (window.Prism && Prism.highlightAll) Prism.highlightAll();
        }, 200);
    };
})();


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
// === OAuth helpers (integrated with GLOBAL) ===
(function (global) {
    const $w = $(window);
    GLOBAL.OAuth = GLOBAL.OAuth || {};
    let installed = false;

    async function deliver(code, state) {
        // prefer static JSInvokable (no instance ref needed)
        async function callDotNet() {
            if (global.DotNet && typeof global.DotNet.invokeMethodAsync === 'function') {
                return global.DotNet.invokeMethodAsync('AssistantEngine.UI', 'AE_ReceiveOAuthCode', code, state);
            }
            throw new Error('DotNet not ready');
        }

        // retry until Blazor boots
        for (let i = 0; i < 50; i++) {
            try { await callDotNet(); break; }
            catch { await new Promise(r => setTimeout(r, 100)); }
        }
    }

    function onMessage(e) {
        const evt = e.originalEvent || e;
        const d = evt && evt.data;
        if (!d || d.type !== 'AE_OAUTH_CODE') return;
        deliver(d.code, d.state);
    }

    function install() {
        if (installed) return;
        installed = true;
        $w.on('message.ae.oauth', onMessage);
    }

    // optional: allow Razor /oauth/callback page to call directly
    GLOBAL.OAuth.deliver = deliver;
    GLOBAL.OAuth.install = install;

    $(install);
})(window);



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
    // keep your existing click handler

    window.ai = window.ai || {};

    // scan for overflow (unchanged)
    window.ai.scanThinks = function () {
        $('think').each(function () {
            var $t = $(this), el = $t.find('.body')[0];
            if (!el) return;
            var overflow = el.scrollHeight > el.clientHeight + 1;
            $t.toggleClass('has-overflow', overflow);
         //   if (!overflow) $t.removeClass('open'); // avoid stuck "Show less"
        });
    };

    // observe streaming updates and rescan (throttled)
    window.ai._attached = window.ai._attached || {};
    window.ai.observeThinks = function (selector) {
        var $root = $(selector);
        if (!$root.length) return;

        var key = $root[0];
        if (window.ai._attached[key]) return; // don't double-attach

        var t;
        var scan = function () {
            clearTimeout(t);
            t = setTimeout(window.ai.scanThinks, 50); // throttle bursty updates
        };

        var obs = new MutationObserver(scan);
        obs.observe($root[0], { childList: true, subtree: true, characterData: true });
        window.ai._attached[key] = obs;

        // initial pass
        window.ai.scanThinks();
    };

});