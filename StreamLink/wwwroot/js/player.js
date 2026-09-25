window.streamlinkStorage = {
  get: (key) => window.localStorage.getItem(key),
  set: (key, value) => window.localStorage.setItem(key, value),
  remove: (key) => window.localStorage.removeItem(key),
};

const players = new Map();

window.streamlinkPlayer = {
  load: (elementId, hlsSource, tsSource, compatibleSource) => {
    const video = document.getElementById(elementId);
    if (!video) return;
    const sources = [
      compatibleSource && { kind: "compatible", url: compatibleSource },
      hlsSource && { kind: "hls", url: hlsSource },
      tsSource && { kind: "ts", url: tsSource },
    ]
      .filter(Boolean)
      .map((source) => ({
        ...source,
        // mpegts.js fetches inside a blob: worker, where /stream/... has no
        // document origin to resolve against. Always give media engines full URLs.
        url: new URL(source.url, document.baseURI).href,
      }));
    if (!sources.length) return;

    const key = sources.map((s) => s.url).join("|");
    const previous = players.get(elementId);
    if (previous?.video === video && previous.key === key) return;
    previous?.dispose();

    const status = video
      .closest(".player-card")
      .querySelector(".player-status");
    const statusText = status.querySelector(".player-status-text");
    const error = video.closest(".player-card").querySelector(".player-error");
    let engine;
    let index = 0;
    let attempt = 0;
    let startupTimer;
    let pictureTimer;
    let disposed = false;
    let stopping = false;

    const message = (text) => {
      statusText.textContent = text;
      status.hidden = !text;
      error.hidden = true;
      error.textContent = "";
    };
    const showError = (text) => {
      status.hidden = true;
      statusText.textContent = "";
      error.textContent = text;
      error.hidden = false;
    };
    const stop = () => {
      stopping = true;
      clearTimeout(startupTimer);
      clearTimeout(pictureTimer);
      if (engine) {
        if (engine.kind === "hls") engine.instance.destroy();
        else {
          engine.instance.pause();
          engine.instance.unload();
          engine.instance.detachMediaElement();
          engine.instance.destroy();
        }
        engine = undefined;
      }
      video.pause();
      video.removeAttribute("src");
      video.load();
      stopping = false;
    };
    const fail = (reason) => {
      if (disposed) return;
      if (index + 1 < sources.length) {
        stop();
        index++;
        start();
      } else {
        clearTimeout(startupTimer);
        showError(
          `Unable to play this channel: ${reason} If it plays in VLC, check the server log for FFmpeg/provider errors.`,
        );
      }
    };
    const onError = () => {
      if (!stopping && !disposed) fail("the browser could not decode it.");
    };
    const onPlaying = () => {
      message("");
      clearTimeout(pictureTimer);
      const current = attempt;
      pictureTimer = setTimeout(() => {
        if (
          !disposed &&
          current === attempt &&
          video.videoWidth === 0 &&
          !video.paused
        )
          fail("audio plays but no video track can be decoded.");
      }, 6000);
    };
    const onWaiting = () => {
      if (!disposed) message("Catching up…");
    };
    const start = () => {
      stop();
      const current = ++attempt;
      const source = sources[index];
      message("Connecting…");
      startupTimer = setTimeout(() => {
        if (
          !disposed &&
          current === attempt &&
          video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA
        )
          fail("no video arrived in 25 seconds.");
      }, 25000);

      try {
        if (source.kind === "hls") {
          if (video.canPlayType("application/vnd.apple.mpegurl")) {
            video.src = source.url;
            video.load();
          } else if (window.Hls?.isSupported()) {
            const hls = new window.Hls({
              enableWorker: true,
              lowLatencyMode: false,
              liveSyncDurationCount: 4,
              liveMaxLatencyDurationCount: 10,
              maxBufferLength: 30,
            });
            engine = { kind: "hls", instance: hls };
            hls.on(window.Hls.Events.ERROR, (_event, data) => {
              if (data.fatal && current === attempt)
                fail(`HLS ${data.type || "error"}.`);
            });
            hls.attachMedia(video);
            hls.loadSource(source.url);
          } else {
            fail("HLS is not supported by this browser.");
            return;
          }
        } else {
          if (!window.mpegts?.isSupported()) {
            fail("MPEG-TS playback is not supported by this browser.");
            return;
          }
          const ts = window.mpegts.createPlayer(
            { type: "mpegts", isLive: true, url: source.url },
            {
              enableWorker: true,
              lazyLoad: false,
              liveBufferLatencyChasing: false,
              liveBufferLatencyMaxLatency: 20,
              liveBufferLatencyMinRemain: 2,
            },
          );
          engine = { kind: "ts", instance: ts };
          ts.on(window.mpegts.Events.ERROR, (_type, detail) => {
            if (current === attempt)
              fail(`stream request failed (${detail || "unknown error"}).`);
          });
          ts.attachMediaElement(video);
          ts.load();
        }
        video.play().catch((error) => {
          if (current === attempt && error.name === "NotAllowedError")
            message(
              "Press play to start the live stream (the browser blocked autoplay with sound).",
            );
        });
      } catch (error) {
        if (current === attempt) fail("the playback engine could not start.");
      }
    };

    const observer = new MutationObserver(() => {
      if (!video.isConnected) {
        players.delete(elementId);
        dispose();
      }
    });
    const onEnded = () => {
      if (!stopping && !disposed) fail("the live connection ended.");
    };
    const dispose = () => {
      if (disposed) return;
      disposed = true;
      observer.disconnect();
      video.removeEventListener("error", onError);
      video.removeEventListener("playing", onPlaying);
      video.removeEventListener("waiting", onWaiting);
      video.removeEventListener("ended", onEnded);
      stop();
    };
    video.addEventListener("error", onError);
    video.addEventListener("playing", onPlaying);
    video.addEventListener("waiting", onWaiting);
    video.addEventListener("ended", onEnded);
    players.set(elementId, { video, key, dispose });
    observer.observe(document.body, { childList: true, subtree: true });
    start();
  },
};
