const storageKey = "swimburger.datetime-lab.state.v1";
const rememberKey = "swimburger.datetime-lab.remember.v1";
const sharePrefix = "#state=";
const maxSharedStateLength = 32_000;

function encodeState(value) {
  const bytes = new TextEncoder().encode(value);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}

function decodeState(value) {
  if (!value || value.length > maxSharedStateLength) return null;
  const padded = value.replaceAll("-", "+").replaceAll("_", "/").padEnd(Math.ceil(value.length / 4) * 4, "=");
  const binary = atob(padded);
  const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
}

function readInitialState() {
  try {
    const remembered = localStorage.getItem(rememberKey) === "true";
    if (location.hash.startsWith(sharePrefix)) {
      return { state: decodeState(location.hash.slice(sharePrefix.length)), remembered, source: "link" };
    }
    if (remembered) {
      return { state: localStorage.getItem(storageKey), remembered: true, source: "device" };
    }
  } catch {
    // Invalid shared data or unavailable storage falls back to the default example.
  }
  return { state: null, remembered: false, source: "default" };
}

async function copyText(value) {
  const previousFocus = document.activeElement;
  try {
    await navigator.clipboard.writeText(value);
    return true;
  } catch {
    const area = document.createElement("textarea");
    area.value = value;
    area.setAttribute("readonly", "");
    area.style.cssText = "position:fixed;inset-block-start:0;opacity:0";
    document.body.append(area);
    area.select();
    try {
      return document.execCommand("copy");
    } finally {
      area.remove();
      previousFocus?.focus({ preventScroll: true });
    }
  }
}

globalThis.dateTimeLab = {
  getInitialState: readInitialState,

  async copyShareLink(state) {
    try {
      const encoded = encodeState(state);
      if (encoded.length > maxSharedStateLength) return "too-large";
      const url = new URL(location.href);
      url.hash = `state=${encoded}`;
      return await copyText(url.toString()) ? "copied" : "failed";
    } catch {
      return "failed";
    }
  },

  clearShareState() {
    if (!location.hash) return;
    history.replaceState(null, "", `${location.pathname}${location.search}`);
  },

  saveRememberedState(state) {
    try {
      if (localStorage.getItem(rememberKey) === "true") localStorage.setItem(storageKey, state);
      return true;
    } catch {
      return false;
    }
  },

  setRememberedState(remembered, state) {
    try {
      localStorage.setItem(rememberKey, remembered ? "true" : "false");
      if (remembered) localStorage.setItem(storageKey, state);
      else localStorage.removeItem(storageKey);
      return true;
    } catch {
      return false;
    }
  },

  forgetRememberedState() {
    try {
      localStorage.removeItem(storageKey);
      localStorage.removeItem(rememberKey);
      return true;
    } catch {
      return false;
    }
  },

  async copyText(value) {
    return copyText(value);
  }
};
