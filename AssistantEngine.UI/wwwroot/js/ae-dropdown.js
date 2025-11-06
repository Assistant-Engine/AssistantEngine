window.aeDropdown = (function () {
    const handlers = new Map();

    function outsideHandler(id, dotnet) {
        return function (e) {
            const $root = $('#' + id);
            if (!$root.length) return;
            const isInside = $root.is(e.target) || $root.has(e.target).length > 0;
            if (!isInside) dotnet.invokeMethodAsync('Close');
        };
    }

    function keyHandler(dotnet) {
        return function (e) {
            if (e.key === 'Escape') dotnet.invokeMethodAsync('Close');
        };
    }

    return {
        register: function (id, dotnetRef) {
            const h1 = outsideHandler(id, dotnetRef);
            const h2 = keyHandler(dotnetRef);
            $(document).on('mousedown.ae-dd.' + id, h1);
            $(document).on('keydown.ae-dd.' + id, h2);
            handlers.set(id, { h1, h2 });
        },
        unregister: function (id) {
            const h = handlers.get(id);
            if (!h) return;
            $(document).off('mousedown.ae-dd.' + id, h.h1);
            $(document).off('keydown.ae-dd.' + id, h.h2);
            handlers.delete(id);
        }
    };
})();
