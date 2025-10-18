// debug-probe.js (vanilla)
(function () {
    function show(msg) {
        var el = document.getElementById("panic");
        if (!el) {
            el = document.createElement("pre");
            el.id = "panic";
            el.style.cssText = "position:fixed;left:8px;bottom:108px;max-width:80vw;max-height:40vh;overflow:auto;background:#111;color:#0f0;font:12px/1.4 monospace;padding:8px;border:1px solid #333;z-index:999999";
            document.body.appendChild(el);
        }
        el.textContent += msg + "\n";
    }

    window.addEventListener("error", e => show("[onerror] " + e.message));
    window.addEventListener("unhandledrejection", e => {
        var r = e.reason;
        show("[unhandledrejection] " + (r && (r.stack || r.message) || r));
    });

    document.addEventListener("DOMContentLoaded", function () {
        ["_framework/blazor.boot.json", "_framework/blazor.webview.js", "_content/AssistantEngine.UI/app.css"]
            .forEach(u => fetch(u).then(r => show("[probe] " + u + " -> " + r.status))
                .catch(err => show("[probe] " + u + " -> FAILED: " + err)));
    });
})();
