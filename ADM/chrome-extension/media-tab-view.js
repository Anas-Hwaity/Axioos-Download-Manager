"use strict";

function validTabId(activeTabId) {
    return (typeof activeTabId === "number" && Number.isSafeInteger(activeTabId) && activeTabId >= 0) ||
        (typeof activeTabId === "string" && /^(0|[1-9][0-9]*)$/.test(activeTabId));
}

function normalizedText(value) {
    return String(value ?? "")
        .replace(/\s+/g, " ")
        .trim()
        .toLowerCase();
}

function parseHeight(text) {
    const value = String(text ?? "");
    let match = value.match(/\b\d{2,5}x(\d{2,5})\b/i);
    if (match) return Number(match[1]) || 0;
    match = value.match(/\b(\d{3,4})p\b/i);
    return match ? (Number(match[1]) || 0) : 0;
}

function parseBitrate(text) {
    const value = String(text ?? "");
    let match = value.match(/\b(\d+(?:\.\d+)?)\s*(?:mbps|mb\/s|m(?:bit)?\/s)\b/i);
    if (match) return Math.round(Number(match[1]) * 1_000_000);
    match = value.match(/\b(\d+(?:\.\d+)?)\s*(?:kbps|kb\/s|k(?:bit)?\/s|kb)\b/i);
    return match ? Math.round(Number(match[1]) * 1_000) : 0;
}

function parseContainer(text) {
    const value = String(text ?? "").toLowerCase();
    if (/(?:\.|\b)mp4\b/.test(value)) return "mp4";
    if (/(?:\.|\b)(?:mkv|matroska)\b/.test(value)) return "mkv";
    if (/(?:\.|\b)webm\b/.test(value)) return "webm";
    if (/(?:\.|\b)m4a\b/.test(value)) return "m4a";
    if (/(?:\.|\b)mp3\b/.test(value)) return "mp3";
    if (/(?:\.|\b)aac\b/.test(value)) return "aac";
    if (/(?:\.|\b)ts\b/.test(value)) return "ts";
    return "";
}

function isBareFragmentOrManifest(text) {
    const value = normalizedText(text);
    return /^\[?\.?((m4s)|(mp4)|(ts)|(m3u8)|(mpd))\]?$/.test(value) ||
        /(?:^|[/?#])(?:manifest\.mpd|[^/?#]+\.m3u8)(?:$|[?#])/i.test(value);
}

function mediaMeta(item) {
    const info = String(item?.info ?? "");
    const text = String(item?.text ?? "");
    const combined = `${info} ${text}`;
    const height = parseHeight(combined);
    const bitrate = parseBitrate(combined);
    const container = parseContainer(combined);
    const audioOnly = height === 0 && (/(?:^|\b)audio(?:\b|$)/i.test(combined) ||
        container === "m4a" || container === "mp3" || container === "aac");
    const raw = isBareFragmentOrManifest(info) || isBareFragmentOrManifest(text);
    return { height, bitrate, container, audioOnly, raw };
}

function mediaSignature(item) {
    const meta = mediaMeta(item);
    const info = normalizedText(item?.info);
    const text = normalizedText(item?.text).replace(/\.(mp4|mkv|webm|m4a|mp3|aac|ts)$/i, "");

    if (meta.height || meta.bitrate || meta.container) {
        return [text, info, meta.height, meta.bitrate, meta.container, meta.audioOnly ? "audio" : "video"].join("|");
    }
    if (!info && !text) return `id:${String(item?.id ?? "")}`;
    return `${text}|${info}`;
}

function containerRank(container) {
    switch (container) {
        case "mp4": return 6;
        case "mkv": return 5;
        case "webm": return 4;
        case "m4a": return 3;
        case "mp3": return 2;
        case "aac": return 1;
        default: return 0;
    }
}

export function rankAndDedupeMedia(items) {
    const latestBySignature = new Map();
    let index = 0;
    for (const item of Array.isArray(items) ? items : []) {
        if (!item || typeof item.id !== "string" || item.id.length === 0) {
            index++;
            continue;
        }
        latestBySignature.set(mediaSignature(item), { item, index });
        index++;
    }

    return [...latestBySignature.values()]
        .sort((a, b) => {
            const ma = mediaMeta(a.item);
            const mb = mediaMeta(b.item);
            if (ma.raw !== mb.raw) return ma.raw ? 1 : -1;
            if (ma.audioOnly !== mb.audioOnly) return ma.audioOnly ? 1 : -1;
            if (ma.height !== mb.height) return mb.height - ma.height;
            if (ma.bitrate !== mb.bitrate) return mb.bitrate - ma.bitrate;
            const containerDelta = containerRank(mb.container) - containerRank(ma.container);
            if (containerDelta) return containerDelta;
            const labelDelta = String(a.item.info || a.item.text || "")
                .localeCompare(String(b.item.info || b.item.text || ""));
            if (labelDelta) return labelDelta;
            return a.index - b.index;
        })
        .map(entry => entry.item);
}

function mediaForTabUnranked(videoList, activeTabId) {
    if (!Array.isArray(videoList) || !validTabId(activeTabId)) {
        return [];
    }
    const id = String(activeTabId);
    return videoList.filter(item => item && String(item.tabId) === id);
}

export function mediaForTab(videoList, activeTabId) {
    return rankAndDedupeMedia(mediaForTabUnranked(videoList, activeTabId));
}

export function samePageAddress(first, second) {
    const key = value => {
        try {
            const url = new URL(String(value || ""));
            const host = url.hostname.replace(/^(www|m)\./i, "").toLowerCase();
            if (host === "youtu.be") return "youtube.com/watch?v=" + url.pathname.slice(1);
            if (host.endsWith("youtube.com") && url.searchParams.get("v")) return "youtube.com/watch?v=" + url.searchParams.get("v");
            if (host.endsWith("youtube.com") && url.pathname.startsWith("/shorts/")) return "youtube.com/watch?v=" + url.pathname.split("/")[2];
            return host + url.pathname.replace(/\/+$/, "") + url.search;
        } catch {
            return "";
        }
    };
    const a = key(first);
    return a.length > 0 && a === key(second);
}

export function mediaForPage(videoList, activeTabId, pageSessionId, pageUrl = "") {
    const tabMedia = mediaForTabUnranked(videoList, activeTabId);
    const scoped = (typeof pageSessionId !== "string" || pageSessionId.length === 0)
        ? tabMedia
        : tabMedia.filter(item => item && (item.pageSessionId === pageSessionId ||
            (item.source === "ytdlp" && samePageAddress(item.tabUrl, pageUrl))));
    return rankAndDedupeMedia(scoped);
}
