"use strict";

(() => {
    const MIN_STANDALONE_MEDIA_BYTES = 512 * 1024;
    const DEFAULT_TITLE_LENGTH = 72;
    const SITE_SUFFIX = /\s*[\/|·\-:]\s*(X|Twitter|Instagram|Facebook|TikTok|YouTube|Reddit|Vimeo|Twitch|Threads|LinkedIn|Pinterest|Tumblr|Dailymotion)\s*$/i;
    const BARE_SITE_TITLE = /^(x|twitter|instagram|facebook|tiktok|youtube|reddit|vimeo|twitch|threads|linkedin|pinterest|tumblr|dailymotion|home)$/i;
    const SEGMENT_EXTENSIONS = new Set(["m4s", "ts", "m4f", "cmfv", "cmfa", "f4f", "m2ts", "mts", "aacp", "fmp4"]);
    const MANIFEST_EXTENSIONS = new Set(["m3u8", "m3u", "mpd", "ism", "isml", "f4m"]);
    const RANGE_QUERY_KEYS = ["range", "bytestart", "byteend", "rn", "rbuf", "byterange"];
    const CODEC_DIRECTORY = /^(avc1|avc3|hvc1|hev1|av01|vp09|vp9|vp8|h264|h265)(\.[0-9a-f.]+)?$/i;
    const AUDIO_DIRECTORY = /^(mp4a|aac|opus|ac-3|ec-3|audio|aud)(\.[0-9a-f.]+)?$/i;
    const RESOLUTION_SEGMENT = /^(\d{2,5})x(\d{2,5})$/i;
    const TRANSPORT_NOISE_MIMES = new Set(["application/vnd.yt-ump", "application/x-protobuf", "application/json+protobuf"]);
    const TRANSPORT_NOISE_HOSTS = ["googlevideo.com"];

    function headerValue(headers, name) {
        if (!headers || typeof headers !== "object") return "";
        const wanted = String(name).toLowerCase();
        const key = Object.keys(headers).find(item => item.toLowerCase() === wanted);
        if (!key) return "";
        const value = headers[key];
        if (Array.isArray(value)) return value.length > 0 ? String(value[0] ?? "") : "";
        return value === undefined || value === null ? "" : String(value);
    }

    function parseUrl(url) {
        try {
            const parsed = new URL(String(url || ""));
            return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed : null;
        } catch {
            return null;
        }
    }

    function pathSegments(parsed) {
        return parsed.pathname.split("/").filter(Boolean).map(segment => {
            try { return decodeURIComponent(segment); } catch { return segment; }
        });
    }

    function extensionOf(segment) {
        const match = /\.([a-z0-9]{1,6})$/i.exec(String(segment || ""));
        return match ? match[1].toLowerCase() : "";
    }

    function canonicalMediaUrl(url) {
        const parsed = parseUrl(url);
        if (!parsed) return String(url || "");
        for (const key of RANGE_QUERY_KEYS) parsed.searchParams.delete(key);
        parsed.hash = "";
        return parsed.toString();
    }

    function totalBytes(headers) {
        const contentRange = headerValue(headers, "content-range").trim();
        if (contentRange) {
            const match = /\/\s*(\d+)\s*$/.exec(contentRange);
            return match ? Number(match[1]) : null;
        }
        const length = headerValue(headers, "content-length").trim();
        return /^\d+$/.test(length) ? Number(length) : null;
    }

    function isRangeResponse(headers) {
        return headerValue(headers, "content-range").trim().length > 0;
    }

    function dimensionsOf(parsed) {
        if (!parsed) return null;
        for (const segment of pathSegments(parsed)) {
            const match = RESOLUTION_SEGMENT.exec(segment);
            if (match) return { width: Number(match[1]), height: Number(match[2]) };
        }
        const inline = /(?:^|[^0-9])(\d{3,5})x(\d{3,5})(?:[^0-9]|$)/i.exec(parsed.pathname);
        return inline ? { width: Number(inline[1]), height: Number(inline[2]) } : null;
    }

    function manifestRole(parsed, mime) {
        const segments = pathSegments(parsed).map(item => item.toLowerCase());
        const file = segments[segments.length - 1] || "";
        if (extensionOf(file) === "mpd" || /dash\+xml/i.test(mime)) return "master";
        if (segments.some(item => AUDIO_DIRECTORY.test(item))) return "audio";
        if (segments.some(item => RESOLUTION_SEGMENT.test(item) || CODEC_DIRECTORY.test(item))) return "variant";
        if (/^(chunklist|media|index)[_-]?\w*\.m3u8$/i.test(file) && /\d/.test(file)) return "variant";
        if (/[_-](\d{3,4}p|\d{3,5}k)\.m3u8$/i.test(file)) return "variant";
        return "master";
    }

    function isStreamingTransportNoise(url, mime = "") {
        const parsed = typeof url === "string" ? parseUrl(url) : url;
        if (TRANSPORT_NOISE_MIMES.has(String(mime || "").split(";")[0].trim().toLowerCase())) return true;
        if (!parsed) return false;
        const host = parsed.hostname.toLowerCase();
        return TRANSPORT_NOISE_HOSTS.some(item => host === item || host.endsWith("." + item));
    }

    function mediaSourceOf(item) {
        return String(item?.source || "").toLowerCase() === "ytdlp" ? "ytdlp" : "browser";
    }

    function classifyMediaRequest(data) {
        const url = String(data?.url || "");
        const headers = data?.responseHeaders || {};
        const parsed = parseUrl(url);
        const mime = headerValue(headers, "content-type").split(";")[0].trim().toLowerCase();
        const result = {
            kind: "progressive",
            role: null,
            canonicalUrl: canonicalMediaUrl(url),
            totalBytes: totalBytes(headers),
            rangeResponse: isRangeResponse(headers),
            mime,
            dimensions: dimensionsOf(parsed)
        };
        if (!parsed || isStreamingTransportNoise(parsed, mime)) {
            result.kind = "ignored";
            return result;
        }
        const segments = pathSegments(parsed);
        const file = (segments[segments.length - 1] || "").toLowerCase();
        const ext = extensionOf(file);
        const lowerUrl = url.toLowerCase();

        if (MANIFEST_EXTENSIONS.has(ext) || /mpegurl|dash\+xml|vnd\.ms-sstr/.test(mime)) {
            result.kind = "manifest";
            result.role = manifestRole(parsed, mime);
            return result;
        }
        if (SEGMENT_EXTENSIONS.has(ext) || /m4s|f4f|mp2t|iso\.segment/.test(mime) ||
            /(^|[^a-z])init[-_.]?[a-z0-9]*\.(mp4|m4a|m4v|webm)$/.test(file) ||
            /^(seg|segment|chunk|frag|fragment|part)[-_]?\d+/.test(file) ||
            /[-_](seg|segment|chunk|frag|fragment)[-_]?\d+\.[a-z0-9]+$/.test(file) ||
            lowerUrl.includes("abst") || lowerUrl.includes("f4x")) {
            result.kind = "segment";
            return result;
        }
        if (result.totalBytes !== null && result.totalBytes < MIN_STANDALONE_MEDIA_BYTES) {
            result.kind = "fragment";
            return result;
        }
        return result;
    }

    function looksLikeAddress(text) {
        if (/\s/.test(text)) return false;
        return /^[a-z][a-z0-9+.-]*:\/\//i.test(text) || /^[\w.-]+(:\d{1,5})?\/\S*$/.test(text) || /^[\w.-]+:\d{1,5}$/.test(text);
    }

    function stripUrls(text) {
        return String(text || "").replace(/\b(?:https?:\/\/|www\.)\S+/gi, " ").replace(/\bpic\.(?:twitter|x)\.com\/\S+/gi, " ");
    }

    function truncateAtWord(text, maxLength, ellipsis) {
        if (text.length <= maxLength) return text;
        const slice = text.slice(0, maxLength);
        const lastSpace = slice.lastIndexOf(" ");
        const cut = lastSpace >= Math.floor(maxLength * 0.6) ? slice.slice(0, lastSpace) : slice;
        const trimmed = cut.replace(/[\s,.;:!?"'“”‘’-]+$/u, "");
        return ellipsis ? `${trimmed}…` : trimmed;
    }

    function conciseTitle(title, options = {}) {
        const maxLength = Number(options.maxLength) > 8 ? Number(options.maxLength) : DEFAULT_TITLE_LENGTH;
        const ellipsis = options.ellipsis !== false;
        let text = String(title || "").replace(/\s+/g, " ").trim();
        if (!text) return "";
        let previous;
        do {
            previous = text;
            text = text.replace(SITE_SUFFIX, "").trim();
        } while (text !== previous && text.length > 0);
        if (!text || BARE_SITE_TITLE.test(text) || looksLikeAddress(text)) return "";

        const post = /^(.+?)\s+on\s+(?:X|Twitter)\s*:\s*["“](.*?)["”]?\s*$/i.exec(text) ||
            /^(.+?)\s+on\s+(?:Instagram|Threads|Facebook|TikTok)\s*:\s*["“](.*?)["”]?\s*$/i.exec(text);
        if (post) {
            const author = post[1].replace(/\s*\(@[^)]*\)\s*$/, "").trim();
            const body = stripUrls(post[2]).replace(/\s+/g, " ").trim();
            if (!body) return truncateAtWord(`${author} video`, maxLength, ellipsis);
            const room = Math.max(12, maxLength - author.length - 2);
            return `${author}: ${truncateAtWord(body, room, ellipsis)}`;
        }
        const cleaned = stripUrls(text).replace(/\s+/g, " ").trim();
        return truncateAtWord(cleaned || text, maxLength, ellipsis);
    }

    const GENERIC_MEDIA_BASENAMES = /^(playlist|master|index|manifest|chunklist|media|video|videoplayback|stream|source|main|audio|default|output|file|download|mp4|hls|dash|vid|clip)([-_. ]?\d{0,5}p?)?$/i;
    const SITE_NAMES = Object.freeze({
        "x.com": "X", "twitter.com": "X", "instagram.com": "Instagram", "facebook.com": "Facebook", "fb.watch": "Facebook",
        "tiktok.com": "TikTok", "youtube.com": "YouTube", "youtu.be": "YouTube", "reddit.com": "Reddit", "redd.it": "Reddit",
        "vimeo.com": "Vimeo", "twitch.tv": "Twitch", "threads.net": "Threads", "linkedin.com": "LinkedIn",
        "pinterest.com": "Pinterest", "tumblr.com": "Tumblr", "dailymotion.com": "Dailymotion", "snapchat.com": "Snapchat",
        "bilibili.com": "Bilibili", "kick.com": "Kick", "rumble.com": "Rumble", "streamable.com": "Streamable"
    });

    function looksOpaqueName(value) {
        const text = String(value || "").trim();
        if (!text) return true;
        if (/^[a-z][a-z0-9+.-]*:\/\//i.test(text)) return true;
        const ext = extensionOf(text);
        if (MANIFEST_EXTENSIONS.has(ext) || SEGMENT_EXTENSIONS.has(ext)) return true;
        const base = text.replace(/\.[a-z0-9]{2,6}$/i, "");
        if (GENERIC_MEDIA_BASENAMES.test(base)) return true;
        if (/^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i.test(base)) return true;
        if (/^\d{5,}([_-]\d+)*$/.test(base)) return true;
        if (!/\s/.test(base) && /^[a-z0-9_-]+$/i.test(base)) {
            if (base.length >= 12) return true;
            const letters = base.replace(/[^a-z]/gi, "");
            const digits = base.replace(/[^0-9]/g, "");
            if (base.length >= 8 && digits.length >= 3 && /[a-z]/.test(base) && /[A-Z]/.test(base)) return true;
            if (base.length >= 8 && letters.length > 0 && digits.length / base.length > 0.5) return true;
        }
        return false;
    }

    function siteName(url) {
        const parsed = parseUrl(url);
        if (!parsed) return "";
        const host = parsed.hostname.toLowerCase().replace(/^(www|m|mobile)\./, "");
        for (const key of Object.keys(SITE_NAMES)) {
            if (host === key || host.endsWith("." + key)) return SITE_NAMES[key];
        }
        const parts = host.split(".");
        const core = parts.length >= 2 ? parts[parts.length - 2] : host;
        return core ? core.charAt(0).toUpperCase() + core.slice(1) : "";
    }

    function qualityHint(detail) {
        const text = String(detail || "");
        const dims = /(\d{3,5})\s*[x×]\s*(\d{3,5})/i.exec(text);
        if (dims) return `${dims[2]}p`;
        const p = /\b(\d{3,4})p\b/i.exec(text);
        return p ? `${p[1]}p` : "";
    }

    function displayName(label, pageTitle, detail, index = 0, pageUrl = "") {
        const current = String(label || "").replace(/\s+/g, " ").trim();
        if (current && !looksOpaqueName(current)) return current.slice(0, 240);
        const title = conciseTitle(pageTitle);
        const quality = qualityHint(detail);
        if (title) return quality ? `${title} (${quality})` : title;
        const site = siteName(pageUrl);
        const base = site ? `${site} video` : "Video";
        const number = Number(index) > 0 ? ` ${Number(index) + 1}` : "";
        return quality ? `${base}${number} (${quality})` : `${base}${number}`;
    }

    function isPostHost(parsed) {
        const host = parsed.hostname.toLowerCase();
        return ["x.com", "twitter.com"].some(item => host === item || host.endsWith("." + item));
    }

    function statusPostUrl(url) {
        const parsed = parseUrl(url);
        if (!parsed || !isPostHost(parsed)) return "";
        const match = /^\/([A-Za-z0-9_]{1,30})\/status(?:es)?\/(\d{5,25})/.exec(parsed.pathname);
        if (match) return `https://x.com/${match[1]}/status/${match[2]}`;
        const web = /^\/i\/web\/status\/(\d{5,25})/.exec(parsed.pathname);
        return web ? `https://x.com/i/web/status/${web[1]}` : "";
    }

    function socialAnalysisUrl(pageUrl, candidateUrls = []) {
        const page = parseUrl(pageUrl);
        if (!page) return String(pageUrl || "");
        if (!isPostHost(page)) return page.toString();
        for (const candidate of Array.isArray(candidateUrls) ? candidateUrls : []) {
            const post = statusPostUrl(candidate);
            if (post) return post;
        }
        return statusPostUrl(page.toString()) || page.toString();
    }

    function humanBytes(bytes) {
        const value = Number(bytes);
        if (!Number.isFinite(value) || value <= 0) return "";
        if (value >= 1073741824) return `${(value / 1073741824).toFixed(1)} GB`;
        if (value >= 1048576) return `${(value / 1048576).toFixed(1)} MB`;
        return `${Math.max(1, Math.round(value / 1024))} KB`;
    }

    globalThis.AdmMediaIdentity = Object.freeze({
        MIN_STANDALONE_MEDIA_BYTES,
        headerValue,
        canonicalMediaUrl,
        classifyMediaRequest,
        isStreamingTransportNoise,
        mediaSourceOf,
        conciseTitle,
        looksOpaqueName,
        siteName,
        displayName,
        statusPostUrl,
        socialAnalysisUrl,
        humanBytes
    });
})();
