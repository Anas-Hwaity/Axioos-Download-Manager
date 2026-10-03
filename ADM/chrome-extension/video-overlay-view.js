"use strict";

(() => {
    const HOST_ATTRIBUTE = "data-adm-video-overlay-host";
    const STYLE_TEXT = `
        :host {
            all: initial;
            --adm-bg: #0a1120;
            --adm-ink: #e8f1ff;
            --adm-mut: #8ea6c4;
            --adm-a1: #5ee7f7;
            --adm-a2: #7b8cff;
            --adm-on: #07101f;
        }
        * { box-sizing: border-box; }
        .adm-panel {
            --adm-cyan: var(--adm-a1);
            --adm-blue: var(--adm-a2);
            --adm-violet: var(--adm-a1);
            --adm-text: var(--adm-ink);
            --adm-muted: color-mix(in srgb, var(--adm-ink) 68%, transparent);
            position: relative;
            display: inline-flex;
            align-items: stretch;
            max-width: min(470px, calc(100vw - 20px));
            min-height: 40px;
            border: 1px solid color-mix(in srgb, var(--adm-a2) 34%, transparent);
            border-radius: 15px;
            background:
                radial-gradient(140% 180% at 0% 0%, color-mix(in srgb, var(--adm-a1) 20%, transparent), transparent 48%),
                radial-gradient(120% 180% at 100% 100%, color-mix(in srgb, var(--adm-a2) 18%, transparent), transparent 54%),
                linear-gradient(145deg, color-mix(in srgb, var(--adm-bg) 86%, transparent), color-mix(in srgb, var(--adm-bg) 90%, transparent));
            box-shadow:
                0 18px 50px rgba(0, 0, 0, .44),
                0 0 0 1px color-mix(in srgb, var(--adm-ink) 4.5%, transparent) inset,
                0 1px 0 color-mix(in srgb, var(--adm-ink) 16%, transparent) inset,
                0 0 24px color-mix(in srgb, var(--adm-a1) 10%, transparent);
            color: var(--adm-text);
            font: 600 12.5px/1.25 Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
            pointer-events: auto;
            -webkit-font-smoothing: antialiased;
            isolation: isolate;
            overflow: visible;
        }
        .adm-panel::before {
            content: "";
            position: absolute;
            inset: 0;
            border-radius: inherit;
            pointer-events: none;
            background: linear-gradient(110deg, color-mix(in srgb, var(--adm-ink) 16%, transparent), transparent 28%, transparent 68%, color-mix(in srgb, var(--adm-a2) 8%, transparent));
            opacity: .72;
            z-index: -1;
        }
        @supports (backdrop-filter: blur(1px)) {
            .adm-panel,
            .adm-menu,
            .adm-context-menu {
                backdrop-filter: blur(22px) saturate(1.45) contrast(1.05);
                -webkit-backdrop-filter: blur(22px) saturate(1.45) contrast(1.05);
            }
        }
        .adm-primary, .adm-drag, .adm-choice {
            border: 0;
            color: inherit;
            font: inherit;
        }
        .adm-primary {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            min-height: 40px;
            padding: 0 11px 0 11px;
            border-radius: 14px 0 0 14px;
            background: transparent;
            cursor: pointer;
            white-space: nowrap;
            transition: background .16s ease, transform .16s ease;
        }
        .adm-primary-label { letter-spacing: -.01em; }
        .adm-primary:hover, .adm-primary:focus-visible,
        .adm-drag:hover, .adm-drag:focus-visible,
        .adm-choice:hover, .adm-choice:focus-visible {
            background: linear-gradient(135deg, color-mix(in srgb, var(--adm-a1) 13%, transparent), color-mix(in srgb, var(--adm-a2) 11%, transparent));
            outline: none;
        }
        .adm-primary:active { transform: translateY(1px); }
        .adm-primary:focus-visible, .adm-drag:focus-visible, .adm-choice:focus-visible {
            box-shadow: inset 0 0 0 2px color-mix(in srgb, var(--adm-a1) 68%, transparent);
        }
        .adm-caret {
            margin-left: 2px;
            color: color-mix(in srgb, var(--adm-ink) 80%, transparent);
            font-size: 10px;
            transform: translateY(-1px);
            transition: transform .16s ease;
        }
        .adm-primary[aria-expanded="true"] .adm-caret { transform: rotate(180deg); }
        .adm-drag {
            width: 30px;
            min-height: 40px;
            border-left: 1px solid color-mix(in srgb, var(--adm-a2) 17%, transparent);
            border-radius: 0 14px 14px 0;
            background: color-mix(in srgb, var(--adm-ink) 1.5%, transparent);
            color: color-mix(in srgb, var(--adm-ink) 78%, transparent);
            cursor: move;
            touch-action: none;
            user-select: none;
            transition: background .16s ease, color .16s ease;
        }
        .adm-menu, .adm-context-menu {
            position: absolute;
            top: calc(100% + 9px);
            right: 0;
            width: max-content;
            min-width: min(320px, calc(100vw - 20px));
            max-width: min(470px, calc(100vw - 20px));
            max-height: min(360px, 58vh);
            overflow: auto;
            padding: 7px;
            border: 1px solid color-mix(in srgb, var(--adm-a1) 30%, transparent);
            border-radius: 15px;
            background:
                radial-gradient(140% 180% at 15% 0%, color-mix(in srgb, var(--adm-a2) 16%, transparent), transparent 50%),
                radial-gradient(120% 150% at 100% 100%, color-mix(in srgb, var(--adm-a1) 16%, transparent), transparent 55%),
                color-mix(in srgb, var(--adm-bg) 94%, transparent);
            box-shadow:
                0 24px 70px rgba(0, 0, 0, .52),
                inset 0 1px 0 color-mix(in srgb, var(--adm-ink) 12%, transparent),
                0 0 0 1px color-mix(in srgb, var(--adm-ink) 2.5%, transparent);
            scrollbar-color: color-mix(in srgb, var(--adm-a2) 45%, transparent) transparent;
            scrollbar-width: thin;
        }
        .adm-menu[hidden], .adm-context-menu[hidden], .adm-metadata-panel[hidden], .adm-panel[hidden], .adm-restore-dot[hidden] { display: none; }
        .adm-context-menu { min-width: 250px; z-index: 2; }
        .adm-context-label {
            padding: 8px 10px 5px;
            color: var(--adm-muted);
            font: 700 10px/1.2 Inter, ui-sans-serif, system-ui, sans-serif;
            text-transform: uppercase;
            letter-spacing: .10em;
        }
        .adm-separator {
            height: 1px;
            margin: 5px 7px;
            background: linear-gradient(90deg, transparent, color-mix(in srgb, var(--adm-a1) 26%, transparent), transparent);
        }
        .adm-restore-dot {
            width: 22px;
            height: 22px;
            padding: 0;
            border: 1px solid color-mix(in srgb, var(--adm-a2) 54%, transparent);
            border-radius: 50%;
            background:
                radial-gradient(circle at 35% 30%, var(--adm-ink) 0 8%, var(--adm-a1) 16% 34%, var(--adm-a2) 52%, var(--adm-a1) 100%);
            box-shadow: 0 6px 18px rgba(0,0,0,.44), 0 0 18px color-mix(in srgb, var(--adm-a2) 30%, transparent);
            cursor: pointer;
            pointer-events: auto;
            transition: transform .16s ease, box-shadow .16s ease;
        }
        .adm-restore-dot:hover, .adm-restore-dot:focus-visible {
            transform: scale(1.13);
            outline: none;
            box-shadow: 0 8px 22px rgba(0,0,0,.46), 0 0 22px color-mix(in srgb, var(--adm-a1) 48%, transparent);
        }
        .adm-choice {
            position: relative;
            display: block;
            width: 100%;
            min-height: 36px;
            padding: 9px 11px;
            margin: 2px 0;
            border: 1px solid transparent;
            border-radius: 10px;
            background: color-mix(in srgb, var(--adm-ink) 2.5%, transparent);
            color: var(--adm-ink);
            text-align: left;
            cursor: pointer;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            transition: transform .14s ease, background .14s ease, border-color .14s ease, box-shadow .14s ease;
        }
        .adm-choice:hover, .adm-choice:focus-visible {
            transform: translateX(1px);
            border-color: color-mix(in srgb, var(--adm-a2) 26%, transparent);
            background: linear-gradient(100deg, color-mix(in srgb, var(--adm-a1) 13%, transparent), color-mix(in srgb, var(--adm-a2) 10%, transparent));
            box-shadow: inset 0 1px 0 color-mix(in srgb, var(--adm-ink) 6%, transparent);
        }
        .adm-choice:disabled { cursor: default; }
        .adm-media-choice {
            white-space: normal;
            overflow: visible;
            line-height: 1.35;
            padding: 10px 12px;
        }
        .adm-media-title {
            display: block;
            color: var(--adm-ink);
            font-weight: 700;
            font-size: 13px;
            line-height: 1.35;
            overflow-wrap: anywhere;
        }
        .adm-media-meta {
            display: block;
            margin-top: 4px;
            color: color-mix(in srgb, var(--adm-ink) 76%, transparent);
            font-size: 11px;
            line-height: 1.3;
            overflow-wrap: anywhere;
        }
        .adm-metadata-panel {
            position: absolute;
            top: calc(100% + 9px);
            right: 0;
            z-index: 4;
            width: min(440px, calc(100vw - 20px));
            max-height: min(390px, 62vh);
            overflow: auto;
            padding: 12px;
            border: 1px solid color-mix(in srgb, var(--adm-a1) 34%, transparent);
            border-radius: 15px;
            background: color-mix(in srgb, var(--adm-bg) 97%, transparent);
            box-shadow: 0 24px 70px rgba(0, 0, 0, .58), inset 0 1px 0 color-mix(in srgb, var(--adm-ink) 10%, transparent);
            color: var(--adm-ink);
        }
        .adm-metadata-head {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 12px;
            margin-bottom: 9px;
        }
        .adm-metadata-title { font-weight: 800; font-size: 13px; }
        .adm-metadata-close {
            border: 1px solid color-mix(in srgb, var(--adm-a2) 24%, transparent);
            border-radius: 8px;
            background: color-mix(in srgb, var(--adm-ink) 5%, transparent);
            color: var(--adm-ink);
            cursor: pointer;
            width: 28px;
            height: 28px;
        }
        .adm-metadata-row {
            display: grid;
            grid-template-columns: 105px minmax(0, 1fr);
            gap: 10px;
            padding: 6px 2px;
            border-top: 1px solid color-mix(in srgb, var(--adm-a1) 10%, transparent);
        }
        .adm-metadata-key { color: color-mix(in srgb, var(--adm-a2) 74%, transparent); font-size: 11px; font-weight: 700; }
        .adm-metadata-value { color: var(--adm-ink); font-size: 11px; overflow-wrap: anywhere; user-select: text; }
        .adm-clear {
            margin-top: 6px;
            border-color: color-mix(in srgb, #f08a3c 34%, transparent);
            color: color-mix(in srgb, #f08a3c 28%, var(--adm-ink));
        }
        .adm-analysis-line {
            min-height: 46px;
            padding: 10px 11px 10px 35px;
            border-color: color-mix(in srgb, var(--adm-a1) 18%, transparent);
            background: linear-gradient(105deg, color-mix(in srgb, var(--adm-a2) 12%, transparent), color-mix(in srgb, var(--adm-a1) 10%, transparent));
            color: color-mix(in srgb, var(--adm-ink) 92%, transparent);
            white-space: normal;
            font-weight: 600;
            line-height: 1.35;
            opacity: 1;
        }
        .adm-analysis-line::before {
            content: "";
            position: absolute;
            left: 13px;
            top: 15px;
            width: 10px;
            height: 10px;
            border-radius: 50%;
            background: var(--adm-a2);
            box-shadow: 0 0 0 4px color-mix(in srgb, var(--adm-a1) 10%, transparent), 0 0 12px color-mix(in srgb, var(--adm-a2) 50%, transparent);
        }
        .adm-analysis-line.queued::before,
        .adm-analysis-line.running::before {
            animation: admPulse 1.15s ease-in-out infinite;
        }
        .adm-analysis-line.ready {
            border-color: rgba(91, 232, 174, .24);
            background: linear-gradient(105deg, rgba(41, 170, 120, .13), rgba(92, 184, 50, .10));
        }
        .adm-analysis-line.ready::before { background: #62e6ad; box-shadow: 0 0 12px rgba(76,226,164,.48); }
        .adm-analysis-line.failed {
            border-color: rgba(255, 112, 141, .26);
            background: linear-gradient(105deg, rgba(181, 50, 82, .14), rgba(105, 45, 115, .10));
        }
        .adm-analysis-line.failed::before { background: #ff7798; box-shadow: 0 0 12px rgba(255,96,133,.44); }
        .adm-analysis-line.cancelled::before { background: #a9b5c4; box-shadow: none; }
        .adm-analyze {
            margin-top: 6px;
            border-color: color-mix(in srgb, var(--adm-a1) 24%, transparent);
            background: linear-gradient(100deg, color-mix(in srgb, var(--adm-a2) 16%, transparent), color-mix(in srgb, var(--adm-a1) 15%, transparent));
            color: var(--adm-ink);
            font-weight: 700;
        }
        .adm-analyze:hover {
            border-color: color-mix(in srgb, var(--adm-a2) 42%, transparent);
            background: linear-gradient(100deg, color-mix(in srgb, var(--adm-a1) 24%, transparent), color-mix(in srgb, var(--adm-a2) 22%, transparent));
            box-shadow: 0 0 18px color-mix(in srgb, var(--adm-a1) 10%, transparent);
        }
        @keyframes admPulse {
            0%, 100% { transform: scale(.80); opacity: .58; }
            50% { transform: scale(1.16); opacity: 1; }
        }
        @media (prefers-reduced-motion: reduce) {
            *, *::before, *::after { animation: none !important; transition: none !important; scroll-behavior: auto !important; }
        }
    `;

    let currentAppearance = null;

    function applyTheme(overlay, appearance) {
        if (appearance) currentAppearance = appearance;
        if (!overlay?.host || !currentAppearance) return;
        globalThis.AdmExtensionTheme?.apply(overlay.host, currentAppearance);
    }

    function create(anchor, callbacks = {}) {
        const host = document.createElement("div");
        host.setAttribute(HOST_ATTRIBUTE, "");
        host.style.position = "fixed";
        host.style.zIndex = "2147483647";
        host.style.pointerEvents = "none";
        host.style.contain = "layout style";

        const shadow = host.attachShadow({ mode: "open" });
        const style = document.createElement("style");
        style.textContent = STYLE_TEXT;

        const panel = document.createElement("div");
        panel.className = "adm-panel";

        const primary = document.createElement("button");
        primary.type = "button";
        primary.className = "adm-primary";
        primary.setAttribute("aria-haspopup", "menu");
        primary.setAttribute("aria-expanded", "false");
        primary.setAttribute("aria-label", "Download this video. Choose a detected media stream.");
        const primaryLabel = document.createElement("span");
        primaryLabel.className = "adm-primary-label";
        primaryLabel.textContent = "Download this video";
        primary.append(primaryLabel);
        const caret = document.createElement("span");
        caret.className = "adm-caret";
        caret.setAttribute("aria-hidden", "true");
        caret.textContent = "▾";
        primary.append(caret);

        const dragHandle = document.createElement("button");
        dragHandle.type = "button";
        dragHandle.className = "adm-drag";
        dragHandle.setAttribute("aria-label", "Move Axioos video control or open hide options");
        dragHandle.title = "Drag anywhere • click for hide options";
        dragHandle.textContent = "↔";

        const menu = document.createElement("div");
        menu.className = "adm-menu";
        menu.setAttribute("role", "menu");
        menu.hidden = true;

        const contextMenu = document.createElement("div");
        contextMenu.className = "adm-context-menu";
        contextMenu.setAttribute("role", "menu");
        contextMenu.hidden = true;

        const restoreDot = document.createElement("button");
        restoreDot.type = "button";
        restoreDot.className = "adm-restore-dot";
        restoreDot.setAttribute("aria-label", "Show Axioos video control");
        restoreDot.title = "Show Axioos video control";
        restoreDot.hidden = true;

        const metadataPanel = document.createElement("div");
        metadataPanel.className = "adm-metadata-panel";
        metadataPanel.setAttribute("role", "dialog");
        metadataPanel.setAttribute("aria-label", "Detected media metadata");
        metadataPanel.hidden = true;

        panel.append(primary, dragHandle, menu, contextMenu, metadataPanel);
        shadow.append(style, panel, restoreDot);
        (document.documentElement || document.body).appendChild(host);

        const overlay = { host, panel, primary, primaryLabel, dragHandle, menu, contextMenu, metadataPanel, restoreDot, anchor, collapsed: false };
        applyTheme(overlay, currentAppearance);
        primary.addEventListener("click", event => callbacks.onPrimaryClick?.(overlay, event));
        dragHandle.addEventListener("pointerdown", event => callbacks.onDragStart?.(overlay, event));
        dragHandle.addEventListener("click", event => callbacks.onMoveMenuClick?.(overlay, event));
        dragHandle.addEventListener("keydown", event => callbacks.onDragKey?.(overlay, event));
        panel.addEventListener("contextmenu", event => callbacks.onContextMenu?.(overlay, event));
        panel.addEventListener("keydown", event => callbacks.onKeyDown?.(overlay, event));
        restoreDot.addEventListener("click", event => callbacks.onRestore?.(overlay, event));
        return overlay;
    }

    function setPrimaryStatus(overlay, state, label) {
        if (!overlay) return;
        if (overlay.primaryLabel) overlay.primaryLabel.textContent = label || "Download this video";
        const normalized = typeof state === "string" ? state : "";
        if (normalized) overlay.panel.dataset.analysisState = normalized;
        else delete overlay.panel.dataset.analysisState;
    }

    function formatDuration(seconds) {
        const value = Number(seconds);
        if (!Number.isFinite(value) || value <= 0) return "";
        const total = Math.round(value);
        const hours = Math.floor(total / 3600);
        const minutes = Math.floor((total % 3600) / 60);
        const secs = total % 60;
        return hours > 0 ? `${hours}:${String(minutes).padStart(2, "0")}:${String(secs).padStart(2, "0")}`
            : `${minutes}:${String(secs).padStart(2, "0")}`;
    }

    function mediaLabel(item, index) {
        const label = typeof item?.label === "string" ? item.label.trim() : "";
        const duration = formatDuration(item?.durationSeconds);
        const base = label || `Detected media ${index + 1}`;
        return duration ? `${base}  •  ${duration}` : base;
    }

    function mediaGroups(items) {
        const valid = items.filter(item => item && typeof item.id === "string" && item.id.length > 0);
        return [
            ["Found by yt-dlp", valid.filter(item => String(item.source || "") === "ytdlp")],
            ["Detected in the browser", valid.filter(item => String(item.source || "") !== "ytdlp")]
        ].filter(group => group[1].length > 0);
    }

    function renderMenu(overlay, items, onSelect, onContext = null) {
        overlay.menu.replaceChildren();
        let index = 0;
        for (const [heading, groupItems] of mediaGroups(items)) {
            const label = document.createElement("div");
            label.className = "adm-context-label adm-source-label";
            label.setAttribute("role", "presentation");
            label.textContent = `${heading} (${groupItems.length})`;
            overlay.menu.appendChild(label);
            for (const item of groupItems) renderMenuChoice(overlay, item, index++, onSelect, onContext);
        }
    }

    function renderMenuChoice(overlay, item, index, onSelect, onContext) {
        const choice = document.createElement("button");
        choice.type = "button";
        choice.className = "adm-choice adm-media-choice";
        choice.setAttribute("role", "menuitem");
        const title = document.createElement("span");
        title.className = "adm-media-title";
        title.textContent = typeof item?.label === "string" && item.label.trim() ? item.label.trim() : `Detected media ${index + 1}`;
        const meta = document.createElement("span");
        meta.className = "adm-media-meta";
        const bits = [];
        const duration = formatDuration(item?.durationSeconds);
        if (duration) bits.push(duration);
        if (item?.detail) bits.push(String(item.detail));
        meta.textContent = bits.join(" • ") || "Click to download • Right-click for metadata";
        choice.append(title, meta);
        choice.title = mediaLabel(item, index);
        choice.addEventListener("click", event => onSelect(item, event));
        if (typeof onContext === "function") {
            choice.addEventListener("contextmenu", event => {
                event.preventDefault();
                event.stopPropagation();
                onContext(item, event);
            });
        }
        overlay.menu.appendChild(choice);
    }

    function setMenuOpen(overlay, open, focusFirst = false) {
        overlay.menu.hidden = !open;
        overlay.primary.setAttribute("aria-expanded", open ? "true" : "false");
        if (open && focusFirst) {
            overlay.menu.querySelector("button")?.focus({ preventScroll: true });
        }
    }

    function closeMenu(overlay) {
        if (overlay) setMenuOpen(overlay, false);
    }

    function renderContextMenu(overlay, items, onSelect) {
        overlay.contextMenu.replaceChildren();
        for (const item of items) {
            if (item?.type === "separator") {
                const separator = document.createElement("div");
                separator.className = "adm-separator";
                separator.setAttribute("role", "separator");
                overlay.contextMenu.appendChild(separator);
                continue;
            }
            if (item?.type === "label") {
                const label = document.createElement("div");
                label.className = "adm-context-label";
                label.textContent = String(item.label || "");
                overlay.contextMenu.appendChild(label);
                continue;
            }
            if (!item || typeof item.action !== "string") continue;
            const choice = document.createElement("button");
            choice.type = "button";
            choice.className = "adm-choice";
            choice.setAttribute("role", "menuitem");
            choice.textContent = String(item.label || item.action);
            choice.addEventListener("click", event => onSelect(item.action, event));
            overlay.contextMenu.appendChild(choice);
        }
    }

    function setContextMenuOpen(overlay, open, focusFirst = false) {
        overlay.contextMenu.hidden = !open;
        if (open && focusFirst) overlay.contextMenu.querySelector("button")?.focus({ preventScroll: true });
    }

    function closeContextMenu(overlay) {
        if (overlay) setContextMenuOpen(overlay, false);
    }

    function renderMetadataPanel(overlay, title, rows, onClose) {
        if (!overlay?.metadataPanel) return;
        const panel = overlay.metadataPanel;
        panel.replaceChildren();
        const head = document.createElement("div");
        head.className = "adm-metadata-head";
        const heading = document.createElement("div");
        heading.className = "adm-metadata-title";
        heading.textContent = title || "Media metadata";
        const close = document.createElement("button");
        close.type = "button";
        close.className = "adm-metadata-close";
        close.setAttribute("aria-label", "Close metadata");
        close.textContent = "×";
        close.addEventListener("click", event => { event.preventDefault(); event.stopPropagation(); closeMetadataPanel(overlay); onClose?.(); });
        head.append(heading, close);
        panel.appendChild(head);
        for (const row of rows || []) {
            const line = document.createElement("div");
            line.className = "adm-metadata-row";
            const key = document.createElement("div");
            key.className = "adm-metadata-key";
            key.textContent = String(row?.label || "");
            const value = document.createElement("div");
            value.className = "adm-metadata-value";
            value.textContent = String(row?.value || "Not available");
            line.append(key, value);
            panel.appendChild(line);
        }
        panel.hidden = false;
        close.focus({ preventScroll: true });
    }

    function closeMetadataPanel(overlay) {
        if (overlay?.metadataPanel) overlay.metadataPanel.hidden = true;
    }

    function setCollapsed(overlay, collapsed) {
        if (!overlay) return;
        overlay.collapsed = collapsed === true;
        overlay.panel.hidden = overlay.collapsed;
        overlay.restoreDot.hidden = !overlay.collapsed;
        if (overlay.collapsed) {
            closeMenu(overlay);
            closeContextMenu(overlay);
            closeMetadataPanel(overlay);
        }
    }

    function destroy(overlay) {
        overlay?.host?.remove();
    }

    function isHostInPath(path) {
        return path.some(node => node && typeof node.hasAttribute === "function" && node.hasAttribute(HOST_ATTRIBUTE));
    }

    globalThis.AdmVideoOverlayView = Object.freeze({
        create,
        renderMenu,
        setPrimaryStatus,
        setMenuOpen,
        closeMenu,
        renderContextMenu,
        setContextMenuOpen,
        closeContextMenu,
        renderMetadataPanel,
        closeMetadataPanel,
        setCollapsed,
        destroy,
        isHostInPath,
        applyTheme,
    });
})();
