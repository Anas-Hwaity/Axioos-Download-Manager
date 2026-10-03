"use strict";

(() => {
    const core = globalThis.AdmVideoOverlayCore;
    const view = globalThis.AdmVideoOverlayView;
    if (!core || !view || !globalThis.chrome?.runtime) return;

    const POSITION_KEY = "adm.videoOverlay.horizontalRatio.v1";
    const POSITION_2D_KEY = "adm.videoOverlay.position2d.v1";
    const SUPPRESSION_KEY = "adm.videoOverlay.suppression.v1";
    const VISIT_SUPPRESSION_KEY = "adm.videoOverlay.visitSuppression.v1";
    const FALLBACK_TOP = 72;
    const VIDEO_TOP_INSET = 8;
    const VIEWPORT_MARGIN = 8;

    let mediaList = [];
    let analysisAllowed = false;
    let serverAnalyses = new Map();
    const localAnalyses = new Map();
    const LOST_ANALYSIS_MS = 45000;
    const SILENT_SERVER_MS = 30000;
    let selectedVideo = null;
    let overlayPosition = { xRatio: 1, yRatio: 0, detached: false };
    let refreshQueued = false;
    let fallbackOverlay = null;
    let suppressionOverlay = null;
    let suppressionEntries = Object.create(null);
    let currentSiteKey = core.siteSuppressionKey(location.href);
    let visitSuppressionSites = loadVisitSuppressionSites();
    let suppressionExpiryTimer = null;
    let suppressionLoaded = !chrome.storage?.local?.get;
    let collapsedAnchors = new WeakSet();
    let fallbackCollapsed = false;
    const videoOverlays = new Map();


    function isSocialMediaPage() {
        return !!globalThis.AdmSocialSitePolicy?.isSupported(location.href, "externalAnalysis");
    }

    function isRunningState(item) {
        return !!item && (item.state === "queued" || item.state === "running");
    }

    function stamp(item) {
        const value = Date.parse(item?.updatedAt || item?.startedAt || "");
        return Number.isFinite(value) ? value : 0;
    }

    function analysisFor(url) {
        const key = String(url || "");
        const server = serverAnalyses.get(key) || null;
        const local = localAnalyses.get(key) || null;
        if (server && (!local || local.accepted === true || stamp(server) >= stamp(local))) {
            if (local && local.accepted === true) localAnalyses.delete(key);
            if (isRunningState(server) && stamp(server) > 0 && Date.now() - stamp(server) > SILENT_SERVER_MS) {
                return { ...server, state: "failed", code: "AnalysisLost",
                    message: "Axioos stopped reporting progress for this video. Make sure the app is still open, then try again." };
            }
            return server;
        }
        if (local && local.accepted === true && isRunningState(local) && Date.now() - (local.acceptedAt || 0) > LOST_ANALYSIS_MS) {
            const lost = { ...local, state: "failed", code: "AnalysisLost",
                message: "Axioos stopped reporting progress for this video. Try again.", updatedAt: new Date().toISOString() };
            localAnalyses.set(key, lost);
            return lost;
        }
        if (local && local.accepted !== true && isRunningState(local) && Date.now() - stamp(local) > PENDING_ANALYSIS_MS + 5000) {
            const silent = { ...local, state: "failed", code: "NoAnswer",
                message: extensionAlive() ? "Axioos did not answer. Make sure the app is running, then try again." : EXTENSION_RELOADED,
                updatedAt: new Date().toISOString() };
            localAnalyses.set(key, silent);
            return silent;
        }
        return local;
    }

    function setLocalAnalysis(url, patch) {
        const key = String(url || "");
        const previous = localAnalyses.get(key) || serverAnalyses.get(key) || { url: key, startedAt: new Date().toISOString() };
        const next = { ...previous, ...patch, url: key, updatedAt: new Date().toISOString() };
        localAnalyses.set(key, next);
        return next;
    }

    function isAnalysisRunning() {
        for (const key of new Set([...serverAnalyses.keys(), ...localAnalyses.keys()])) {
            if (isRunningState(analysisFor(key))) return true;
        }
        return false;
    }

    function analysisElapsedSeconds(item) {
        if (!item?.startedAt) return 0;
        const start = Date.parse(item.startedAt);
        const end = isRunningState(item) ? Date.now() : Date.parse(item.updatedAt || item.startedAt);
        return Number.isFinite(start) && Number.isFinite(end) ? Math.max(0, Math.floor((end - start) / 1000)) : 0;
    }

    function formatElapsed(seconds) {
        const s = Math.max(0, Math.floor(Number(seconds) || 0));
        const m = Math.floor(s / 60);
        return m > 0 ? `${m}:${String(s % 60).padStart(2, "0")}` : `${s}s`;
    }

    function consumeRuntimeError() {
        void chrome.runtime?.lastError;
    }

    const EXTENSION_RELOADED = "The Axioos extension was updated or restarted. Reload this page, then try again.";
    const PENDING_ANALYSIS_MS = 45000;

    function extensionAlive() {
        try {
            return !!chrome.runtime?.id;
        } catch {
            return false;
        }
    }

    function safeSend(message, callback, timeoutMs) {
        let done = false;
        const finish = (response, error) => {
            if (done) return;
            done = true;
            if (timer) clearTimeout(timer);
            if (typeof callback === "function") callback(response, error);
        };
        const timer = timeoutMs > 0 ? setTimeout(() => finish(undefined, "The Axioos extension did not answer in time. Try again."), timeoutMs) : 0;
        if (!extensionAlive()) {
            finish(undefined, EXTENSION_RELOADED);
            return;
        }
        try {
            chrome.runtime.sendMessage(message, response => {
                const error = chrome.runtime?.lastError;
                finish(error ? undefined : response, error ? (error.message || "Could not contact the Axioos extension background service.") : null);
            });
        } catch (error) {
            finish(undefined, /context invalidated/i.test(String(error?.message || error)) ? EXTENSION_RELOADED : String(error?.message || error));
        }
    }

    function closeAllMenus(except = null) {
        for (const overlay of videoOverlays.values()) {
            if (overlay !== except) {
                view.closeMenu(overlay);
                view.closeContextMenu(overlay);
            }
        }
        if (fallbackOverlay && fallbackOverlay !== except) {
            view.closeMenu(fallbackOverlay);
            view.closeContextMenu(fallbackOverlay);
        }
    }

    function persistSuppressionEntries() {
        if (!chrome.storage?.local?.set) return;
        chrome.storage.local.set({ [SUPPRESSION_KEY]: { ...suppressionEntries } }, consumeRuntimeError);
    }

    function loadVisitSuppressionSites() {
        try {
            const parsed = JSON.parse(sessionStorage.getItem(VISIT_SUPPRESSION_KEY) || "[]");
            return new Set(Array.isArray(parsed) ? parsed.filter(item => typeof item === "string" && item) : []);
        } catch {
            return new Set();
        }
    }

    function persistVisitSuppressionSites() {
        try {
            sessionStorage.setItem(VISIT_SUPPRESSION_KEY, JSON.stringify([...visitSuppressionSites]));
        } catch { }
    }

    function scheduleSuppressionExpiry(expiresAt) {
        if (suppressionExpiryTimer !== null) clearTimeout(suppressionExpiryTimer);
        suppressionExpiryTimer = null;
        const remaining = Number(expiresAt) - Date.now();
        if (!Number.isFinite(remaining)) return;
        const delay = Math.max(0, Math.min(remaining, 2147000000));
        suppressionExpiryTimer = setTimeout(() => {
            suppressionExpiryTimer = null;
            scheduleRefresh();
        }, delay);
    }

    function syncSiteIdentity() {
        const next = core.siteSuppressionKey(location.href);
        if (next === currentSiteKey) return;
        currentSiteKey = next;
        collapsedAnchors = new WeakSet();
        fallbackCollapsed = false;
        if (suppressionExpiryTimer !== null) clearTimeout(suppressionExpiryTimer);
        suppressionExpiryTimer = null;
        view.destroy(suppressionOverlay);
        suppressionOverlay = null;
    }

    function persistedSuppressionState() {
        const entry = suppressionEntries[currentSiteKey];
        const state = core.resolveSuppressionEntry(entry, Date.now());
        if (state.expired) {
            delete suppressionEntries[currentSiteKey];
            persistSuppressionEntries();
            return { active: false, expired: true };
        }
        if (state.active && state.mode === "temporary") scheduleSuppressionExpiry(entry.expiresAt);
        return state;
    }

    function isSiteSuppressed() {
        syncSiteIdentity();
        if (visitSuppressionSites.has(currentSiteKey)) return true;
        return persistedSuppressionState().active === true;
    }

    function setSiteSuppression(mode, durationMs = null) {
        syncSiteIdentity();
        const now = Date.now();
        if (mode === "visit") {
            visitSuppressionSites.add(currentSiteKey);
            persistVisitSuppressionSites();
        } else if (mode === "temporary") {
            const expiresAt = now + Number(durationMs);
            suppressionEntries[currentSiteKey] = { mode, expiresAt, updatedAt: now };
            persistSuppressionEntries();
            scheduleSuppressionExpiry(expiresAt);
        } else if (mode === "permanent") {
            suppressionEntries[currentSiteKey] = { mode, updatedAt: now };
            persistSuppressionEntries();
        }
        closeAllMenus();
        scheduleRefresh();
    }

    function clearSiteSuppression() {
        syncSiteIdentity();
        visitSuppressionSites.delete(currentSiteKey);
        persistVisitSuppressionSites();
        if (Object.prototype.hasOwnProperty.call(suppressionEntries, currentSiteKey)) {
            delete suppressionEntries[currentSiteKey];
            persistSuppressionEntries();
        }
        if (suppressionExpiryTimer !== null) clearTimeout(suppressionExpiryTimer);
        suppressionExpiryTimer = null;
        view.destroy(suppressionOverlay);
        suppressionOverlay = null;
        scheduleRefresh();
    }

    function selectMedia(overlay, item, event) {
        event.preventDefault();
        event.stopPropagation();
        view.setPrimaryStatus?.(overlay, "running", "Sending selected video to Axioos…");
        safeSend({ type: "vid", itemId: item.id }, (response, error) => {
            if (error) {
                view.setPrimaryStatus?.(overlay, "failed", error);
                return;
            }
            if (!response?.accepted) {
                view.setPrimaryStatus?.(overlay, "failed", response?.reason || "Axioos did not accept the selected video.");
                return;
            }
            view.setPrimaryStatus?.(overlay, "ready", "Sent to Axioos");
            view.closeMenu(overlay);
            setTimeout(() => scheduleRefresh(), 900);
        }, 60000);
    }

    function humanSize(bytes) {
        const value = Number(bytes);
        if (!Number.isFinite(value) || value < 0) return "Not available";
        if (value < 1024) return `${value} B`;
        const units = ["KB", "MB", "GB", "TB"];
        let size = value / 1024;
        let unit = units[0];
        for (let i = 1; i < units.length && size >= 1024; i++) { size /= 1024; unit = units[i]; }
        return `${size >= 100 ? size.toFixed(0) : size.toFixed(1)} ${unit}`;
    }

    function durationText(seconds) {
        const value = Number(seconds);
        if (!Number.isFinite(value) || value <= 0) return "Not available";
        const total = Math.round(value);
        const h = Math.floor(total / 3600);
        const m = Math.floor((total % 3600) / 60);
        const s = total % 60;
        return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`
            : `${m}:${String(s).padStart(2, "0")}`;
    }

    function metadataRows(metadata, fallbackDuration) {
        const width = Number(metadata?.width);
        const height = Number(metadata?.height);
        const dimensions = width > 0 && height > 0 ? `${width} × ${height}` : "Not available";
        const aspect = metadata?.aspectRatio || (width > 0 && height > 0 ? `${(width / height).toFixed(3)}:1` : "Not available");
        const duration = Number(metadata?.durationSeconds) > 0 ? metadata.durationSeconds : fallbackDuration;
        const explicitBitrate = Number(metadata?.bitrateKbps);
        const sizeBytes = Number(metadata?.sizeBytes);
        const derivedBitrate = !(explicitBitrate > 0) && sizeBytes > 0 && Number(duration) > 0
            ? Math.round((sizeBytes * 8) / Number(duration) / 1000) : null;
        const bitrate = explicitBitrate > 0 ? explicitBitrate : derivedBitrate;
        return [
            { label: "Name", value: metadata?.name || "Not available" },
            { label: "Duration", value: durationText(duration) },
            { label: "Size", value: humanSize(metadata?.sizeBytes) },
            { label: "Dimensions", value: dimensions },
            { label: "Aspect ratio", value: aspect || "Not available" },
            { label: "Bitrate", value: bitrate > 0 ? `${bitrate} kbps${explicitBitrate > 0 ? "" : " (derived)"}` : "Not available" },
            { label: "MIME / container", value: metadata?.mimeType || "Not available" },
            { label: "Quality / details", value: metadata?.detail || "Not available" },
            { label: "Media link", value: metadata?.link || "Not available" },
            { label: "Page link", value: metadata?.pageLink || location.href },
            { label: "Detected by", value: metadata?.detectedBy || "Not available" }
        ];
    }

    function looksOpaqueMediaName(value) {
        const text = String(value || "").trim();
        if (!text) return true;
        const base = text.replace(/\.[a-z0-9]{2,6}$/i, "");
        return base.length >= 12 && !/\s/.test(base) && /^[a-z0-9_-]+$/i.test(base);
    }

    function recognizablePageTitle() {
        const identity = globalThis.AdmMediaIdentity;
        if (identity) return identity.conciseTitle(document.title);
        const title = String(document.title || "").trim();
        return title.slice(0, 72);
    }

    function analysisTargetUrl(overlay) {
        const identity = globalThis.AdmMediaIdentity;
        if (!identity) return location.href;
        const article = overlay?.anchor?.closest?.("article");
        const candidates = [];
        if (article) {
            for (const time of article.querySelectorAll('a[href*="/status/"] time')) {
                const link = time.closest("a");
                if (link?.href) candidates.push(link.href);
            }
            for (const link of article.querySelectorAll('a[href*="/status/"]')) {
                if (link.href) candidates.push(link.href);
            }
        }
        return identity.socialAnalysisUrl(location.href, candidates) || location.href;
    }

    function showMediaMetadata(overlay, item, event) {
        event?.preventDefault();
        event?.stopPropagation();
        const fallbackDuration = Number(item?.durationSeconds) > 0 ? Number(item.durationSeconds) : Number(overlay?.anchor?.duration);
        safeSend({ type: "media-metadata", itemId: item.id }, (response, error) => {
            if (error) {
                view.renderMetadataPanel?.(overlay, "Video metadata", [{ label: "Status", value: error }]);
                return;
            }
            if (!response?.accepted) {
                view.renderMetadataPanel?.(overlay, "Video metadata", [
                    { label: "Status", value: response?.reason || "Metadata is no longer available for this detected media." }
                ]);
                return;
            }
            const anchorWidth = Number(overlay?.anchor?.videoWidth);
            const anchorHeight = Number(overlay?.anchor?.videoHeight);
            const metadata = {
                ...response.metadata,
                detail: response.metadata?.detail || item?.detail || "",
                durationSeconds: Number(response.metadata?.durationSeconds) > 0 ? response.metadata.durationSeconds : fallbackDuration,
                width: Number(response.metadata?.width) > 0 ? response.metadata.width : (anchorWidth > 0 ? anchorWidth : null),
                height: Number(response.metadata?.height) > 0 ? response.metadata.height : (anchorHeight > 0 ? anchorHeight : null)
            };
            view.renderMetadataPanel?.(overlay, "Video metadata", metadataRows(metadata, fallbackDuration));
        }, 15000);
    }

    function showMediaContextMenu(overlay, item, event) {
        event?.preventDefault();
        event?.stopPropagation();
        closeAllMenus(overlay);
        view.closeMetadataPanel?.(overlay);
        view.renderContextMenu(overlay, [{ action: "view-metadata", label: "View metadata" }], (action, actionEvent) => {
            if (action !== "view-metadata") return;
            view.closeContextMenu(overlay);
            showMediaMetadata(overlay, item, actionEvent);
        });
        view.setContextMenuOpen(overlay, true, true);
    }

    function analysisStatusText(analysis, running) {
        const elapsed = formatElapsed(analysisElapsedSeconds(analysis));
        const title = analysis.state === "failed" ? "Analysis failed"
            : analysis.state === "ready" ? "Analysis complete"
            : analysis.state === "cancelled" ? "Analysis cancelled"
            : analysis.state === "queued" ? "Starting yt-dlp…"
            : "Analyzing with yt-dlp…";
        const message = String(analysis.message || "").trim().replace(/…\s*\d+s$/, "").trim();
        return `${title}${running ? ` (${elapsed})` : ""}${message ? `: ${message}` : ""}`;
    }

    function renderMenu(overlay) {
        const targetUrl = analysisTargetUrl(overlay);
        overlay.targetUrl = targetUrl;
        const analysis = analysisFor(targetUrl);
        const running = isRunningState(analysis);
        const primaryLabel = running ? "Analyzing with yt-dlp…"
            : analysis?.state === "ready" ? "Video options ready"
            : analysis?.state === "failed" ? "Analysis failed"
            : analysis?.state === "cancelled" ? "Analysis cancelled"
            : "Download this video";
        view.setPrimaryStatus?.(overlay, analysis?.state || "", primaryLabel);
        const anchorDuration = Number(overlay?.anchor?.duration);
        const pageTitle = recognizablePageTitle();
        const displayItems = mediaList.map((item, index) => {
            const currentLabel = String(item?.label || "").trim();
            const label = globalThis.AdmMediaIdentity?.displayName
                ? globalThis.AdmMediaIdentity.displayName(currentLabel, pageTitle, item?.detail, index)
                : (looksOpaqueMediaName(currentLabel) && pageTitle ? pageTitle : (currentLabel || `Detected media ${index + 1}`));
            return {
                ...item,
                label,
                durationSeconds: Number.isFinite(Number(item?.durationSeconds)) && Number(item.durationSeconds) > 0
                    ? Number(item.durationSeconds)
                    : (Number.isFinite(anchorDuration) && anchorDuration > 0 ? anchorDuration : null)
            };
        });
        const signature = JSON.stringify([
            displayItems.map(item => [item.id, item.label, item.detail, item.source, item.durationSeconds]),
            analysis?.state || "", analysis?.code || "", running, isSocialMediaPage(), targetUrl
        ]);
        const statusText = analysis ? analysisStatusText(analysis, running) : "";
        if (overlay.menuSignature === signature && overlay.menu.childElementCount > 0 &&
            (!analysis || (overlay.statusLine && overlay.menu.contains(overlay.statusLine)))) {
            if (analysis && overlay.statusLine.textContent !== statusText) overlay.statusLine.textContent = statusText;
            return;
        }
        overlay.menuSignature = signature;
        overlay.statusLine = null;
        view.renderMenu(overlay, displayItems,
            (item, event) => selectMedia(overlay, item, event),
            (item, event) => showMediaContextMenu(overlay, item, event));
        if (analysis) {
            const status = document.createElement("button");
            status.type = "button";
            status.disabled = true;
            status.className = `adm-choice adm-analysis-line ${analysis.state || ""}`;
            status.setAttribute("role", "menuitem");
            status.textContent = statusText;
            overlay.statusLine = status;
            overlay.menu.appendChild(status);
        }
        if (mediaList.length > 0) {
            const clear = document.createElement("button");
            clear.type = "button";
            clear.className = "adm-choice adm-clear";
            clear.setAttribute("role", "menuitem");
            clear.textContent = "Clear detected media";
            clear.addEventListener("click", event => {
                event.preventDefault();
                event.stopPropagation();
                mediaList = [];
                localAnalyses.delete(targetUrl);
                view.closeContextMenu?.(overlay);
                view.closeMetadataPanel?.(overlay);
                renderMenu(overlay);
                safeSend({ type: "clear-detected-media" }, (response, error) => {
                    if (error) { view.setPrimaryStatus?.(overlay, "failed", error); return; }
                    if (!response?.accepted) view.setPrimaryStatus?.(overlay, "failed", response?.reason || "Could not clear detected media.");
                    scheduleRefresh();
                }, 15000);
            });
            overlay.menu.appendChild(clear);
        }
        if (isSocialMediaPage()) {
            const analyze = document.createElement("button");
            analyze.type = "button";
            analyze.className = "adm-choice adm-analyze";
            analyze.setAttribute("role", "menuitem");
            analyze.disabled = false;
            analyze.textContent = running ? "Cancel yt-dlp analysis"
                : analysis?.state === "failed" && analysis?.code === "AuthenticationRequired" ? "Retry using browser session"
                : analysis?.state === "failed" ? "Retry with yt-dlp" : "Analyze this video with yt-dlp";
            analyze.addEventListener("click", event => {
                event.preventDefault();
                event.stopPropagation();
                const current = analysisFor(targetUrl);
                if (isRunningState(current)) {
                    setLocalAnalysis(targetUrl, { ...current, message: "Cancelling yt-dlp analysis…", accepted: false });
                    renderMenu(overlay);
                    safeSend({ type: "cancel-page-video-analysis", url: targetUrl }, (response, error) => {
                        if (error) {
                            setLocalAnalysis(targetUrl, { state: "cancelled", accepted: false, message: "Analysis stopped. " + error });
                        } else if (response?.accepted) {
                            setLocalAnalysis(targetUrl, { state: "cancelled", message: "Analysis cancelled by user." });
                        } else {
                            setLocalAnalysis(targetUrl, { state: "failed", message: response?.reason || "Axioos did not accept the cancellation request." });
                        }
                        scheduleRefresh();
                    });
                    scheduleRefresh();
                    return;
                }
                const useBrowserSessionAssistance = current?.state === "failed" && current?.code === "AuthenticationRequired";
                serverAnalyses.delete(targetUrl);
                localAnalyses.set(targetUrl, {
                    state: "queued", message: "Launching or reconnecting to Axioos…", url: targetUrl, accepted: false,
                    startedAt: new Date().toISOString(), updatedAt: new Date().toISOString()
                });
                renderMenu(overlay);
                scheduleAnalysisTick();
                safeSend({
                    type: "analyze-page-video", url: targetUrl, userAgent: navigator.userAgent,
                    browserSessionAssistance: useBrowserSessionAssistance
                }, (response, error) => {
                    if (error) {
                        setLocalAnalysis(targetUrl, { state: "failed", accepted: false, message: error });
                    } else if (!response?.accepted) {
                        setLocalAnalysis(targetUrl, { state: "failed", accepted: false, message: response?.reason || "Axioos did not accept this analysis request." });
                    } else {
                        setLocalAnalysis(targetUrl, { state: "queued", accepted: true, acceptedAt: Date.now(), message: "Accepted by Axioos. Waiting for yt-dlp…" });
                    }
                    scheduleRefresh();
                }, PENDING_ANALYSIS_MS);
                scheduleRefresh();
            });
            overlay.menu.appendChild(analyze);
        }
    }

    function overlayWidth(overlay) {
        const element = overlay.collapsed ? overlay.restoreDot : overlay.panel;
        const rect = element.getBoundingClientRect();
        return Math.max(1, rect.width || element.offsetWidth || (overlay.collapsed ? 18 : 188));
    }

    function overlayHeight(overlay) {
        const element = overlay.collapsed ? overlay.restoreDot : overlay.panel;
        const rect = element.getBoundingClientRect();
        return Math.max(1, rect.height || element.offsetHeight || (overlay.collapsed ? 18 : 40));
    }

    function viewportAxisBounds(elementSize, viewportSize) {
        return { min: VIEWPORT_MARGIN, max: Math.max(VIEWPORT_MARGIN, viewportSize - elementSize - VIEWPORT_MARGIN) };
    }

    function positionOverlay(overlay) {
        if (!overlay?.host?.isConnected) return;
        if (overlay.detached || overlayPosition.detached) {
            overlay.detached = true;
            const xBounds = viewportAxisBounds(overlayWidth(overlay), window.innerWidth);
            const yBounds = viewportAxisBounds(overlayHeight(overlay), window.innerHeight);
            overlay.host.style.left = `${Math.round(core.leftForRatio(xBounds, overlayPosition.xRatio))}px`;
            overlay.host.style.top = `${Math.round(core.leftForRatio(yBounds, overlayPosition.yRatio))}px`;
            return;
        }

        const anchorRect = overlay.anchor ? overlay.anchor.getBoundingClientRect() : null;
        const bounds = core.horizontalBounds(anchorRect, overlayWidth(overlay), window.innerWidth, VIEWPORT_MARGIN);
        overlay.host.style.left = `${Math.round(core.leftForRatio(bounds, overlayPosition.xRatio))}px`;
        if (overlay.anchor) {
            const rect = overlay.anchor.getBoundingClientRect();
            const maxTop = Math.max(VIEWPORT_MARGIN, window.innerHeight - overlayHeight(overlay) - VIEWPORT_MARGIN);
            overlay.host.style.top = `${Math.round(Math.min(maxTop, Math.max(VIEWPORT_MARGIN, rect.top + VIDEO_TOP_INSET)))}px`;
        } else {
            overlay.host.style.top = `${Math.min(Math.max(VIEWPORT_MARGIN, FALLBACK_TOP), Math.max(VIEWPORT_MARGIN, window.innerHeight - 48))}px`;
        }
    }

    function positionAllOverlays() {
        for (const overlay of videoOverlays.values()) positionOverlay(overlay);
        if (fallbackOverlay) positionOverlay(fallbackOverlay);
    }

    function rememberPosition() {
        if (chrome.storage?.local?.set) {
            chrome.storage.local.set({ [POSITION_2D_KEY]: { ...overlayPosition } }, consumeRuntimeError);
        }
    }

    function beginDrag(overlay, event) {
        if (event.button !== 0) return;
        event.preventDefault();
        event.stopPropagation();
        closeAllMenus();

        const pointerId = event.pointerId;
        const startX = event.clientX;
        const startY = event.clientY;
        const panelRect = overlay.panel.getBoundingClientRect();
        const grabX = event.clientX - panelRect.left;
        const grabY = event.clientY - panelRect.top;
        let moved = false;
        try { overlay.dragHandle.setPointerCapture(pointerId); } catch { }

        function move(moveEvent) {
            if (moveEvent.pointerId !== pointerId) return;
            if (!moved && Math.hypot(moveEvent.clientX - startX, moveEvent.clientY - startY) < 3) return;
            moved = true;
            overlay.detached = true;
            overlayPosition.detached = true;
            const xBounds = viewportAxisBounds(overlayWidth(overlay), window.innerWidth);
            const yBounds = viewportAxisBounds(overlayHeight(overlay), window.innerHeight);
            overlayPosition.xRatio = core.ratioForLeft(xBounds, moveEvent.clientX - grabX);
            overlayPosition.yRatio = core.ratioForLeft(yBounds, moveEvent.clientY - grabY);
            positionOverlay(overlay);
        }

        function finish(endEvent) {
            if (endEvent.pointerId !== pointerId) return;
            overlay.dragHandle.removeEventListener("pointermove", move);
            overlay.dragHandle.removeEventListener("pointerup", finish);
            overlay.dragHandle.removeEventListener("pointercancel", finish);
            overlay.ignoreNextMoveClick = moved;
            if (moved) {
                rememberPosition();
                scheduleRefresh();
            }
        }

        overlay.dragHandle.addEventListener("pointermove", move);
        overlay.dragHandle.addEventListener("pointerup", finish);
        overlay.dragHandle.addEventListener("pointercancel", finish);
    }

    function moveByKeyboard(overlay, event) {
        const delta = 0.05;
        const changes = { ArrowLeft: [-delta, 0], ArrowRight: [delta, 0], ArrowUp: [0, -delta], ArrowDown: [0, delta] };
        if (!(event.key in changes)) return;
        event.preventDefault();
        overlay.detached = true;
        overlayPosition.detached = true;
        overlayPosition.xRatio = core.normalizeRatio(overlayPosition.xRatio + changes[event.key][0]);
        overlayPosition.yRatio = core.normalizeRatio(overlayPosition.yRatio + changes[event.key][1]);
        rememberPosition();
        positionOverlay(overlay);
    }

    function toggleMoveOptions(overlay, event) {
        event.preventDefault();
        event.stopPropagation();
        if (overlay.ignoreNextMoveClick) {
            overlay.ignoreNextMoveClick = false;
            return;
        }
        const opening = overlay.contextMenu.hidden;
        closeAllMenus(overlay);
        view.closeMenu(overlay);
        view.setContextMenuOpen(overlay, opening, opening);
    }

    const CONTEXT_ACTIONS = Object.freeze([
        { action: "hide-this", label: "Hide this" },
        { type: "separator" },
        { type: "label", label: "Hide all popups on this site" },
        { action: "hide-all-temporary", label: "Temporary…" },
        { action: "hide-all-visit", label: "During this visit" },
        { action: "hide-all-permanent", label: "Permanently" },
    ]);

    function restoreIndividualOverlay(overlay, event) {
        event?.preventDefault();
        event?.stopPropagation();
        if (overlay.anchor) collapsedAnchors.delete(overlay.anchor);
        else fallbackCollapsed = false;
        view.setCollapsed(overlay, false);
        renderMenu(overlay);
        positionOverlay(overlay);
    }

    function handleContextAction(overlay, action, event) {
        event.preventDefault();
        event.stopPropagation();
        view.closeContextMenu(overlay);
        if (action === "hide-this") {
            if (overlay.anchor) collapsedAnchors.add(overlay.anchor);
            else fallbackCollapsed = true;
            view.setCollapsed(overlay, true);
            positionOverlay(overlay);
            return;
        }
        if (action === "hide-all-visit") {
            setSiteSuppression("visit");
            return;
        }
        if (action === "hide-all-permanent") {
            setSiteSuppression("permanent");
            return;
        }
        if (action !== "hide-all-temporary") return;
        const input = window.prompt(
            "Hide Axioos video controls for how long? Use seconds, minutes, hours, or days (for example: 30m, 2h, 1 day).",
            "30m"
        );
        if (input === null) return;
        const durationMs = core.parseDurationMs(input);
        if (durationMs === null) {
            window.alert("Enter a positive duration such as 30s, 10m, 2h, or 1 day.");
            return;
        }
        setSiteSuppression("temporary", durationMs);
    }

    function createOverlay(anchor) {
        const overlay = view.create(anchor, {
            onPrimaryClick(item, event) {
                event.preventDefault();
                event.stopPropagation();
                const opening = item.menu.hidden;
                closeAllMenus(item);
                view.closeContextMenu(item);
                view.setMenuOpen(item, opening, opening);
            },
            onContextMenu(item, event) {
                event.preventDefault();
                event.stopPropagation();
                closeAllMenus(item);
                view.closeMenu(item);
                view.setContextMenuOpen(item, true, false);
            },
            onRestore: restoreIndividualOverlay,
            onDragStart: beginDrag,
            onMoveMenuClick: toggleMoveOptions,
            onDragKey(item, event) { moveByKeyboard(item, event); },
            onKeyDown(item, event) {
                if (event.key === "ContextMenu" || (event.shiftKey && event.key === "F10")) {
                    event.preventDefault();
                    event.stopPropagation();
                    closeAllMenus(item);
                    view.closeMenu(item);
                    view.setContextMenuOpen(item, true, true);
                    return;
                }
                if (event.key !== "Escape") return;
                view.closeMenu(item);
                view.closeContextMenu(item);
                item.primary.focus({ preventScroll: true });
                event.stopPropagation();
            }
        });
        view.renderContextMenu(overlay, CONTEXT_ACTIONS, (action, event) => handleContextAction(overlay, action, event));
        renderMenu(overlay);
        view.setCollapsed(overlay, anchor ? collapsedAnchors.has(anchor) : fallbackCollapsed);
        positionOverlay(overlay);
        return overlay;
    }

    function createSuppressionOverlay() {
        const overlay = view.create(null, {
            onRestore(_item, event) {
                event.preventDefault();
                event.stopPropagation();
                clearSiteSuppression();
            }
        });
        view.setCollapsed(overlay, true);
        positionOverlay(overlay);
        return overlay;
    }

    function isVisibleVideo(video) {
        if (!video?.isConnected) return false;
        const style = getComputedStyle(video);
        if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity) === 0) return false;
        return core.isUsableVideoRect(video.getBoundingClientRect(), window.innerWidth, window.innerHeight);
    }

    function visibleArea(video) {
        const rect = video.getBoundingClientRect();
        const width = Math.max(0, Math.min(rect.right, window.innerWidth) - Math.max(rect.left, 0));
        const height = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
        return width * height;
    }

    function activeVideos(visible) {
        const chosen = core.pickActiveVideo(visible.map(video => ({
            ref: video,
            selected: video === selectedVideo,
            playing: !video.paused && !video.ended && video.readyState > 2,
            area: visibleArea(video)
        })));
        return chosen ? [chosen] : [];
    }

    function videoAtPoint(event) {
        const path = typeof event.composedPath === "function" ? event.composedPath() : [];
        for (const node of path) if (node instanceof HTMLVideoElement) return node;
        const x = event.clientX;
        const y = event.clientY;
        for (const video of document.querySelectorAll("video")) {
            const rect = video.getBoundingClientRect();
            if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom && isVisibleVideo(video)) return video;
        }
        return null;
    }

    function applyAppearance(appearance) {
        view.applyTheme?.(null, appearance);
        for (const overlay of videoOverlays.values()) view.applyTheme?.(overlay, appearance);
        if (fallbackOverlay) view.applyTheme?.(fallbackOverlay, appearance);
        if (suppressionOverlay) view.applyTheme?.(suppressionOverlay, appearance);
    }

    function destroyRegularOverlays() {
        for (const overlay of videoOverlays.values()) view.destroy(overlay);
        videoOverlays.clear();
        view.destroy(fallbackOverlay);
        fallbackOverlay = null;
    }

    function refresh() {
        refreshQueued = false;
        syncSiteIdentity();
        if (!suppressionLoaded) return;
        const videos = activeVideos([...document.querySelectorAll("video")].filter(isVisibleVideo));
        const socialCandidate = analysisAllowed && videos.length > 0 && isSocialMediaPage();
        const effectiveMediaCount = mediaList.length > 0 || socialCandidate || localAnalyses.size > 0 || serverAnalyses.size > 0 ? Math.max(1, mediaList.length) : 0;
        const mode = core.overlayMode(effectiveMediaCount, videos.length);

        if (mode === "hidden") {
            destroyRegularOverlays();
            view.destroy(suppressionOverlay);
            suppressionOverlay = null;
            return;
        }

        if (isSiteSuppressed()) {
            destroyRegularOverlays();
            if (!suppressionOverlay) suppressionOverlay = createSuppressionOverlay();
            else positionOverlay(suppressionOverlay);
            return;
        }

        view.destroy(suppressionOverlay);
        suppressionOverlay = null;
        const visibleSet = new Set(videos);
        for (const [video, overlay] of videoOverlays.entries()) {
            if (!visibleSet.has(video)) {
                view.destroy(overlay);
                videoOverlays.delete(video);
            }
        }

        if (mode === "video" && overlayPosition.detached) {
            for (const overlay of videoOverlays.values()) view.destroy(overlay);
            videoOverlays.clear();
            if (!fallbackOverlay) fallbackOverlay = createOverlay(null);
            else {
                renderMenu(fallbackOverlay);
                positionOverlay(fallbackOverlay);
            }
            return;
        }

        if (mode === "video") {
            view.destroy(fallbackOverlay);
            fallbackOverlay = null;
            for (const video of videos) {
                let overlay = videoOverlays.get(video);
                if (!overlay) {
                    overlay = createOverlay(video);
                    videoOverlays.set(video, overlay);
                } else {
                    renderMenu(overlay);
                    positionOverlay(overlay);
                }
            }
            return;
        }

        for (const overlay of videoOverlays.values()) view.destroy(overlay);
        videoOverlays.clear();
        if (!fallbackOverlay) fallbackOverlay = createOverlay(null);
        else {
            renderMenu(fallbackOverlay);
            positionOverlay(fallbackOverlay);
        }
    }

    function scheduleRefresh() {
        if (refreshQueued) return;
        refreshQueued = true;
        if (typeof queueMicrotask === "function") {
            queueMicrotask(refresh);
            return;
        }
        Promise.resolve().then(refresh);
    }

    function applyMediaState(message) {
        mediaList = message?.enabled === true && Array.isArray(message?.list)
            ? message.list.filter(item => item && typeof item.id === "string" && item.id.length > 0)
            : [];
        const incoming = Array.isArray(message?.analyses) ? message.analyses : (message?.analysis ? [message.analysis] : []);
        const next = new Map();
        for (const item of incoming) {
            if (item && typeof item.url === "string" && item.url && !next.has(item.url)) next.set(item.url, item);
        }
        serverAnalyses = next;
        analysisAllowed = message?.analysisAllowed === true;
        if (message?.appearance) applyAppearance(message.appearance);
        if (isAnalysisRunning()) scheduleAnalysisTick();
        scheduleRefresh();
    }

    let lastAnalysisPoll = 0;
    function pollAnalysisStatus() {
        if (!isAnalysisRunning()) return;
        const now = Date.now();
        if (now - lastAnalysisPoll < 1200) return;
        lastAnalysisPoll = now;
        safeSend({ type: "media-stat", pageUrl: location.href, refresh: true, pending: true }, (response, error) => {
            if (error === EXTENSION_RELOADED) {
                for (const [key, item] of localAnalyses.entries()) {
                    if (isRunningState(item)) localAnalyses.set(key, { ...item, state: "failed", accepted: false, message: EXTENSION_RELOADED, updatedAt: new Date().toISOString() });
                }
                scheduleRefresh();
                return;
            }
            if (response) applyMediaState(response);
        }, 15000);
    }

    function mutationAffectsVideo(mutation) {
        const target = mutation.target;
        if (target instanceof Element && (target.matches("video") || target.querySelector("video"))) return true;
        for (const node of [...mutation.addedNodes, ...mutation.removedNodes]) {
            if (node instanceof Element && (node.matches("video") || node.querySelector("video"))) return true;
        }
        return false;
    }

    chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
        if (message?.type !== "adm-media-list") return undefined;
        applyMediaState(message);
        if (typeof sendResponse === "function") {
            sendResponse({ ok: true, type: "adm-media-list-applied" });
        }
        return false;
    });
    document.addEventListener("pointerdown", event => {
        if (view.isHostInPath(event.composedPath())) return;
        closeAllMenus();
        const video = videoAtPoint(event);
        if (video && video !== selectedVideo) {
            selectedVideo = video;
            scheduleRefresh();
        }
    }, true);
    document.addEventListener("fullscreenchange", scheduleRefresh, true);
    document.addEventListener("play", scheduleRefresh, true);
    document.addEventListener("loadedmetadata", scheduleRefresh, true);
    document.addEventListener("emptied", scheduleRefresh, true);
    window.addEventListener("resize", scheduleRefresh, { passive: true });
    window.addEventListener("scroll", scheduleRefresh, { passive: true, capture: true });
    window.addEventListener("pageshow", scheduleRefresh, { passive: true });
    window.addEventListener("popstate", scheduleRefresh, { passive: true });
    window.addEventListener("hashchange", scheduleRefresh, { passive: true });
    if (window.navigation?.addEventListener) window.navigation.addEventListener("navigate", scheduleRefresh);

    const observer = new MutationObserver(mutations => {
        if (mutations.some(mutationAffectsVideo)) scheduleRefresh();
    });
    observer.observe(document.documentElement, {
        childList: true, subtree: true, attributes: true, attributeFilter: ["class", "style", "src"]
    });

    if (chrome.storage?.local?.get) {
        chrome.storage.local.get([POSITION_KEY, POSITION_2D_KEY, SUPPRESSION_KEY], result => {
            consumeRuntimeError();
            const storedPosition = result?.[POSITION_2D_KEY];
            overlayPosition = storedPosition && typeof storedPosition === "object" ? {
                xRatio: core.normalizeRatio(storedPosition.xRatio),
                yRatio: core.normalizeRatio(storedPosition.yRatio ?? 0),
                detached: storedPosition.detached === true
            } : { xRatio: core.normalizeRatio(result?.[POSITION_KEY]), yRatio: 0, detached: false };
            const stored = result?.[SUPPRESSION_KEY];
            suppressionEntries = stored && typeof stored === "object" && !Array.isArray(stored)
                ? Object.assign(Object.create(null), stored)
                : Object.create(null);
            suppressionLoaded = true;
            scheduleRefresh();
        });
    } else {
        suppressionLoaded = true;
        scheduleRefresh();
    }

    let analysisTickScheduled = false;
    function scheduleAnalysisTick() {
        if (analysisTickScheduled || !isAnalysisRunning()) return;
        analysisTickScheduled = true;
        setTimeout(() => {
            analysisTickScheduled = false;
            if (!isAnalysisRunning()) return;
            scheduleRefresh();
            pollAnalysisStatus();
            scheduleAnalysisTick();
        }, 1000);
    }

    globalThis.AdmExtensionTheme?.load(stored => { if (stored) applyAppearance(stored); });

    chrome.runtime.sendMessage({ type: "media-stat" }, response => {
        if (chrome.runtime.lastError) {
            consumeRuntimeError();
            applyMediaState({ enabled: false, list: [] });
        } else applyMediaState(response || { enabled: false, list: [] });
    });
})();
