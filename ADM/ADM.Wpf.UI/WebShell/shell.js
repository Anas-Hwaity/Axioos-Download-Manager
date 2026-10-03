"use strict";

(() => {
    const catalog = globalThis.AxioosThemeCatalog;
    const app = document.getElementById("app");
    const webview = globalThis.chrome && globalThis.chrome.webview ? globalThis.chrome.webview : null;

    const ICON = {
        all: '<path d="M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z"/>',
        video: '<rect x="3" y="6" width="12" height="12" rx="2"/><path d="M15 10l6-3v10l-6-3"/>',
        music: '<path d="M9 17V5l11-2v12"/><circle cx="6.5" cy="17" r="2.5"/><circle cx="17.5" cy="15" r="2.5"/>',
        app: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>',
        zip: '<path d="M6 3h12v18H6zM12 3v2M12 7v2M12 11v2"/><rect x="10.5" y="14" width="3" height="3"/>',
        disc: '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="2.5"/>',
        doc: '<path d="M6 3h8l4 4v14H6zM14 3v4h4"/>',
        image: '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="9" cy="10" r="2"/><path d="M21 17l-5-5-9 8"/>',
        other: '<path d="M6 3h8l4 4v14H6zM14 3v4h4M9 13h6M9 17h4"/>',
        hist: '<path d="M12 7v5l3 2"/><path d="M4 12a8 8 0 1 0 2.3-5.7M4 4v3h3"/>',
        dash: '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
        set: '<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9L7 7M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1"/>',
        pause: '<path d="M9 5v14M15 5v14"/>',
        play: '<path d="M7 5l12 7-12 7z"/>',
        folder: '<path d="M3 6h6l2 2h10v11H3z"/>',
        open: '<path d="M14 4h6v6M20 4l-9 9M18 14v6H4V6h6"/>',
        trash: '<path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/>',
        more: '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
        retry: '<path d="M20 12a8 8 0 1 1-2.3-5.7M20 4v4h-4"/>',
        search: '<circle cx="11" cy="11" r="7"/><path d="M20 20l-4-4"/>',
        check: '<path d="M5 12l5 5 9-10"/>',
        close: '<path d="M6 6l12 12M18 6L6 18"/>',
        info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.5"/>',
        menu: '<path d="M4 7h16M4 12h16M4 17h16"/>',
        palette: '<path d="M12 3a9 9 0 1 0 0 18c1.5 0 2-1 2-2s-1-1.5-1-2.5 1-1.5 2-1.5h2a4 4 0 0 0 4-4c0-4.4-4-8-9-8z"/><circle cx="7.5" cy="11" r="1"/><circle cx="10" cy="7" r="1"/><circle cx="15" cy="7.5" r="1"/>',
        queue: '<path d="M4 6h11M4 12h11M4 18h7M18 14v6M15 17h6"/>',
        plug: '<path d="M9 3v5M15 3v5M6 8h12v4a6 6 0 0 1-12 0zM12 18v3"/>',
        plus: '<path d="M12 5v14M5 12h14"/>',
        tg: '<path d="M21 4L3 11l6 2 2 6 3-4 5 4z"/><path d="M9 13l12-9"/>',
        gh: '<path d="M9 19c-4 1.5-4-2-6-2.5M15 21v-3.5c0-1 .1-1.4-.5-2 2.8-.3 5.5-1.4 5.5-6a4.6 4.6 0 0 0-1.3-3.2 4.2 4.2 0 0 0-.1-3.2s-1.1-.3-3.5 1.3a12 12 0 0 0-6.2 0C6.5 2.8 5.4 3.1 5.4 3.1a4.2 4.2 0 0 0-.1 3.2A4.6 4.6 0 0 0 4 9.5c0 4.6 2.7 5.7 5.5 6-.6.6-.6 1.2-.5 2V21"/>'
    };

    const CAT_TINT = { video: "#ff6b9a", music: "#b57bff", app: "#4dabf7", zip: "#ffa94d", disc: "#ff8f5a", doc: "#51cf66", image: "#f7c948", other: "#94a3b8" };
    const EXT_KIND = {
        video: ["mp4", "mkv", "webm", "avi", "mov", "flv", "wmv", "m4v", "3gp", "ts", "mpeg", "mpg"],
        music: ["mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "wma"],
        app: ["exe", "msi", "msix", "appx", "apk", "dmg", "deb", "rpm", "bat", "jar"],
        zip: ["zip", "rar", "7z", "tar", "gz", "xz", "bz2", "zst", "tgz", "cab"],
        disc: ["iso", "img", "bin", "vhd", "vhdx", "vmdk"],
        doc: ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "odt", "epub", "csv"],
        image: ["jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "heic", "avif"]
    };

    const bidiClean = value => String(value == null ? "" : value).replace(/[\u202A-\u202E\u2066-\u2069]/g, "");
    const esc = value => String(value == null ? "" : value).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const svg = (name, width) => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="${width || 1.8}" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICON[name] || ICON.other}</svg>`;
    const LOGO = size => `<svg viewBox="0 0 64 64" width="${size}" height="${size}" aria-label="Axioos"><defs><linearGradient id="axlg${size}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#c6ff3d"/><stop offset="1" stop-color="#3dffc6"/></linearGradient></defs><rect x="2" y="2" width="60" height="60" rx="16" fill="url(#axlg${size})"/><path fill="#0a0f02" fill-rule="evenodd" d="M32 9 L53 55 H42.5 L38.8 46.5 H25.2 L21.5 55 H11 Z M25.5 28 H38.5 L32 39 Z"/></svg>`;

    const model = { items: [], buttons: {}, monitoring: false, speedLimitEnabled: false, speedLimitKiB: 0, categories: [], update: false, version: "", developer: {}, capabilities: {} };
    const ui = { filter: "all", cat: "all", sel: new Set(), anchor: null, search: "", drawer: null, pop: null, lastKind: null };
    let appearance = catalog.normalize(null);
    let appearanceBeforeEdit = null;
    const history = new Map();
    const rowRefs = new Map();
    let structureKey = "";

    function send(message) {
        if (webview) {
            try { webview.postMessage(message); } catch (error) { toast("Could not reach Axioos", String(error && error.message || error), null, true); }
        } else {
            mock.handle(message);
        }
    }

    function kindOf(name) {
        const match = /\.([a-z0-9]{1,6})$/i.exec(String(name || ""));
        const ext = match ? match[1].toLowerCase() : "";
        for (const [kind, list] of Object.entries(EXT_KIND)) if (list.includes(ext)) return kind;
        return "other";
    }

    function extOf(name) {
        const match = /\.([a-z0-9]{1,5})$/i.exec(String(name || ""));
        return match ? match[1].toUpperCase() : "FILE";
    }

    function fs(bytes) {
        const value = Number(bytes);
        if (!Number.isFinite(value) || value < 0) return "Unknown size";
        if (value >= 1073741824) return `${(value / 1073741824).toFixed(value >= 10737418240 ? 1 : 2)} GB`;
        if (value >= 1048576) return `${(value / 1048576).toFixed(value >= 104857600 ? 0 : 1)} MB`;
        if (value >= 1024) return `${Math.round(value / 1024)} KB`;
        return `${value} B`;
    }

    function parseSpeed(text) {
        const match = /([\d.,]+)\s*([KMG]?)i?B?\s*\/\s*s/i.exec(String(text || ""));
        if (!match) return 0;
        const base = parseFloat(match[1].replace(",", "."));
        const mult = { "": 1, K: 1024, M: 1048576, G: 1073741824 }[match[2].toUpperCase()] || 1;
        return Number.isFinite(base) ? base * mult : 0;
    }

    function speedText(bytesPerSecond) {
        if (!bytesPerSecond) return "0 KB/s";
        if (bytesPerSecond >= 1048576) return `${(bytesPerSecond / 1048576).toFixed(1)} MB/s`;
        return `${Math.round(bytesPerSecond / 1024)} KB/s`;
    }

    function hostOf(item) {
        return item.host || "";
    }

    function categoryMatches(item, cat) {
        if (cat === "all") return true;
        const category = model.categories.find(c => c.id === cat);
        if (!category) return true;
        const match = /\.([a-z0-9]{1,8})$/i.exec(item.name || "");
        return !!match && category.exts.includes(match[1].toLowerCase());
    }

    function textMatches(item) {
        const q = ui.search.trim().toLowerCase();
        if (!q || /^[a-z]+:\/\//i.test(q)) return true;
        return (item.name || "").toLowerCase().includes(q) || hostOf(item).toLowerCase().includes(q);
    }

    function visibleItems() {
        return model.items.filter(item => (ui.filter === "all" || item.status === ui.filter) && categoryMatches(item, ui.cat) && textMatches(item))
            .sort((a, b) => String(b.added).localeCompare(String(a.added)));
    }

    function counts() {
        const c = { all: 0, active: 0, queued: 0, paused: 0, done: 0, failed: 0 };
        for (const item of model.items) {
            if (!categoryMatches(item, ui.cat)) continue;
            c.all++;
            if (c[item.status] !== undefined) c[item.status]++;
        }
        return c;
    }

    function applyVars() {
        const vars = catalog.cssVars(appearance);
        for (const [key, value] of Object.entries(vars)) {
            if (key === "color-scheme") document.documentElement.style.colorScheme = value;
            else app.style.setProperty(key, value);
        }
        document.body.style.background = catalog.THEMES[appearance.theme].bg;
        app.className = `ax pg-${appearance.progress} ${appearance.density}${appearance.bg === "grain" ? " grain" : ""}${appearance.x.mono ? " mono" : ""}${appearance.x.actions ? " hasacts" : ""}`;
    }

    function selectedItems() {
        return model.items.filter(item => ui.sel.has(item.id));
    }

    function badge(item) {
        const kind = kindOf(item.name);
        if (appearance.badge === "glyph") return `<span class="bd" style="background:color-mix(in srgb, ${CAT_TINT[kind]} 22%, transparent);color:${CAT_TINT[kind]}">${svg(kind === "other" ? "other" : kind, 2)}</span>`;
        if (appearance.badge === "tint") return `<span class="bd" style="background:${CAT_TINT[kind]};color:#14161a">${esc(extOf(item.name))}</span>`;
        return `<span class="bd">${esc(extOf(item.name))}</span>`;
    }

    function sparkPoints(values, w, h) {
        const list = values.length > 1 ? values : [0, 0];
        const max = Math.max(...list, 1) * 1.15;
        return list.map((v, i) => `${(i / (list.length - 1) * w).toFixed(1)},${(h - v / max * h + 1).toFixed(1)}`).join(" ");
    }

    function progressBar(item) {
        const cls = item.status === "paused" ? " warn" : item.status === "failed" ? " bad" : "";
        if (appearance.progress === "dots") {
            return `<div class="pb dots${cls}" data-dots="1">${Array.from({ length: 24 }, () => "<span></span>").join("")}</div>`;
        }
        if (appearance.progress === "thick") return `<div class="pb${cls}"><em class="n" data-f="pct"></em><i data-f="bar"><em class="n" data-f="pctOn"></em></i></div>`;
        return `<div class="pb${cls}"><i data-f="bar"></i></div>`;
    }

    function rowColumns() {
        return `32px minmax(0,1fr)${appearance.x.spark ? " 72px" : ""} minmax(72px, 90px) 96px`;
    }

    function rowActions(item) {
        if (!appearance.x.actions) return "";
        const list = [];
        if (item.kind === "progress") {
            if (item.status === "active" || item.status === "queued") list.push(["pause", "pause", "Pause"]);
            else list.push(["resume", "play", "Resume"]);
        } else {
            list.push(["open", "open", "Open"]);
        }
        if (item.kind !== "progress") list.push(["openFolder", "folder", "Show in folder"]);
        list.push(["menu", "more", "More"]);
        return `<span class="acts">${list.map(([action, icon, label]) => `<button type="button" data-act="${action}" title="${label}" aria-label="${label}">${svg(icon, 2)}</button>`).join("")}</span>`;
    }

    function buildRow(item) {
        const el = document.createElement("div");
        el.className = "row";
        el.dataset.id = item.id;
        el.setAttribute("role", "option");
        el.tabIndex = -1;
        el.style.gridTemplateColumns = rowColumns();
        const showBar = item.kind === "progress" && item.status !== "done";
        el.innerHTML = `${badge(item)}<div class="nm"><b data-f="name"></b><small data-f="meta"></small>${showBar ? progressBar(item) : ""}</div>${appearance.x.spark ? `<svg class="spk" viewBox="0 0 72 24"><polyline data-f="spark" fill="none" stroke="var(--a1)" stroke-width="1.5" stroke-linejoin="round"/></svg>` : ""}<span class="v n" data-f="c1"></span><span class="c2 v m n" data-f="c2"></span>${rowActions(item)}`;
        const refs = {};
        el.querySelectorAll("[data-f]").forEach(node => { refs[node.dataset.f] = node; });
        refs.dots = el.querySelector("[data-dots]");
        return { el, refs, shape: rowShape(item) };
    }

    function rowShape(item) {
        return [item.kind, item.status, item.name, appearance.progress, appearance.badge, appearance.x.spark, appearance.x.actions].join("|");
    }

    function updateRow(entry, item) {
        const r = entry.refs;
        r.name.textContent = bidiClean(item.name);
        r.name.title = bidiClean(item.name);
        const size = fs(item.size);
        let meta;
        let c1 = "";
        let c1cls = "v n";
        let c2 = "";
        if (item.kind === "progress") {
            if (item.status === "active") {
                meta = `${item.progress}% of ${size}${hostOf(item) ? " · " + hostOf(item) : ""}`;
                c1 = item.speedText || "";
                c2 = item.etaText || "";
            } else if (item.status === "paused") {
                meta = `${item.progress}% of ${size} · ${item.statusText || "Paused"}`;
                c1 = "Paused";
                c1cls = "v wa";
                c2 = `${item.progress}%`;
            } else if (item.status === "queued") {
                meta = `${size} · ${item.statusText || "Waiting"}`;
                c1 = "Queued";
                c1cls = "v m";
            } else if (item.status === "failed") {
                meta = `${size} · ${item.statusText || "Stopped"}`;
                c1 = "Failed";
                c1cls = "v ba";
                c2 = "Retry";
            } else {
                meta = `${size} · ${item.statusText || ""}`;
                c1 = item.statusText || "";
            }
        } else {
            meta = `${size}${item.dir ? " · " + item.dir : ""}`;
            c1 = "Done";
            c1cls = "v ok";
            c2 = item.addedText || "";
        }
        r.meta.textContent = meta;
        r.meta.title = meta;
        r.c1.textContent = c1;
        r.c1.className = c1cls;
        r.c2.textContent = c2;
        r.c2.className = item.status === "failed" ? "c2 v ac n" : "c2 v m n";
        if (item.status === "failed") {
            r.c2.dataset.act = "resume";
            r.c2.tabIndex = 0;
            r.c2.setAttribute("role", "button");
        } else {
            delete r.c2.dataset.act;
            r.c2.removeAttribute("tabindex");
            r.c2.removeAttribute("role");
        }
        if (r.bar) r.bar.style.width = `${Math.max(0, Math.min(100, item.progress || 0))}%`;
        if (r.pct) r.pct.textContent = `${item.progress || 0}%`;
        if (r.pctOn) r.pctOn.textContent = `${item.progress || 0}%`;
        if (entry.refs.dots) {
            const filled = Math.floor((item.progress || 0) / 100 * 24);
            [...entry.refs.dots.children].forEach((span, i) => { span.className = i < filled ? "f" : ""; });
        }
        if (r.spark) r.spark.setAttribute("points", item.status === "active" ? sparkPoints((history.get(item.id) || []).slice(-20), 72, 22) : "");
        entry.el.classList.toggle("sel", ui.sel.has(item.id));
        entry.el.setAttribute("aria-selected", ui.sel.has(item.id) ? "true" : "false");
    }

    function renderList(container) {
        const items = visibleItems();
        const listKey = items.map(item => item.id).join(",");
        if (items.length === 0) {
            rowRefs.clear();
            container.innerHTML = `<div class="emptylist"><b>${model.items.length ? "Nothing matches" : "No downloads yet"}</b>${model.items.length ? "Try another filter or clear the search." : "Paste a link above, press Add, or download from your browser."}</div>`;
            container.dataset.key = "";
            return;
        }
        if (container.dataset.key !== listKey || container.querySelector(".emptylist")) {
            const scroll = container.scrollTop;
            container.innerHTML = "";
            for (const item of items) {
                let entry = rowRefs.get(item.id);
                if (!entry || entry.shape !== rowShape(item)) {
                    entry = buildRow(item);
                    rowRefs.set(item.id, entry);
                }
                container.appendChild(entry.el);
            }
            for (const id of [...rowRefs.keys()]) if (!items.some(item => item.id === id)) rowRefs.delete(id);
            container.dataset.key = listKey;
            container.scrollTop = scroll;
        }
        for (const item of items) {
            let entry = rowRefs.get(item.id);
            if (entry.shape !== rowShape(item)) {
                const fresh = buildRow(item);
                entry.el.replaceWith(fresh.el);
                rowRefs.set(item.id, fresh);
                entry = fresh;
            }
            updateRow(entry, item);
        }
    }

    function navHtml() {
        const cats = [{ id: "all", name: "All downloads", icon: "all" }].concat(model.categories.map(c => ({ id: c.id, name: c.name, icon: c.icon })));
        const count = id => model.items.filter(item => categoryMatches(item, id)).length;
        const extra = [["history", "hist", "History"], ["dashboard", "dash", "Dashboard"], ["settings", "set", "Settings"], ["about", "info", "About"]];
        if (appearance.nav === "rail") {
            return `<nav class="nav" aria-label="Categories">${cats.map(c => `<button type="button" class="nb${ui.cat === c.id ? " on" : ""}" data-cat="${esc(c.id)}" title="${esc(c.name)}" aria-label="${esc(c.name)}">${svg(c.icon)}</button>`).join("")}<div class="bot">${extra.map(([a, i, l]) => `<button type="button" class="nb" data-action="${a}" title="${l}" aria-label="${l}">${svg(i)}</button>`).join("")}</div></nav>`;
        }
        return `<nav class="nav" aria-label="Categories">${cats.map(c => `<button type="button" class="nb${ui.cat === c.id ? " on" : ""}" data-cat="${esc(c.id)}" title="${esc(c.name)}">${svg(c.icon)}<span class="lbl">${esc(c.name)}</span><span class="c n">${count(c.id)}</span></button>`).join("")}<div class="bot"><div class="sepl"></div>${extra.map(([a, i, l]) => `<button type="button" class="nb" data-action="${a}" title="${l}">${svg(i)}<span class="lbl">${l}</span></button>`).join("")}</div></nav>`;
    }

    function filtersHtml(c) {
        const states = [["all", "All", c.all], ["active", "Active", c.active], ["queued", "Queued", c.queued], ["paused", "Paused", c.paused], ["done", "Finished", c.done], ["failed", "Failed", c.failed]];
        if (appearance.nav === "tabs") {
            const cats = [{ id: "all", name: "All types" }].concat(model.categories);
            return `<div class="tabsbar" role="tablist">${states.map(([f, l, n]) => `<button type="button" role="tab" class="${ui.filter === f ? "on" : ""}" data-f="${f}">${l}<span class="c n">${n}</span></button>`).join("")}</div><div class="chips">${cats.map(k => `<button type="button" class="chip${ui.cat === k.id ? " on" : ""}" data-cat="${esc(k.id)}">${esc(k.name)}</button>`).join("")}</div>`;
        }
        return `<div class="chips" role="tablist">${states.map(([f, l, n]) => `<button type="button" role="tab" class="chip${ui.filter === f ? " on" : ""}" data-f="${f}">${l} <span class="n">${n}</span></button>`).join("")}</div>`;
    }

    function totalSpeed() {
        return model.items.filter(i => i.status === "active").reduce((sum, i) => sum + parseSpeed(i.speedText), 0);
    }

    function connectionCount(item) {
        const live = Number(item.connections);
        if (Number.isFinite(live) && live > 0) return live;
        return item.status === "active" ? 4 : 1;
    }

    function downloadedBytes(item) {
        const size = Math.max(0, Number(item.size) || 0);
        const live = Number(item.downloaded);
        if (Number.isFinite(live) && live > 0) return size > 0 ? Math.min(size, live) : live;
        return size * Math.max(0, Math.min(100, Number(item.progress) || 0)) / 100;
    }

    function visual(item) {
        const act = item.status === "active";
        const p = Math.max(0, Math.min(100, Number(item.progress) || 0));
        if (appearance.insp === "blocks") {
            const f = Math.floor(p / 100 * 16);
            const cells = Array.from({ length: 16 }, (_, i) => i < f ? '<i class="f"></i>' : act && i < f + 3 ? '<i class="a pulse"></i>' : "<i></i>").join("");
            return `<div class="cap"><span>${connectionCount(item)} connections</span><span class="n">${Math.floor(p)}%</span></div><div class="segm">${cells}</div>`;
        }
        if (appearance.insp === "lanes") {
            const offsets = [24, 14, 6, -3, -11, -19, -26, -32];
            const n = Math.min(connectionCount(item), 6);
            const lanes = offsets.slice(0, n).map(o => `<div><i class="${act ? "h" : ""}" style="width:${Math.max(2, Math.min(100, p + o)).toFixed(1)}%"></i></div>`).join("");
            return `<div class="cap"><span>${n} live connections</span><span class="n">${Math.floor(p)}%</span></div><div class="lanes">${lanes}</div>`;
        }
        if (appearance.insp === "ring") {
            const fill = appearance.accent === "grad" ? `conic-gradient(var(--a1), var(--a2) ${p}%, var(--track) ${p}% 100%)` : `conic-gradient(var(--a1) 0 ${p}%, var(--track) ${p}% 100%)`;
            return `<div class="ringw"><div class="ring" style="background:${fill}"></div><div class="ringt"><span><b class="n">${Math.floor(p)}%</b><small class="n">${esc(fs(downloadedBytes(item)))} of ${esc(fs(item.size))}</small></span></div></div>`;
        }
        const hist = history.get(item.id) || [];
        const values = hist.length > 1 ? hist.slice(-40) : [0, 0];
        const max = Math.max(...values, 1) * 1.2;
        const pts = values.map((v, i) => [i / (values.length - 1) * 260, 78 - v / max * 74]);
        const line = pts.map((pt, i) => `${i ? "L" : "M"}${pt[0].toFixed(1)} ${pt[1].toFixed(1)}`).join(" ");
        return `<div class="cap"><span>Speed, last 40 seconds</span><span class="n">${act ? esc(item.speedText || "") : "paused"}</span></div><div class="graph"><svg viewBox="0 0 260 80" preserveAspectRatio="none"><path d="${line} L260 80 L0 80 Z" fill="var(--selbg)"/><path d="${line}" fill="none" stroke="var(--a1)" stroke-width="2" vector-effect="non-scaling-stroke"/></svg></div>`;
    }

    function buttonHtml(name, label, primary) {
        const state = model.buttons[name];
        if (state && state.visible === false) return "";
        const disabled = state && state.enabled === false ? " disabled" : "";
        return `<button type="button" class="btn s${primary ? "" : " gh"}" data-button="${name}"${disabled}>${label}</button>`;
    }

    function inspectorHtml() {
        const sel = selectedItems();
        if (sel.length === 0) {
            const c = counts();
            return `<aside class="insp" aria-label="Details"><div class="empty"><b>Select a download</b>${c.active} downloading · ${esc(speedText(totalSpeed()))}</div></aside>`;
        }
        if (sel.length > 1) {
            return `<aside class="insp" aria-label="Details"><div><h4>${sel.length} downloads selected</h4><div class="sub">${esc(fs(sel.reduce((s, i) => s + Math.max(0, Number(i.size) || 0), 0)))} in total</div></div><div class="btns">${buttonHtml("resume", "Resume", true)}${buttonHtml("pause", "Pause")}${buttonHtml("delete", "Delete")}</div></aside>`;
        }
        const item = sel[0];
        const saved = item.dir ? `<dt>Saved to</dt><dd>${esc(item.dir)}</dd>` : "";
        let body = "";
        if (item.kind === "progress" && (item.status === "active" || item.status === "paused")) {
            body += `<div data-vis>${visual(item)}</div>`;
            body += `<dl><dt>Downloaded</dt><dd class="n">${esc(fs(downloadedBytes(item)))} of ${esc(fs(item.size))}</dd>`;
            body += item.status === "active"
                ? `<dt>Speed</dt><dd class="n">${esc(item.speedText || "")}</dd><dt>Time left</dt><dd class="n">${esc(item.etaText || "")}</dd>`
                : `<dt>Status</dt><dd class="wa">Paused</dd>`;
            body += `<dt>Resume</dt><dd>${esc(item.resume || "Unknown")}</dd>${saved}</dl>`;
        } else if (item.kind === "progress" && item.status === "queued") {
            body += `<dl><dt>Size</dt><dd class="n">${esc(fs(item.size))}</dd><dt>Position</dt><dd>${esc(item.statusText || "Waiting in queue")}</dd>${saved}</dl><div class="btns">${buttonHtml("resume", "Start now", true)}</div>`;
        } else if (item.kind === "progress" && item.status === "failed") {
            body += `<div class="errbox">${esc(item.statusText || "This download stopped with an error.")}</div><dl><dt>Downloaded</dt><dd class="n">${esc(fs(downloadedBytes(item)))} of ${esc(fs(item.size))}</dd>${saved}</dl>`;
            body += `<div class="btns">${["Http", "Dash"].includes(item.downloadType) ? '<button type="button" class="btn s" data-context="refresh">Refresh link</button>' : ""}${buttonHtml("resume", "Retry")}</div>`;
        } else {
            const when = item.addedText ? " " + esc(String(item.addedText).charAt(0).toLowerCase() + String(item.addedText).slice(1)) : " just now";
            body += `<div class="donebig"><i>${svg("check", 2.4)}</i>Finished${when}</div><dl><dt>Size</dt><dd class="n">${esc(fs(item.size))}</dd>${saved}</dl>`;
            body += `<div class="btns">${buttonHtml("open", "Open", true)}${buttonHtml("openFolder", "Show in folder")}</div>`;
        }
        const sub = [item.host, item.via].filter(Boolean).map(esc).join(" &middot; ");
        return `<aside class="insp" aria-label="Details"><div><h4>${esc(bidiClean(item.name))}</h4><div class="sub">${sub || esc(item.dir || "")}</div></div>${body}</aside>`;
    }

    function limitText() {
        return model.speedLimitEnabled && Number(model.speedLimitKiB) > 0
            ? speedText(Number(model.speedLimitKiB) * 1024) : "off";
    }

    function statusLeftHtml() {
        const c = counts();
        return `<span><b class="n">${c.active}</b> downloading</span><span class="n">${esc(speedText(totalSpeed()))}</span><span>Speed limit ${esc(limitText())}</span>`;
    }

    function statusHtml() {
        return `<footer class="sb"><span class="sbl">${statusLeftHtml()}</span>${statusRightHtml()}</footer>`;
    }

    function statusRightHtml() {
        return `<span class="r"><button type="button" class="sbb" data-action="toggleMonitoring" aria-pressed="${model.monitoring}"><span class="dot${model.monitoring ? " on" : ""}"></span>Browser monitoring ${model.monitoring ? "on" : "off"}</button><button type="button" class="sbb" data-action="scheduler">${svg("queue")}Queue and scheduler</button><button type="button" class="sbb" data-action="appearance">${svg("palette")}Appearance</button></span>`;
    }

    function headerHtml() {
        const lim = appearance.x.limit ? `<button type="button" class="lim n" data-action="speedLimit" title="Change speed limit">Limit<span class="tr"><i></i><b></b></span><span>${esc(limitText())}</span></button>` : "";
        const upd = model.update ? `<button type="button" class="sbb" data-action="update">Update available</button>` : "";
        return `<header class="tb"><div class="brand">${LOGO(22)}Axioos</div><span class="sub">Downloads</span><div class="sp"></div>${upd}${lim}<button type="button" class="tbi" data-action="appearance" title="Appearance" aria-label="Appearance">${svg("palette")}</button><button type="button" class="tbi" data-action="about" title="About Axioos" aria-label="About Axioos">${svg("info")}</button><button type="button" class="tbi" data-menu="main" title="Menu" aria-label="Menu">${svg("menu")}</button></header>`;
    }

    let layoutKey = "";
    const chromeHtml = new Map();
    function swap(selector, html) {
        const node = app.querySelector(selector);
        if (!node || chromeHtml.get(selector) === html) return;
        node.outerHTML = html;
        chromeHtml.set(selector, html);
    }
    function render(force) {
        let forced = false;
        let listScroll = 0;
        let searchFocus = null;
        applyVars();
        const c = counts();
        const key = [appearance.nav, appearance.x.status, appearance.x.drop, model.categories.map(x => x.id).join(","), ui.drawer].join("|");
        if (force || key !== layoutKey || !app.querySelector(".body")) {
            layoutKey = key;
            rowRefs.clear();
            chromeHtml.clear();
            const oldList = app.querySelector('[data-slot="list"]');
            listScroll = oldList ? oldList.scrollTop : 0;
            const q = document.getElementById("q");
            searchFocus = q && document.activeElement === q ? [q.selectionStart, q.selectionEnd] : null;
            forced = true;
            const drawerBody = app.querySelector(".dbody");
            const drawerScroll = drawerBody ? drawerBody.scrollTop : 0;
            const noInsp = narrowWindow();
            app.innerHTML = `${headerHtml()}<div class="body ${appearance.nav}${noInsp ? " noinsp" : ""}">${appearance.nav !== "tabs" ? navHtml() : ""}<main class="mid"><div class="search"><label class="in">${svg("search")}<input id="q" type="text" spellcheck="false" autocomplete="off" placeholder="Paste a link or search your downloads" value="${esc(ui.search)}" aria-label="Paste a link or search"><kbd>Ctrl K</kbd></label><button type="button" class="btn" data-action="add">Add</button></div><div data-slot="filters"></div><div class="list" role="listbox" aria-multiselectable="true" aria-label="Downloads" data-slot="list"></div>${appearance.x.drop ? `<div class="dropz">Drop links anywhere in this window to download them</div>` : ""}</main><div data-slot="insp"></div></div>${appearance.x.status ? statusHtml() : ""}<div id="toastc"></div>${drawerHtml()}`;
            app.style.gridTemplateRows = appearance.x.status ? "48px minmax(0,1fr) 34px" : "48px minmax(0,1fr)";
            const nextDrawer = app.querySelector(".dbody");
            if (nextDrawer) nextDrawer.scrollTop = drawerScroll;
        } else {
            swap(".tb", headerHtml());
            swap(".nav", navHtml());
            const sbl = app.querySelector(".sb .sbl");
            const left = statusLeftHtml();
            if (sbl && chromeHtml.get(".sbl") !== left) { sbl.innerHTML = left; chromeHtml.set(".sbl", left); }
            swap(".sb .r", statusRightHtml());
        }
        const filters = app.querySelector('[data-slot="filters"]');
        const fkey = JSON.stringify([c, ui.filter, ui.cat, appearance.nav]);
        if (filters.dataset.key !== fkey) { filters.innerHTML = filtersHtml(c); filters.dataset.key = fkey; }
        const listEl = app.querySelector('[data-slot="list"]');
        renderList(listEl);
        if (forced) {
            listEl.scrollTop = listScroll;
            const q = document.getElementById("q");
            if (q && searchFocus) { q.focus(); q.setSelectionRange(searchFocus[0], searchFocus[1]); }
            chromeHtml.set(".tb", headerHtml());
            chromeHtml.set(".nav", navHtml());
            chromeHtml.set(".sbl", statusLeftHtml());
            chromeHtml.set(".sb .r", statusRightHtml());
        }
        const insp = app.querySelector('[data-slot="insp"]');
        const docked = aboutDocked();
        const ikey = docked ? "about|" + (ui.aboutTab || "about") : inspectorKey();
        if (insp.dataset.key !== ikey) {
            const html = docked ? aboutDrawer(true) : inspectorHtml();
            if (forced || insp.dataset.html !== html) { insp.innerHTML = html; insp.dataset.html = html; }
            insp.dataset.key = ikey;
        }
        if (forced || !!ui.pop !== !!document.getElementById("pop")) renderPop();
    }

    function inspectorKey() {
        const sel = selectedItems();
        return JSON.stringify([sel.map(i => [i.id, i.status, i.progress, i.speedText, i.etaText, i.statusText, i.size, i.connections, i.resume, i.downloaded, i.via, i.dir, i.addedText, (history.get(i.id) || []).length]), model.buttons, appearance.insp, appearance.accent]);
    }

    function narrowWindow() {
        return window.innerWidth < 860;
    }

    function aboutDocked() {
        return ui.drawer === "about" && !narrowWindow();
    }

    function drawerHtml() {
        if (ui.drawer === "appearance") return appearanceDrawer();
        if (ui.drawer === "about" && narrowWindow()) return aboutDrawer(false);
        return "";
    }

    function presetBlurb(name) {
        return { Signature: "Glacier, icon rail, thin bars", "Night shift": "Graphite, sidebar, compact", Neon: "Cyber Lime, grid, stripes", Daylight: "Paper, top tabs, light" }[name] || "";
    }

    function appearanceDrawer() {
        const current = catalog.presetName(appearance);
        let h = `<section class="drawer" role="dialog" aria-label="Appearance" data-drawer="appearance"><header><h3>Appearance</h3><button type="button" class="tbi" data-action="closeDrawer" aria-label="Close">${svg("close", 2)}</button></header><div class="dbody">`;
        h += `<div class="grp"><h5>Presets</h5><div class="presets">${Object.keys(catalog.PRESETS).map(p => `<button type="button" class="${current === p ? "on" : ""}" data-preset="${esc(p)}"><b>${esc(p)}</b><small>${esc(presetBlurb(p))}</small></button>`).join("")}</div></div>`;
        h += `<div class="grp"><h5>Colour theme</h5><div class="sw">${Object.entries(catalog.THEMES).map(([k, t]) => `<button type="button" class="${appearance.theme === k ? "on" : ""}" data-theme="${k}"><i style="background:linear-gradient(135deg, ${t.a1}, ${t.a2} 55%, ${t.bg} 56%)"></i>${esc(t.name)}</button>`).join("")}</div></div>`;
        for (const [key, o] of Object.entries(catalog.OPTS)) {
            h += `<div class="grp"><h5>${esc(o.label)}</h5><div class="segc" data-key="${key}">${o.items.map(([v, l]) => `<button type="button" class="${appearance[key] === v ? "on" : ""}" data-v="${esc(v)}">${esc(l)}</button>`).join("")}</div></div>`;
        }
        h += `<div class="grp"><h5>Frost</h5><label class="rng"><span>Clear</span><input type="range" min="0" max="100" step="5" value="${appearance.glass}" data-glass aria-label="Frost"><span>Frosted</span></label></div>`;
        h += `<div class="grp"><h5>Little extras</h5>${catalog.EXTRAS.map(([k, l]) => `<label class="tg"><input type="checkbox" data-x="${k}"${appearance.x[k] ? " checked" : ""}>${esc(l)}</label>`).join("")}</div>`;
        h += `<p class="fine">Every window, dialog and the browser extension use this theme.</p>`;
        h += `</div><footer><button type="button" class="btn s gh" data-action="cancelAppearance">Cancel</button><button type="button" class="btn s" data-action="saveAppearance">Save</button></footer></section>`;
        return h;
    }

    function aboutDrawer(docked) {
        const dev = model.developer || {};
        const tab = ui.aboutTab === "program" ? "program" : "about";
        const tabs = `<div class="atabs" role="tablist"><button type="button" role="tab" class="atab${tab === "about" ? " on" : ""}" aria-selected="${tab === "about"}" data-action="aboutTab" data-tab="about">About</button><button type="button" role="tab" class="atab${tab === "program" ? " on" : ""}" aria-selected="${tab === "program"}" data-action="aboutTab" data-tab="program">What Axioos adds</button></div>`;
        let body;
        if (tab === "program") {
            const sections = Array.isArray(model.releaseNotes) ? model.releaseNotes : [];
            body = `<div class="dbody notes"><p>${esc(model.releaseSummary || "")}</p>${sections.map(section => `<div class="grp"><h5>${esc(section.title)}</h5><ul>${(section.items || []).map(item => `<li>${esc(item)}</li>`).join("")}</ul></div>`).join("")}<p class="fine">${esc(model.baseVersion || "")}</p></div>`;
        } else {
            body = `<div class="dbody about"><div class="logo">${LOGO(72)}</div><h4>Axioos Download Manager</h4><div class="fine">${esc(model.version || "")}</div><div class="fine">${esc(model.baseVersion || "")}</div><div class="card"><small>Developed by</small><b>${esc(dev.name || "Anas Al Hwaity")}</b></div><button type="button" class="link" data-action="openTelegram">${svg("tg")}<span><small>Telegram</small>${esc(dev.telegram || "@Anas12rm")}</span></button><button type="button" class="link" data-action="openGitHub">${svg("gh")}<span><small>GitHub</small>github.com/Anas-Hwaity</span></button><p class="fine">Based on Xtreme Download Manager © 2013 - 2023 Subhra Das Gupta, GPL-2.0.</p></div>`;
        }
        return `<section class="drawer${docked ? " docked" : ""}" role="${docked ? "region" : "dialog"}" aria-label="About Axioos" data-drawer="about"><header><h3>About</h3><button type="button" class="tbi" data-action="closeDrawer" aria-label="Close">${svg("close", 2)}</button></header>${tabs}${body}</section>`;
    }

    function renderPop() {
        const old = document.getElementById("pop");
        if (old) old.remove();
        if (!ui.pop) return;
        const el = document.createElement("div");
        el.id = "pop";
        el.className = "pop";
        el.setAttribute("role", "menu");
        el.innerHTML = ui.pop.items.map(entry => entry === "-" ? "<hr>" : `<button type="button" role="menuitem" data-pop="${esc(entry.id)}"${entry.enabled === false ? " disabled" : ""}>${entry.icon ? svg(entry.icon) : ""}${esc(entry.text)}</button>`).join("");
        app.appendChild(el);
        const rect = el.getBoundingClientRect();
        const x = Math.min(ui.pop.x, window.innerWidth - rect.width - 8);
        const y = ui.pop.y + rect.height > window.innerHeight - 8 ? Math.max(8, ui.pop.y - rect.height) : ui.pop.y;
        el.style.left = `${Math.max(8, x)}px`;
        el.style.top = `${y}px`;
        const first = el.querySelector("button:not(:disabled)");
        if (first && ui.pop.focus) first.focus();
    }

    function openPop(items, anchor, handler) {
        const rect = anchor && anchor.getBoundingClientRect ? anchor.getBoundingClientRect() : null;
        ui.pop = { items, handler, x: rect ? rect.right - 230 : anchor.x, y: rect ? rect.bottom + 6 : anchor.y, focus: !!rect };
        renderPop();
    }

    function closePop() {
        ui.pop = null;
        renderPop();
    }

    function mainMenuItems() {
        return [
            { id: "newDownload", text: "New download", icon: "plus" }, { id: "videoDownload", text: "Video download", icon: "video" }, { id: "batchDownload", text: "Batch download", icon: "queue" }, "-",
            { id: "scheduler", text: "Queue and scheduler", icon: "queue" }, { id: "history", text: "History", icon: "hist" }, { id: "dashboard", text: "Dashboard", icon: "dash" }, { id: "mediaGrabber", text: "Media grabber", icon: "video" }, "-",
            { id: "appearance", text: "Appearance", icon: "palette" }, { id: "settings", text: "Settings", icon: "set" }, { id: "monitoringSettings", text: "Browser monitoring settings", icon: "plug" }, { id: "language", text: "Language", icon: "other" }, "-",
            { id: "import", text: "Import download list", icon: "other" }, { id: "export", text: "Export download list", icon: "other" }, { id: "clearFinished", text: "Clear finished downloads", icon: "trash" }, "-",
            { id: "help", text: "Help and support", icon: "info" }, { id: "reportProblem", text: "Report a problem", icon: "gh" }, { id: "checkUpdate", text: "Check for updates", icon: "retry" }, { id: "about", text: "About Axioos", icon: "info" }, "-",
            { id: "exit", text: "Exit Axioos", icon: "close" }
        ];
    }

    function toast(title, body, actions, always) {
        if (!appearance.x.toast && !always) return;
        const box = document.getElementById("toastc");
        if (!box) return;
        const el = document.createElement("div");
        el.className = "toast";
        el.setAttribute("role", "status");
        el.innerHTML = `<b>${esc(title)}</b>${body ? `<p>${esc(body)}</p>` : ""}${actions && actions.length ? `<div class="btns">${actions.map((action, index) => `<button type="button" class="btn s${index ? " gh" : ""}" data-toast="${esc(action.name)}" data-id="${esc(action.id)}">${esc(action.label)}</button>`).join("")}</div>` : ""}`;
        box.appendChild(el);
        setTimeout(() => el.remove(), 5200);
    }

    function selectOnly(id) {
        ui.sel = new Set(id ? [id] : []);
        ui.anchor = id;
        syncSelection();
    }

    function pruneSelection() {
        const visible = new Set(visibleItems().map(item => item.id));
        const kept = [...ui.sel].filter(id => visible.has(id));
        if (kept.length === ui.sel.size) return false;
        ui.sel = new Set(kept);
        if (!ui.sel.has(ui.anchor)) ui.anchor = kept[0] || null;
        syncSelection();
        return true;
    }

    function syncSelection() {
        const ids = [...ui.sel];
        const first = model.items.find(item => item.id === ids[0]);
        send({ cmd: "select", ids, kind: first ? first.kind : ui.lastKind || "progress" });
        render();
    }

    function onRowClick(event, id) {
        const item = model.items.find(i => i.id === id);
        if (!item) return;
        if (event.shiftKey && ui.anchor) {
            const list = visibleItems().filter(i => i.kind === item.kind);
            const a = list.findIndex(i => i.id === ui.anchor);
            const b = list.findIndex(i => i.id === id);
            if (a >= 0 && b >= 0) {
                ui.sel = new Set(list.slice(Math.min(a, b), Math.max(a, b) + 1).map(i => i.id));
                ui.lastKind = item.kind;
                syncSelection();
                return;
            }
        }
        if (event.ctrlKey || event.metaKey) {
            const sameKind = selectedItems().every(i => i.kind === item.kind);
            if (!sameKind) ui.sel.clear();
            if (ui.sel.has(id)) ui.sel.delete(id); else ui.sel.add(id);
            ui.anchor = id;
            ui.lastKind = item.kind;
            syncSelection();
            return;
        }
        ui.lastKind = item.kind;
        selectOnly(id);
    }

    function requestContextMenu(id, point) {
        if (id && !ui.sel.has(id)) selectOnly(id);
        const item = model.items.find(i => i.id === (id || [...ui.sel][0]));
        if (!item) return;
        ui.pendingMenu = { x: point.x, y: point.y };
        send({ cmd: "menu", kind: item.kind });
    }

    function runAction(action, target) {
        if (action === "appearance" && ui.drawer === "appearance") { runAction("closeDrawer"); return; }
        if (action === "about" && ui.drawer === "appearance" && appearanceBeforeEdit) { appearance = appearanceBeforeEdit; appearanceBeforeEdit = null; }
        if (action === "appearance") { appearanceBeforeEdit = catalog.clone(appearance); ui.drawer = "appearance"; render(true); return; }
        if (action === "about") { if (ui.drawer === "about") { ui.drawer = null; } else { ui.drawer = "about"; ui.aboutTab = "about"; } render(true); return; }
        if (action === "aboutTab") { ui.aboutTab = target?.dataset?.tab === "program" ? "program" : "about"; render(true); return; }
        if (action === "closeDrawer") { if (ui.drawer === "appearance" && appearanceBeforeEdit) appearance = appearanceBeforeEdit; ui.drawer = null; appearanceBeforeEdit = null; render(true); return; }
        if (action === "cancelAppearance") { if (appearanceBeforeEdit) appearance = appearanceBeforeEdit; appearanceBeforeEdit = null; ui.drawer = null; render(true); return; }
        if (action === "saveAppearance") {
            appearanceBeforeEdit = null;
            ui.drawer = null;
            send({ cmd: "appearance", state: appearance, desktop: catalog.desktopTheme(appearance) });
            render(true);
            toast("Appearance saved", catalog.THEMES[appearance.theme].name + " is now your theme.");
            return;
        }
        if (action === "add") {
            const value = ui.search.trim();
            if (/^(https?|ftp):\/\/\S+$/i.test(value)) {
                send({ cmd: "addUrl", url: value });
                ui.search = "";
                const q = document.getElementById("q");
                if (q) q.value = "";
                render();
            } else {
                openPop([{ id: "newDownload", text: "New download", icon: "plus" }, { id: "videoDownload", text: "Video download", icon: "video" }, { id: "batchDownload", text: "Batch download", icon: "queue" }], target, id => send({ cmd: "action", name: id }));
            }
            return;
        }
        if (action === "openTelegram") { send({ cmd: "openUrl", which: "telegram" }); return; }
        if (action === "openGitHub") { send({ cmd: "openUrl", which: "github" }); return; }
        send({ cmd: "action", name: action });
    }

    app.addEventListener("click", event => {
        const t = event.target;
        const toastAction = t.closest && t.closest("[data-toast]");
        if (toastAction) {
            const id = toastAction.dataset.id;
            if (model.items.some(item => item.id === id)) selectOnly(id);
            send({ cmd: "button", name: toastAction.dataset.toast });
            toastAction.closest(".toast").remove();
            return;
        }
        const pop = t.closest && t.closest("[data-pop]");
        if (pop) {
            const handler = ui.pop && ui.pop.handler;
            const id = pop.dataset.pop;
            closePop();
            if (handler) handler(id);
            return;
        }
        if (ui.pop && !t.closest("#pop")) closePop();
        const menuBtn = t.closest("[data-menu]");
        if (menuBtn) { openPop(mainMenuItems(), menuBtn, id => runAction(id)); return; }
        const contextButton = t.closest("[data-context]");
        if (contextButton) { send({ cmd: "contextAction", name: contextButton.dataset.context }); return; }
        const act = t.closest("[data-act]");
        if (act) {
            const row = act.closest(".row");
            const id = row ? row.dataset.id : [...ui.sel][0];
            if (row && act.dataset.act === "menu" ? !ui.sel.has(id) : row && (ui.sel.size !== 1 || !ui.sel.has(id))) selectOnly(id);
            if (act.dataset.act === "menu") { const r = act.getBoundingClientRect(); requestContextMenu(id, { x: r.right - 230, y: r.bottom + 6 }); return; }
            send({ cmd: "button", name: act.dataset.act });
            return;
        }
        const b = t.closest("[data-button]");
        if (b) { send({ cmd: "button", name: b.dataset.button }); return; }
        const preset = t.closest("[data-preset]");
        if (preset) { appearance = catalog.normalize(catalog.PRESETS[preset.dataset.preset]); render(true); return; }
        const theme = t.closest("[data-theme]");
        if (theme) { appearance.theme = theme.dataset.theme; render(true); return; }
        const seg = t.closest(".segc button");
        if (seg) { appearance[seg.parentElement.dataset.key] = seg.dataset.v; render(true); return; }
        const cat = t.closest("[data-cat]");
        if (cat) { ui.cat = cat.dataset.cat; if (!pruneSelection()) render(); return; }
        const f = t.closest("[data-f]");
        if (f && f.closest(".chips, .tabsbar")) { ui.filter = f.dataset.f; if (!pruneSelection()) render(); return; }
        const action = t.closest("[data-action]");
        if (action) { runAction(action.dataset.action, action); return; }
        const row = t.closest(".row");
        if (row) { onRowClick(event, row.dataset.id); return; }
        if (t.closest(".list") && !t.closest(".row")) { ui.sel.clear(); syncSelection(); }
    });

    app.addEventListener("dblclick", event => {
        const row = event.target.closest(".row");
        if (!row || event.target.closest("[data-act]")) return;
        selectOnly(row.dataset.id);
        send({ cmd: "open" });
    });

    app.addEventListener("contextmenu", event => {
        if (event.target.closest("input, textarea")) return;
        event.preventDefault();
        const row = event.target.closest(".row");
        if (row) requestContextMenu(row.dataset.id, { x: event.clientX, y: event.clientY });
    });

    app.addEventListener("input", event => {
        const t = event.target;
        if (t.id === "q") { ui.search = t.value; if (!pruneSelection()) render(); return; }
        if (t.dataset && t.dataset.glass !== undefined) { appearance.glass = Number(t.value); applyVars(); return; }
    });

    app.addEventListener("change", event => {
        const t = event.target;
        if (t.dataset && t.dataset.x) { appearance.x[t.dataset.x] = t.checked; render(true); }
    });

    app.addEventListener("keydown", event => {
        if (event.target.matches && event.target.matches('[role="button"][data-act]') && (event.key === "Enter" || event.key === " ")) {
            event.preventDefault();
            event.stopPropagation();
            event.target.click();
            return;
        }
        if (event.target.id === "q" && event.key === "Enter") { runAction("add", app.querySelector('[data-action="add"]')); }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            if (ui.pop) { closePop(); return; }
            if (ui.drawer) { runAction("closeDrawer"); return; }
        }
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") { event.preventDefault(); const q = document.getElementById("q"); if (q) q.focus(); return; }
        if (event.target.closest && event.target.closest("input, textarea")) return;
        if (event.target.closest && event.target.closest("button, [role=button], #pop, .drawer") && ["Enter", " ", "ArrowDown", "ArrowUp"].includes(event.key)) {
            if (ui.pop && event.target.closest("#pop") && event.key.startsWith("Arrow")) {
                event.preventDefault();
                const buttons = [...document.querySelectorAll("#pop button:not(:disabled)")];
                const next = buttons[(buttons.indexOf(event.target.closest("button")) + (event.key === "ArrowDown" ? 1 : -1) + buttons.length) % buttons.length];
                if (next) next.focus();
            }
            return;
        }
        if (event.key === "Delete" && ui.sel.size && !pruneSelection()) { send({ cmd: "button", name: "delete" }); return; }
        if (event.key === "Enter" && ui.sel.size === 1) { send({ cmd: "open" }); return; }
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "a") {
            event.preventDefault();
            const list = visibleItems();
            const kind = (selectedItems()[0] || list[0] || {}).kind;
            ui.sel = new Set(list.filter(i => i.kind === kind).map(i => i.id));
            syncSelection();
            return;
        }
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            const list = visibleItems();
            if (!list.length) return;
            event.preventDefault();
            const idx = list.findIndex(i => i.id === ui.anchor);
            const next = list[Math.max(0, Math.min(list.length - 1, idx + (event.key === "ArrowDown" ? 1 : -1)))];
            ui.lastKind = next.kind;
            selectOnly(next.id);
            const entry = rowRefs.get(next.id);
            if (entry) entry.el.scrollIntoView({ block: "nearest" });
        }
    });

    window.addEventListener("dragover", event => { event.preventDefault(); });
    window.addEventListener("drop", event => {
        event.preventDefault();
        const text = (event.dataTransfer && (event.dataTransfer.getData("text/uri-list") || event.dataTransfer.getData("text/plain"))) || "";
        const urls = text.split(/\s+/).filter(v => /^(https?|ftp):\/\//i.test(v)).slice(0, 50);
        for (const url of urls) send({ cmd: "addUrl", url });
        if (urls.length) toast(urls.length === 1 ? "Link received" : `${urls.length} links received`, "Axioos is preparing the download.");
    });
    let lastNarrow = narrowWindow();
    window.addEventListener("resize", () => {
        const narrow = narrowWindow();
        if (narrow === lastNarrow) return;
        lastNarrow = narrow;
        render(true);
    });

    let lastHistoryAt = 0;
    function recordHistory() {
        const now = Date.now();
        if (now - lastHistoryAt < 900) return;
        lastHistoryAt = now;
        for (const item of model.items) {
            if (item.kind !== "progress") continue;
            const list = history.get(item.id) || [];
            list.push(item.status === "active" ? parseSpeed(item.speedText) : 0);
            if (list.length > 60) list.shift();
            history.set(item.id, list);
        }
        for (const id of [...history.keys()]) if (!model.items.some(item => item.id === id)) history.delete(id);
    }

    function onHostMessage(message) {
        if (!message || typeof message !== "object") return;
        if (message.type === "state") {
            const previous = new Map(model.items.map(item => [item.id, item.status]));
            const previousKind = new Map(model.items.map(item => [item.id, item.kind]));
            Object.assign(model, message.state || {});
            if (Array.isArray(model.items)) {
                for (const item of model.items) {
                    const was = previous.get(item.id);
                    if (was && was !== "done" && item.kind === "finished") {
                        toast(`Finished: ${item.name}`, `Saved to ${item.dir || "the download folder"}`, [
                            { name: "open", label: "Open", id: item.id },
                            { name: "openFolder", label: "Show in folder", id: item.id }
                        ]);
                    }
                }
            }
            for (const id of [...ui.sel]) if (!model.items.some(item => item.id === id)) ui.sel.delete(id);
            const moved = [...ui.sel].filter(id => {
                const item = model.items.find(entry => entry.id === id);
                return item && previousKind.has(id) && previousKind.get(id) !== item.kind;
            });
            if (moved.length && ui.sel.size > moved.length) for (const id of moved) ui.sel.delete(id);
            recordHistory();
            if (moved.length) syncSelection();
            else render();
        } else if (message.type === "appearance") {
            appearance = message.desktop ? catalog.fromDesktop(message.state || appearance, message.desktop) : catalog.normalize(message.state);
            render(true);
        } else if (message.type === "menu") {
            const point = ui.pendingMenu || { x: 200, y: 200 };
            ui.pendingMenu = null;
            const items = (message.items || []).filter(i => i.visible !== false).map(i => ({ id: i.name, text: i.text, enabled: i.enabled }));
            if (items.length) openPop(items, point, id => send({ cmd: "menuInvoke", name: id }));
        } else if (message.type === "toast") {
            toast(message.title || "", message.body || "", null, true);
        } else if (message.type === "focusSearch") {
            const q = document.getElementById("q");
            if (q) q.focus();
        }
    }

    const mock = {
        timer: 0,
        handle(message) {
            if (message.cmd === "ready") { this.start(); return; }
            if (message.cmd === "select") { this.updateButtons(); onHostMessage({ type: "state", state: { buttons: model.buttons } }); return; }
            if (message.cmd === "menu") {
                const k = message.kind;
                const items = k === "progress"
                    ? [["pause", "Pause"], ["resume", "Resume"], ["delete", "Delete"], ["saveAs", "Save as"], ["refresh", "Refresh link"], ["showProgress", "Show progress"], ["copyURL", "Copy address"], ["restart", "Restart"], ["moveToQueue", "Move to queue"], ["properties", "Properties"]]
                    : [["open", "Open"], ["openFolder", "Open folder"], ["deleteDownloads", "Delete downloads"], ["copyURL1", "Copy address"], ["copyFile", "Copy file"], ["downloadAgain", "Download again"], ["properties1", "Properties"]];
                onHostMessage({ type: "menu", items: items.map(([name, text]) => ({ name, text, enabled: true, visible: true })) });
                return;
            }
            if (message.cmd === "button") {
                const sel = selectedItems();
                for (const item of sel) {
                    if (message.name === "pause" && item.status === "active") { item.status = "paused"; item.statusText = "Paused"; }
                    if (message.name === "resume" && (item.status === "paused" || item.status === "failed")) { item.status = "active"; item.statusText = "Downloading"; }
                    if (message.name === "delete") model.items = model.items.filter(i => i.id !== item.id);
                }
                onHostMessage({ type: "state", state: { items: model.items } });
                return;
            }
            if (message.cmd === "action" && message.name === "toggleMonitoring") { model.monitoring = !model.monitoring; render(); return; }
            if (message.cmd === "addUrl") {
                const name = decodeURIComponent(message.url.split("/").pop().split("?")[0] || "download");
                model.items.push({ id: "m" + Date.now(), kind: "progress", name, size: 52428800, progress: 0, status: "active", statusText: "Downloading", speedText: "3.1 MB/s", etaText: "20s", host: new URL(message.url).hostname, dir: "Downloads", added: new Date().toISOString(), addedText: "Just now" });
                onHostMessage({ type: "state", state: { items: model.items } });
                return;
            }
            if (message.cmd === "action") toast("Preview mode", `"${message.name}" opens the real dialog inside the Axioos app.`);
        },
        updateButtons() {
            const sel = selectedItems();
            const prog = sel.length && sel.every(i => i.kind === "progress");
            const fin = sel.length === 1 && sel[0].kind === "finished";
            model.buttons = {
                pause: { enabled: !!prog && sel.some(i => i.status === "active"), visible: true },
                resume: { enabled: !!prog && sel.some(i => i.status !== "active"), visible: true },
                delete: { enabled: sel.length > 0, visible: true },
                open: { enabled: fin, visible: true },
                openFolder: { enabled: sel.length === 1, visible: true }
            };
        },
        start() {
            const now = Date.now();
            const at = min => new Date(now - min * 60000).toISOString();
            model.version = "Preview";
            model.monitoring = true;
            model.developer = { name: "Anas Al Hwaity", telegram: "@Anas12rm" };
            model.categories = [
                { id: "video", name: "Video", icon: "video", exts: EXT_KIND.video },
                { id: "music", name: "Music", icon: "music", exts: EXT_KIND.music },
                { id: "doc", name: "Documents", icon: "doc", exts: EXT_KIND.doc },
                { id: "zip", name: "Compressed", icon: "zip", exts: EXT_KIND.zip },
                { id: "app", name: "Programs", icon: "app", exts: EXT_KIND.app }
            ];
            model.items = [
                { id: "iso", kind: "progress", name: "ubuntu-24.04.1-desktop-amd64.iso", size: 6119661568, progress: 62, status: "active", statusText: "Downloading", speedText: "8.4 MB/s", etaText: "4m 12s", host: "releases.ubuntu.com", dir: "Downloads", added: at(1), addedText: "Today" },
                { id: "x", kind: "progress", name: "Rami - Look at this sunset over the port.mp4", size: 39845888, progress: 52, status: "active", statusText: "Downloading", speedText: "1.6 MB/s", etaText: "12s", host: "x.com", dir: "Downloads\\Video", added: at(2), addedText: "Today" },
                { id: "lec", kind: "progress", name: "Lecture 12 Distributed Consensus.mp4", size: 1287651328, progress: 37, status: "active", statusText: "Downloading", speedText: "5.9 MB/s", etaText: "2m 40s", host: "lectures.example.edu", dir: "Downloads\\Video", added: at(3), addedText: "Today" },
                { id: "bl", kind: "progress", name: "blender-4.2.3-windows-x64.msi", size: 364904448, progress: 40, status: "paused", statusText: "Paused", speedText: "", etaText: "", host: "download.blender.org", dir: "Downloads\\Programs", added: at(60), addedText: "Today" },
                { id: "node", kind: "progress", name: "node-v22.9.0-x64.msi", size: 32505856, progress: 0, status: "queued", statusText: "Waiting in queue", speedText: "", etaText: "", host: "nodejs.org", dir: "Downloads\\Programs", added: at(70), addedText: "Today" },
                { id: "bk", kind: "progress", name: "Photos-Backup-2026-09.zip", size: 2254857830, progress: 3, status: "failed", statusText: "Server answered 403", speedText: "", etaText: "", host: "drive.example.com", dir: "Downloads\\Compressed", added: at(90), addedText: "Today" },
                { id: "pod", kind: "finished", name: "Podcast episode 214.mp3", size: 88080384, progress: 100, status: "done", statusText: "Finished", host: "feeds.example.fm", dir: "Downloads\\Music", added: at(1500), addedText: "Yesterday" }
            ];
            this.updateButtons();
            render(true);
            clearInterval(this.timer);
            this.timer = setInterval(() => {
                for (const item of model.items) {
                    if (item.status !== "active") continue;
                    const base = parseSpeed(item.speedText) || 1048576;
                    const speed = base * (0.85 + Math.random() * 0.3);
                    item.speedText = speedText(speed);
                    item.progress = Math.min(100, item.progress + Math.max(1, Math.round(speed / Math.max(item.size, 1) * 100)));
                    if (item.progress >= 100) { item.kind = "finished"; item.status = "done"; item.statusText = "Finished"; item.addedText = "Just now"; }
                }
                onHostMessage({ type: "state", state: { items: model.items } });
            }, 1000);
        }
    };

    if (webview) webview.addEventListener("message", event => onHostMessage(event.data));
    globalThis.AxioosShell = Object.freeze({ receive: onHostMessage, state: () => ({ model, ui: { ...ui, sel: [...ui.sel] }, appearance }) });
    render(true);
    send({ cmd: "ready" });
})();
