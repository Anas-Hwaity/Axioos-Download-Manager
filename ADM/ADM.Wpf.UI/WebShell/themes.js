"use strict";

(() => {
    const THEMES = {
        glacier: { name: "Glacier", dark: 1, bg: "#0a1120", g1: "#1b3a5c", g2: "#33205a", ink: "#e8f1ff", mut: "#8ea6c4", a1: "#5ee7f7", a2: "#7b8cff", on: "#07101f" },
        aurora: { name: "Aurora", dark: 1, bg: "#07131a", g1: "#0f4d45", g2: "#2a1a55", ink: "#e6fff8", mut: "#8fbfb2", a1: "#4ef0b5", a2: "#a78bfa", on: "#04140f" },
        ocean: { name: "Ocean", dark: 1, bg: "#050d1f", g1: "#0b3a78", g2: "#062a4a", ink: "#e6f0ff", mut: "#87a2c7", a1: "#38bdf8", a2: "#2563eb", on: "#03101f" },
        orchid: { name: "Orchid", dark: 1, bg: "#130a1a", g1: "#4a1850", g2: "#1e2a66", ink: "#fbeaff", mut: "#c3a3cf", a1: "#ff7ad9", a2: "#9f7aff", on: "#1a0620" },
        ember: { name: "Ember", dark: 1, bg: "#140c0a", g1: "#5a2412", g2: "#3d1030", ink: "#fff1ea", mut: "#c7a89a", a1: "#ffb347", a2: "#ff5f6d", on: "#1a0b06" },
        crimson: { name: "Crimson", dark: 1, bg: "#12070a", g1: "#5c0f1e", g2: "#2a0b2e", ink: "#ffecef", mut: "#c79aa3", a1: "#ff4d6d", a2: "#ff8fa3", on: "#1f0509" },
        gold: { name: "Midnight Gold", dark: 1, bg: "#0b0f1c", g1: "#1c2b4f", g2: "#3a2d10", ink: "#f5f1e6", mut: "#a9a38f", a1: "#ffd166", a2: "#f4a261", on: "#1a1405" },
        lime: { name: "Cyber Lime", dark: 1, bg: "#070807", g1: "#1d2a0a", g2: "#0a1f1f", ink: "#f0ffe8", mut: "#97a88f", a1: "#c6ff3d", a2: "#3dffc6", on: "#0a0f02" },
        graphite: { name: "Graphite", dark: 1, bg: "#111214", g1: "#2a2c30", g2: "#1c1d20", ink: "#f2f3f5", mut: "#9a9ea6", a1: "#ffffff", a2: "#b8bcc4", on: "#111214" },
        paper: { name: "Paper", dark: 0, bg: "#eef2f8", g1: "#cfe0ff", g2: "#e9dcff", ink: "#172033", mut: "#5d6a82", a1: "#2f6bff", a2: "#7c4dff", on: "#ffffff" },
        solar: { name: "Solar", dark: 0, bg: "#f7f1e8", g1: "#ffd9a8", g2: "#ffe9d6", ink: "#2b2118", mut: "#7c6a58", a1: "#f07b1d", a2: "#e0452b", on: "#ffffff" },
        sakura: { name: "Sakura", dark: 0, bg: "#fbf0f3", g1: "#ffd1dc", g2: "#e3e1ff", ink: "#2d1a22", mut: "#85616e", a1: "#e8457a", a2: "#9b5de5", on: "#ffffff" }
    };

    const OPTS = {
        bg: { label: "Background", items: [["aurora", "Aurora"], ["mesh", "Mesh"], ["spot", "Spotlight"], ["grid", "Grid"], ["grain", "Grain"], ["flat", "Flat"]] },
        accent: { label: "Accent", items: [["grad", "Gradient"], ["solid", "Solid"]] },
        nav: { label: "Navigation", items: [["rail", "Icon rail"], ["side", "Labeled sidebar"], ["tabs", "Top tabs"]] },
        progress: { label: "Progress bars", items: [["thin", "Thin gradient"], ["glow", "Glow"], ["stripes", "Animated stripes"], ["thick", "Thick with %"], ["dots", "Segmented"]] },
        insp: { label: "Inspector visual", items: [["blocks", "Segment map"], ["lanes", "Connection lanes"], ["ring", "Progress ring"], ["graph", "Speed graph"]] },
        badge: { label: "File badges", items: [["text", "Text"], ["tint", "Coloured"], ["glyph", "Icons"]] },
        font: { label: "Typeface", items: [["Sora", "Sora"], ["Hanken Grotesk", "Hanken"], ["Space Grotesk", "Space Grotesk"], ["Outfit", "Outfit"], ["Manrope", "Manrope"]] },
        density: { label: "Density", items: [["comfy", "Comfortable"], ["compact", "Compact"]] }
    };

    const EXTRAS = [
        ["spark", "Speed sparkline in each row"],
        ["actions", "Quick actions on hover"],
        ["status", "Status bar"],
        ["toast", "Notifications"],
        ["limit", "Speed limit in title bar"],
        ["drop", "Drop zone under the list"],
        ["mono", "Monospace numbers"]
    ];

    const PRESETS = {
        Signature: { theme: "glacier", bg: "aurora", accent: "grad", nav: "rail", progress: "thin", insp: "blocks", badge: "text", font: "Sora", density: "comfy", glass: 60, x: { spark: false, actions: true, status: true, toast: true, limit: false, drop: false, mono: false } },
        "Night shift": { theme: "graphite", bg: "grain", accent: "solid", nav: "side", progress: "thick", insp: "graph", badge: "glyph", font: "Hanken Grotesk", density: "compact", glass: 35, x: { spark: true, actions: true, status: true, toast: true, limit: true, drop: false, mono: true } },
        Neon: { theme: "lime", bg: "grid", accent: "grad", nav: "rail", progress: "stripes", insp: "lanes", badge: "tint", font: "Space Grotesk", density: "comfy", glass: 45, x: { spark: true, actions: true, status: true, toast: true, limit: false, drop: false, mono: true } },
        Daylight: { theme: "paper", bg: "spot", accent: "grad", nav: "tabs", progress: "glow", insp: "ring", badge: "tint", font: "Outfit", density: "comfy", glass: 70, x: { spark: false, actions: true, status: false, toast: true, limit: false, drop: true, mono: false } }
    };

    const ACCENT_TO_DESKTOP = { grad: "gradient", solid: "solid" };
    const BACKDROP_TO_DESKTOP = { aurora: "aurora", mesh: "mesh", spot: "spotlight", grid: "grid", grain: "grain", flat: "flat" };

    function clone(value) {
        return JSON.parse(JSON.stringify(value));
    }

    function normalize(state) {
        const base = clone(PRESETS.Signature);
        const input = state && typeof state === "object" ? state : {};
        const out = { ...base, ...input, x: { ...base.x, ...(input.x || {}) } };
        if (!THEMES[out.theme]) out.theme = base.theme;
        for (const key of Object.keys(OPTS)) {
            if (!OPTS[key].items.some(item => item[0] === out[key])) out[key] = base[key];
        }
        const glass = Number(out.glass);
        out.glass = Number.isFinite(glass) ? Math.max(0, Math.min(100, Math.round(glass))) : base.glass;
        for (const [key] of EXTRAS) out.x[key] = out.x[key] === true;
        return out;
    }

    function presetName(state) {
        const current = JSON.stringify(normalize(state));
        return Object.keys(PRESETS).find(name => JSON.stringify(normalize(PRESETS[name])) === current) || "";
    }

    function mix(a, b, t) {
        const pa = parseInt(a.slice(1), 16);
        const pb = parseInt(b.slice(1), 16);
        const channel = shift => Math.round(((pa >> shift) & 255) + ((((pb >> shift) & 255) - ((pa >> shift) & 255)) * t));
        return "#" + [16, 8, 0].map(shift => channel(shift).toString(16).padStart(2, "0")).join("");
    }

    function rgba(hex, alpha) {
        const value = parseInt(hex.slice(1), 16);
        return `rgba(${(value >> 16) & 255},${(value >> 8) & 255},${value & 255},${alpha})`;
    }

    function cssVars(state) {
        const s = normalize(state);
        const t = THEMES[s.theme];
        const g = s.glass / 100;
        const v = {};
        const a2 = s.accent === "grad" ? t.a2 : t.a1;
        v["--bg"] = t.bg;
        v["--ink"] = t.ink;
        v["--mut"] = t.mut;
        v["--a1"] = t.a1;
        v["--a2"] = a2;
        v["--on"] = t.on;
        v["--acg"] = s.accent === "grad" ? `linear-gradient(90deg, ${t.a1}, ${t.a2})` : t.a1;
        v["--warn"] = t.dark ? "#ffd27a" : "#b7791f";
        v["--bad"] = t.dark ? "#ff8a9a" : "#d33c4a";
        v["--ok"] = t.dark ? "#6ee7a8" : "#1f9d62";
        if (t.dark) {
            const a = 0.015 + g * 0.075;
            v["--surf"] = `rgba(255,255,255,${a.toFixed(3)})`;
            v["--surf2"] = `rgba(255,255,255,${(a * 0.55).toFixed(3)})`;
            v["--solid"] = mix(t.bg, t.g1, 0.35);
            v["--line1"] = "rgba(255,255,255,.14)";
            v["--line2"] = "rgba(255,255,255,.07)";
            v["--track"] = "rgba(255,255,255,.09)";
        } else {
            const a = 0.35 + g * 0.5;
            v["--surf"] = `rgba(255,255,255,${a.toFixed(3)})`;
            v["--surf2"] = `rgba(255,255,255,${(a * 0.6).toFixed(3)})`;
            v["--solid"] = "#ffffff";
            v["--line1"] = "rgba(20,30,50,.14)";
            v["--line2"] = "rgba(20,30,50,.07)";
            v["--track"] = "rgba(20,30,50,.09)";
        }
        v["--blur"] = `${Math.round(g * 18)}px`;
        v["--selbg"] = `color-mix(in srgb, ${t.a1} ${t.dark ? 12 : 10}%, transparent)`;
        v["--selline"] = `color-mix(in srgb, ${t.a1} 55%, transparent)`;
        v["--badge"] = `color-mix(in srgb, ${t.a2} 26%, transparent)`;
        v["--toastbg"] = `color-mix(in srgb, ${t.bg} 86%, transparent)`;
        v["--menubg"] = t.dark ? mix(t.bg, t.g1, 0.28) : "#ffffff";
        v["--font"] = `"${s.font}", "Segoe UI", system-ui, sans-serif`;
        v["--r"] = "16px";
        v["--rp"] = "999px";
        v["--pad"] = s.density === "compact" ? "6px 10px" : "10px 12px";
        v["--gap"] = s.density === "compact" ? "5px" : "8px";
        const gl = t.dark ? "rgba(255,255,255,.045)" : "rgba(20,30,50,.06)";
        v["--bgimg"] = {
            aurora: `radial-gradient(120% 90% at 0% 0%, ${t.g1} 0%, transparent 55%), radial-gradient(90% 90% at 100% 100%, ${t.g2} 0%, transparent 55%), ${t.bg}`,
            mesh: `radial-gradient(60% 60% at 15% 20%, ${t.g1} 0%, transparent 70%), radial-gradient(50% 60% at 85% 15%, color-mix(in srgb, ${t.a2} 22%, transparent) 0%, transparent 70%), radial-gradient(60% 60% at 70% 95%, ${t.g2} 0%, transparent 70%), radial-gradient(40% 40% at 35% 80%, color-mix(in srgb, ${t.a1} 12%, transparent) 0%, transparent 70%), ${t.bg}`,
            spot: `radial-gradient(70% 60% at 50% -10%, ${t.g1} 0%, transparent 70%), ${t.bg}`,
            grid: `linear-gradient(${gl} 1px, transparent 1px) 0 0 / 32px 32px, linear-gradient(90deg, ${gl} 1px, transparent 1px) 0 0 / 32px 32px, radial-gradient(90% 80% at 0% 0%, ${t.g1} 0%, transparent 60%), ${t.bg}`,
            grain: `radial-gradient(110% 90% at 10% 0%, ${t.g1} 0%, transparent 60%), radial-gradient(80% 80% at 100% 100%, ${t.g2} 0%, transparent 60%), ${t.bg}`,
            flat: t.bg
        }[s.bg];
        v["color-scheme"] = t.dark ? "dark" : "light";
        return v;
    }

    function desktopTheme(state) {
        const s = normalize(state);
        return { theme: s.theme, backdrop: BACKDROP_TO_DESKTOP[s.bg], accent: ACCENT_TO_DESKTOP[s.accent], glass: s.glass };
    }

    function sameDesktop(a, b) {
        return a.theme === b.theme && a.backdrop === b.backdrop && a.accent === b.accent && Number(a.glass) === Number(b.glass);
    }

    function presetForDesktop(desktop) {
        return Object.keys(PRESETS).find(name => sameDesktop(desktopTheme(PRESETS[name]), desktop)) || "";
    }

    function fromDesktop(state, desktop) {
        const s = normalize(state);
        if (!desktop || typeof desktop !== "object") return s;
        if (!sameDesktop(desktopTheme(s), desktop)) {
            const preset = presetForDesktop(desktop);
            if (preset) return normalize(PRESETS[preset]);
        }
        const next = clone(s);
        if (THEMES[desktop.theme]) next.theme = desktop.theme;
        const bg = Object.keys(BACKDROP_TO_DESKTOP).find(key => BACKDROP_TO_DESKTOP[key] === desktop.backdrop);
        if (bg) next.bg = bg;
        const accent = Object.keys(ACCENT_TO_DESKTOP).find(key => ACCENT_TO_DESKTOP[key] === desktop.accent);
        if (accent) next.accent = accent;
        if (desktop.glass !== undefined && desktop.glass !== null) next.glass = desktop.glass;
        return normalize(next);
    }

    globalThis.AxioosThemeCatalog = Object.freeze({ THEMES, OPTS, EXTRAS, PRESETS, normalize, presetName, cssVars, desktopTheme, fromDesktop, presetForDesktop, clone });
})();
