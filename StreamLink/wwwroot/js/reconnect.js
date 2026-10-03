// .NET 8 doesn't reload automatically when a server circuit is rejected.
// Keep reconnecting while the page is usable, and refresh only when the old
// circuit can no longer be restored (session hydration runs again on reload).
(() => {
  const overlay = document.getElementById("streamlink-reconnect");
  const message = document.getElementById("streamlink-reconnect-message");
  document
    .getElementById("streamlink-reconnect-reload")
    .addEventListener("click", () => location.reload());

  let process = null;

  function waitUntilReady(delay, current) {
    return new Promise((resolve) => {
      let timer;
      const finish = () => {
        clearTimeout(timer);
        document.removeEventListener("visibilitychange", check);
        window.removeEventListener("online", check);
        current.cancelWait = null;
        resolve();
      };
      const check = () => {
        if (!current.active || (!document.hidden && navigator.onLine)) finish();
      };
      current.cancelWait = finish;
      timer = setTimeout(() => {
        if (!current.active || (!document.hidden && navigator.onLine)) check();
      }, delay);
      document.addEventListener("visibilitychange", check);
      window.addEventListener("online", check);
    });
  }

  Blazor.start({
    circuit: {
      reconnectionHandler: {
        onConnectionDown: () => {
          if (process) return;
          const current = { active: true };
          process = current;
          // Avoid flashing the overlay during brief network changes.
          const show = setTimeout(() => {
            if (current.active) overlay.classList.add("is-visible");
          }, 700);
          (async () => {
            let attempts = 0;
            while (current.active) {
              await waitUntilReady(
                attempts ? Math.min(attempts * 2000, 10000) : 0,
                current,
              );
              if (!current.active) break;
              message.textContent = "Restoring your connection…";
              try {
                const connected = await Blazor.reconnect();
                if (!current.active) break;
                if (!connected) {
                  location.reload(); // The server discarded the old circuit.
                  break;
                }
                // onConnectionUp hides the overlay after a successful reconnect.
                break;
              } catch {
                attempts++;
                message.textContent = navigator.onLine
                  ? "Still trying to reconnect…"
                  : "Waiting for your connection…";
              }
            }
            clearTimeout(show);
          })();
        },
        onConnectionUp: () => {
          if (process) {
            process.active = false;
            process.cancelWait?.();
          }
          process = null;
          overlay.classList.remove("is-visible");
          message.textContent = "Restoring your connection…";
        },
      },
    },
  });
})();
