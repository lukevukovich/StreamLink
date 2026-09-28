window.streamlinkStorage = {
  get: (key) => window.localStorage.getItem(key),
  set: (key, value) => window.localStorage.setItem(key, value),
  remove: (key) => window.localStorage.removeItem(key),
};

// Clipboard access requires HTTPS/localhost and can lose its user gesture after
// an async redirect. A visible, selectable input is always kept as a fallback.
async function copyToInput(value, input) {
  input.value = value;
  input.hidden = false;
  input.focus();
  input.select();
  try {
    input.setSelectionRange(0, value.length);
  } catch {
    /* Selection remains available. */
  }

  if (navigator.clipboard?.writeText && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(value);
      return "Copied! You can also select the link below.";
    } catch {
      /* Keep the field visible for manual copying. */
    }
  }
  try {
    if (document.execCommand("copy"))
      return "Copied! You can also select the link below.";
  } catch {
    /* Manual selection remains available. */
  }
  return "Tap and hold the link below to copy it.";
}

function vlcDevice() {
  const platform =
    navigator.userAgentData?.platform || navigator.platform || "";
  if (
    /^(Win|Mac|Linux)/i.test(platform) &&
    !(
      platform === "MacIntel" &&
      navigator.maxTouchPoints > 1 &&
      navigator.userAgentData?.mobile !== false
    )
  )
    return "desktop";
  if (
    /^(iPhone|iPad|iPod)$/i.test(platform) ||
    (platform === "MacIntel" &&
      navigator.maxTouchPoints > 1 &&
      navigator.userAgentData?.mobile !== false)
  )
    return "ios";
  if (
    /Android/i.test(platform) ||
    (navigator.userAgentData?.mobile === true &&
      /Android/i.test(navigator.userAgent))
  )
    return "android";
  // Unknown/spoofed platforms get the predictable playlist fallback.
  return "desktop";
}

document.documentElement.dataset.vlcDevice = vlcDevice();

window.streamlinkExternal = {
  resolve: async (grant) => {
    const response = await fetch("/api/external-link", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ grant }),
      cache: "no-store",
      credentials: "same-origin",
    });
    if (!response.ok) throw new Error("No safe redirect was returned.");
    const { url } = await response.json();
    if (!url || !/^https?:\/\//i.test(url))
      throw new Error("Invalid redirect.");
    return url;
  },
  copy: (url, card) =>
    copyToInput(url, card.querySelector(".external-stream-select")),
  openVlc: (url, channelName) => {
    const source = new URL(url);
    if (
      !["https:", "http:"].includes(source.protocol) ||
      /[\r\n\x00-\x1f\x7f]/.test(url)
    )
      return "Unsupported stream URL.";

    const device = vlcDevice();
    if (device === "ios") {
      window.location.href = `vlc-x-callback://x-callback-url/stream?url=${encodeURIComponent(source.href)}`;
      return "If VLC does not open, use Copy link and VLC's Open Network Stream.";
    }
    if (device === "android") {
      const mediaType = source.pathname.toLowerCase().endsWith(".ts")
        ? "video/mp2t"
        : "application/vnd.apple.mpegurl";
      const streamPath =
        `${source.host}${source.pathname}${source.search}`.replaceAll(
          "#",
          "%23",
        );
      const intentUrl = `intent://${streamPath}#Intent;scheme=${source.protocol.slice(0, -1)};package=org.videolan.vlc;type=${mediaType};end`;
      window.location.href = intentUrl;
      return "If VLC does not open, use Copy link and VLC's Open Network Stream.";
    }

    // Desktop VLC has no dependable browser URL handler. A local playlist can
    // be opened with VLC without StreamLink fetching media.
    const file = new Blob([`#EXTM3U\n${source.href}\n`], {
      type: "audio/x-mpegurl",
    });
    const fileUrl = URL.createObjectURL(file);
    const download = document.createElement("a");
    download.href = fileUrl;
    const cleanName =
      (channelName || "")
        .normalize("NFKD")
        .replace(/[\u0300-\u036f]/g, "")
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "")
        .slice(0, 64)
        .replace(/-+$/g, "") || "channel";
    download.download = `StreamLink-${cleanName}.m3u`;
    download.hidden = true;
    document.body.appendChild(download);
    download.click();
    download.remove();
    setTimeout(() => URL.revokeObjectURL(fileUrl), 60000);
    return "Playlist downloaded. Open it with VLC (choose VLC if prompted).";
  },
};

document.addEventListener("click", async (event) => {
  const button =
    event.target instanceof Element
      ? event.target.closest("[data-stream-action]")
      : null;
  if (!button) return;
  const card = button.closest(".external-stream-card");
  const grant = button.dataset.streamGrant;
  if (!card || !grant) return;

  const message = card.querySelector(".external-stream-message");
  const select = card.querySelector(".external-stream-select");
  select.hidden = true;
  select.value = "";
  message.textContent = "Capturing stream link…";
  message.hidden = false;
  button.disabled = true;
  // Opening a blank tab during the click retains user activation on mobile.
  const tab =
    button.dataset.streamAction === "open"
      ? window.open("about:blank", "_blank")
      : null;
  if (tab) tab.opener = null;
  try {
    if (button.dataset.streamAction === "open" && !tab) {
      message.textContent = "Allow pop-ups to open the stream in a new tab.";
      return;
    }
    const url = await window.streamlinkExternal.resolve(grant);
    if (button.dataset.streamAction === "open") {
      tab.location.replace(url);
      message.textContent = "Stream opened in a new tab.";
    } else if (button.dataset.streamAction === "copy") {
      message.textContent = await window.streamlinkExternal.copy(url, card);
    } else {
      message.textContent = window.streamlinkExternal.openVlc(
        url,
        button.dataset.streamName,
      );
    }
  } catch {
    if (tab) tab.close();
    message.textContent =
      "Could not capture a safe redirect link. Nothing was opened or copied.";
  } finally {
    button.disabled = false;
  }
});

document.addEventListener("click", async (event) => {
  const button =
    event.target instanceof Element
      ? event.target.closest(
          "[data-copy-url], [data-copy-input], [data-copy-grant], [data-open-grant]",
        )
      : null;
  if (!button) return;
  const container = button.closest(".share-item, .share-created, .link-row");
  if (!container) return;
  const input = container.querySelector(
    button.hasAttribute("data-copy-input") ? "input[readonly]" : ".copy-select",
  );
  const status = container.querySelector(".copy-status");
  if (!input || !status || button.disabled) return;
  const isOpen = button.hasAttribute("data-open-grant");
  // Open a blank tab during the gesture, before awaiting the provider request.
  const tab = isOpen ? window.open("about:blank", "_blank") : null;
  if (tab) tab.opener = null;
  if (!button.hasAttribute("data-copy-input")) input.hidden = true;
  status.hidden = false;
  button.disabled = true;
  try {
    if (isOpen && !tab) {
      status.textContent = "Allow pop-ups to open the link.";
      return;
    }
    const grant = button.dataset.copyGrant || button.dataset.openGrant;
    const url = grant
      ? await window.streamlinkExternal.resolve(grant)
      : (button.dataset.copyUrl ?? input.value);
    if (!url) throw new Error("Missing link");
    if (tab) {
      tab.location.replace(url);
      status.textContent = "Opened in a new tab.";
    } else {
      status.textContent = await copyToInput(url, input);
    }
  } catch {
    if (tab) tab.close();
    status.textContent =
      "Could not capture a safe link. Nothing was opened or copied.";
  } finally {
    button.disabled = false;
  }
});
