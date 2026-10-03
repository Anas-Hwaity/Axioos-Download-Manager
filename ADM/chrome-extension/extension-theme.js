"use strict";

(() => {
    const STORAGE_KEY = "adm.appearance.v1";
    const THEMES = Object.freeze({
        glacier: { dark: true, bg: "#0a1120", g1: "#1b3a5c", g2: "#33205a", ink: "#e8f1ff", mut: "#8ea6c4", a1: "#5ee7f7", a2: "#7b8cff", on: "#07101f" },
        aurora: { dark: true, bg: "#07131a", g1: "#0f4d45", g2: "#2a1a55", ink: "#e6fff8", mut: "#8fbfb2", a1: "#4ef0b5", a2: "#a78bfa", on: "#04140f" },
        ocean: { dark: true, bg: "#050d1f", g1: "#0b3a78", g2: "#062a4a", ink: "#e6f0ff", mut: "#87a2c7", a1: "#38bdf8", a2: "#2563eb", on: "#03101f" },
        orchid: { dark: true, bg: "#130a1a", g1: "#4a1850", g2: "#1e2a66", ink: "#fbeaff", mut: "#c3a3cf", a1: "#ff7ad9", a2: "#9f7aff", on: "#1a0620" },
        ember: { dark: true, bg: "#140c0a", g1: "#5a2412", g2: "#3d1030", ink: "#fff1ea", mut: "#c7a89a", a1: "#ffb347", a2: "#ff5f6d", on: "#1a0b06" },
        crimson: { dark: true, bg: "#12070a", g1: "#5c0f1e", g2: "#2a0b2e", ink: "#ffecef", mut: "#c79aa3", a1: "#ff4d6d", a2: "#ff8fa3", on: "#1f0509" },
        gold: { dark: true, bg: "#0b0f1c", g1: "#1c2b4f", g2: "#3a2d10", ink: "#f5f1e6", mut: "#a9a38f", a1: "#ffd166", a2: "#f4a261", on: "#1a1405" },
        lime: { dark: true, bg: "#070807", g1: "#1d2a0a", g2: "#0a1f1f", ink: "#f0ffe8", mut: "#97a88f", a1: "#c6ff3d", a2: "#3dffc6", on: "#0a0f02" },
        graphite: { dark: true, bg: "#111214", g1: "#2a2c30", g2: "#1c1d20", ink: "#f2f3f5", mut: "#9a9ea6", a1: "#ffffff", a2: "#b8bcc4", on: "#111214" },
        paper: { dark: false, bg: "#eef2f8", g1: "#cfe0ff", g2: "#e9dcff", ink: "#172033", mut: "#5d6a82", a1: "#2f6bff", a2: "#7c4dff", on: "#ffffff" },
        solar: { dark: false, bg: "#f7f1e8", g1: "#ffd9a8", g2: "#ffe9d6", ink: "#2b2118", mut: "#7c6a58", a1: "#f07b1d", a2: "#e0452b", on: "#ffffff" },
        sakura: { dark: false, bg: "#fbf0f3", g1: "#ffd1dc", g2: "#e3e1ff", ink: "#2d1a22", mut: "#85616e", a1: "#e8457a", a2: "#9b5de5", on: "#ffffff" }
    });

    function normalize(appearance) {
        const theme = String(appearance?.theme || "").toLowerCase();
        const accent = String(appearance?.accent || "").toLowerCase() === "solid" ? "solid" : "gradient";
        return { theme: Object.prototype.hasOwnProperty.call(THEMES, theme) ? theme : "glacier", accent };
    }

    function palette(appearance) {
        const chosen = normalize(appearance);
        const t = THEMES[chosen.theme];
        return { ...t, a2: chosen.accent === "solid" ? t.a1 : t.a2, theme: chosen.theme, accent: chosen.accent };
    }

    function variables(appearance) {
        const p = palette(appearance);
        return {
            "--adm-bg": p.bg, "--adm-g1": p.g1, "--adm-g2": p.g2, "--adm-ink": p.ink, "--adm-mut": p.mut,
            "--adm-a1": p.a1, "--adm-a2": p.a2, "--adm-on": p.on, "color-scheme": p.dark ? "dark" : "light"
        };
    }

    function apply(element, appearance) {
        if (!element?.style) return;
        for (const [key, value] of Object.entries(variables(appearance))) element.style.setProperty(key, value);
    }

    function load(callback) {
        if (!globalThis.chrome?.storage?.local?.get) { callback(null); return; }
        chrome.storage.local.get([STORAGE_KEY], result => {
            void chrome.runtime?.lastError;
            callback(result?.[STORAGE_KEY] || null);
        });
    }

    function save(appearance) {
        if (!appearance || !globalThis.chrome?.storage?.local?.set) return;
        chrome.storage.local.set({ [STORAGE_KEY]: normalize(appearance) }, () => { void chrome.runtime?.lastError; });
    }

    globalThis.AdmExtensionTheme = Object.freeze({ STORAGE_KEY, THEMES, normalize, palette, variables, apply, load, save });
})();
