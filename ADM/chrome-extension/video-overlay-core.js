"use strict";

(() => {
    const DEFAULT_RATIO = 1;
    const DEFAULT_MARGIN = 8;
    const DEFAULT_ANCHOR_INSET = 7;
    const MIN_VIDEO_WIDTH = 120;
    const MIN_VIDEO_HEIGHT = 70;

    function finiteNumber(value, fallback) {
        const number = Number(value);
        return Number.isFinite(number) ? number : fallback;
    }

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function normalizeRatio(value) {
        return clamp(finiteNumber(value, DEFAULT_RATIO), 0, 1);
    }

    function horizontalBounds(anchorRect, overlayWidth, viewportWidth,
        margin = DEFAULT_MARGIN, anchorInset = DEFAULT_ANCHOR_INSET) {
        const width = Math.max(0, finiteNumber(overlayWidth, 0));
        const viewport = Math.max(0, finiteNumber(viewportWidth, 0));
        const edge = Math.max(0, finiteNumber(margin, DEFAULT_MARGIN));
        const inset = Math.max(0, finiteNumber(anchorInset, DEFAULT_ANCHOR_INSET));
        const viewportMax = Math.max(edge, viewport - width - edge);

        if (!anchorRect) {
            return { min: edge, max: viewportMax };
        }

        const left = finiteNumber(anchorRect.left, edge);
        const right = finiteNumber(anchorRect.right, left);
        const min = Math.max(edge, left + inset);
        const max = Math.min(viewportMax, right - width - inset);
        if (max >= min) {
            return { min, max };
        }

        const fixed = clamp(right - width - inset, edge, viewportMax);
        return { min: fixed, max: fixed };
    }

    function leftForRatio(bounds, ratio) {
        const min = finiteNumber(bounds?.min, 0);
        const max = Math.max(min, finiteNumber(bounds?.max, min));
        return min + (max - min) * normalizeRatio(ratio);
    }

    function ratioForLeft(bounds, left) {
        const min = finiteNumber(bounds?.min, 0);
        const max = Math.max(min, finiteNumber(bounds?.max, min));
        const span = max - min;
        if (span <= 0) {
            return DEFAULT_RATIO;
        }
        return normalizeRatio((finiteNumber(left, min) - min) / span);
    }

    function isUsableVideoRect(rect, viewportWidth, viewportHeight,
        minWidth = MIN_VIDEO_WIDTH, minHeight = MIN_VIDEO_HEIGHT) {
        if (!rect) return false;
        const width = finiteNumber(rect.width, 0);
        const height = finiteNumber(rect.height, 0);
        if (width < minWidth || height < minHeight) return false;

        const left = finiteNumber(rect.left, 0);
        const right = finiteNumber(rect.right, left + width);
        const top = finiteNumber(rect.top, 0);
        const bottom = finiteNumber(rect.bottom, top + height);
        return bottom > 0 && right > 0 && top < finiteNumber(viewportHeight, 0) &&
            left < finiteNumber(viewportWidth, 0);
    }

    function overlayMode(mediaCount, visibleVideoCount) {
        const media = Math.max(0, Math.trunc(finiteNumber(mediaCount, 0)));
        const videos = Math.max(0, Math.trunc(finiteNumber(visibleVideoCount, 0)));
        if (media === 0) return "hidden";
        return videos > 0 ? "video" : "fallback";
    }

    function pickActiveVideo(candidates) {
        const list = Array.isArray(candidates) ? candidates.filter(item => item && finiteNumber(item.area, 0) > 0) : [];
        if (list.length === 0) return null;
        const selected = list.find(item => item.selected === true);
        if (selected) return selected.ref;
        const playing = list.filter(item => item.playing === true);
        const pool = playing.length > 0 ? playing : list;
        let best = pool[0];
        for (const item of pool) if (finiteNumber(item.area, 0) > finiteNumber(best.area, 0)) best = item;
        return best.ref;
    }

    function parseDurationMs(value) {
        const text = String(value ?? "").trim().toLowerCase();
        const match = /^(\d+(?:\.\d+)?)\s*(s|sec|secs|second|seconds|m|min|mins|minute|minutes|h|hr|hrs|hour|hours|d|day|days)?$/.exec(text);
        if (!match) return null;
        const amount = Number(match[1]);
        if (!Number.isFinite(amount) || amount <= 0) return null;
        const unit = match[2] || "m";
        const multiplier = unit.startsWith("s") ? 1000
            : unit.startsWith("h") ? 60 * 60 * 1000
            : unit.startsWith("d") ? 24 * 60 * 60 * 1000
            : 60 * 1000;
        const duration = amount * multiplier;
        return Number.isSafeInteger(duration) || Number.isFinite(duration) ? Math.round(duration) : null;
    }

    function pageSuppressionKey(value) {
        try {
            return new URL(String(value)).href;
        } catch {
            return String(value ?? "");
        }
    }

    function siteSuppressionKey(value) {
        try {
            const url = new URL(String(value));
            return url.hostname.toLowerCase().replace(/^www\./, "");
        } catch {
            return String(value ?? "").trim().toLowerCase();
        }
    }

    function resolveSuppressionEntry(entry, now = Date.now()) {
        if (!entry || typeof entry !== "object") return { active: false, expired: false };
        if (entry.mode === "permanent") return { active: true, mode: "permanent" };
        if (entry.mode !== "temporary") return { active: false, expired: false };
        const expiresAt = Number(entry.expiresAt);
        if (!Number.isFinite(expiresAt) || expiresAt <= Number(now)) return { active: false, expired: true };
        return { active: true, mode: "temporary" };
    }

    globalThis.AdmVideoOverlayCore = Object.freeze({
        normalizeRatio,
        horizontalBounds,
        leftForRatio,
        ratioForLeft,
        isUsableVideoRect,
        overlayMode,
        pickActiveVideo,
        parseDurationMs,
        pageSuppressionKey,
        siteSuppressionKey,
        resolveSuppressionEntry,
    });
})();
