(() => {
    const timers = new WeakMap();
    const edited = new WeakSet();
    const isSearch = (element) => element instanceof HTMLInputElement && element.hasAttribute("data-catalog-search");
    const cancel = (element) => {
        clearTimeout(timers.get(element));
        timers.delete(element);
    };
    const schedule = (element) => {
        cancel(element);
        timers.set(element, setTimeout(() => {
            timers.delete(element);
            if (element.isConnected) element.dispatchEvent(new Event("change", { bubbles: true }));
        }, 500));
    };
    const restore = (element) => {
        cancel(element);
        element.value = new URL(window.location.href).searchParams.get("search") ?? "";
    };

    document.addEventListener("input", (event) => {
        if (isSearch(event.target)) {
            edited.add(event.target);
            if (!event.isComposing) schedule(event.target);
        }
    });
    document.addEventListener("compositionend", (event) => {
        if (isSearch(event.target)) schedule(event.target);
    });
    document.addEventListener("change", (event) => {
        if (isSearch(event.target)) cancel(event.target);
    });
    window.addEventListener("popstate", () => {
        document.querySelectorAll("input[data-catalog-search]").forEach(restore);
    });
    window.streamlinkSearch = { initialize: (element) => {
        if (!edited.has(element)) restore(element);
    } };
})();
