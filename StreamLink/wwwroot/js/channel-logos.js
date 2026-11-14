(() => {
    Blazor.registerCustomEventType("logonear", {
        browserEventName: "streamlink-logo-near",
        createEventArgs: () => ({})
    });

    const pending = new WeakSet();
    const nearby = new WeakSet();
    const failed = new WeakSet();
    const leftViewport = new WeakSet();
    const retries = new WeakMap();
    const timers = new WeakMap();
    const isLogo = (element) => element instanceof HTMLImageElement && element.hasAttribute("data-channel-logo");
    const isDeferred = (element) => isLogo(element) && element.hasAttribute("data-deferred-logo");
    const request = (image) => {
        if (pending.has(image) || !image.isConnected) return;
        pending.add(image);
        image.dispatchEvent(new CustomEvent("streamlink-logo-near", { bubbles: true }));
    };

    const observer = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            const image = entry.target;
            if (entry.isIntersecting) {
                nearby.add(image);
                if (!image.hasAttribute("src") || (failed.has(image) && leftViewport.has(image))) request(image);
                leftViewport.delete(image);
            } else {
                nearby.delete(image);
                leftViewport.add(image);
            }
        }
    }, { rootMargin: "300px 0px" });

    const observe = (node) => {
        if (!(node instanceof Element)) return;
        if (isDeferred(node)) observer.observe(node);
        node.querySelectorAll("img[data-deferred-logo]").forEach((image) => observer.observe(image));
    };
    const unobserve = (node) => {
        if (!(node instanceof Element)) return;
        const remove = (image) => {
            observer.unobserve(image);
            nearby.delete(image);
            clearTimeout(timers.get(image));
        };
        if (isDeferred(node)) remove(node);
        node.querySelectorAll("img[data-deferred-logo]").forEach(remove);
    };
    new MutationObserver((records) => {
        for (const record of records) {
            record.removedNodes.forEach(unobserve);
            record.addedNodes.forEach(observe);
        }
    }).observe(document, { childList: true, subtree: true });

    document.addEventListener("load", (event) => {
        if (!isLogo(event.target)) return;
        failed.delete(event.target);
        clearTimeout(timers.get(event.target));
    }, true);
    document.addEventListener("error", (event) => {
        const image = event.target;
        if (!isLogo(image) || !image.hasAttribute("src")) return;
        failed.add(image);
        const attempt = retries.get(image) ?? 0;
        if (attempt >= 2) return;
        retries.set(image, attempt + 1);
        clearTimeout(timers.get(image));
        timers.set(image, setTimeout(() => {
            if (image.isConnected && (!isDeferred(image) || nearby.has(image))) image.src = image.src;
        }, (attempt + 1) * 1000));
    }, true);
})();
