window.streamlinkStorage = {
  get: (key) => window.localStorage.getItem(key),
  set: (key, value) => window.localStorage.setItem(key, value),
  remove: (key) => window.localStorage.removeItem(key),
};

window.streamlinkPlayer = {
  load: async (elementId, source) => {
    const element = document.getElementById(elementId);
    if (!element || !source) return;

    element.controls = true;
    element.playsInline = true;

    element.src = source;
    element.load();
  },
};
