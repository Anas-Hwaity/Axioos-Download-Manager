"use strict";

(() => {
    const STANDARD = Object.freeze({
        nativeMediaDetection: true,
        externalAnalysis: true,
        browserSessionAssistance: true
    });

    const policies = Object.freeze([
        "youtube.com", "youtu.be", "x.com", "twitter.com", "facebook.com", "fb.watch",
        "instagram.com", "threads.net", "tiktok.com", "reddit.com", "redd.it", "vimeo.com",
        "twitch.tv", "dailymotion.com", "snapchat.com", "pinterest.com", "linkedin.com",
        "tumblr.com", "vk.com", "ok.ru"
    ].map(canonicalHost => Object.freeze({ canonicalHost, ...STANDARD })));

    const PUBLIC_WEB = Object.freeze({
        canonicalHost: "*",
        nativeMediaDetection: true,
        externalAnalysis: true,
        browserSessionAssistance: true
    });

    function isLoopbackHost(host) {
        const bare = host.replace(/^\[|\]$/g, "");
        return bare === "localhost" || bare.endsWith(".localhost") || bare === "::1" || /^127\./.test(bare) || bare === "0.0.0.0";
    }

    function publicWeb(url) {
        try {
            const parsed = new URL(String(url || ""));
            if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return null;
            return parsed.hostname && !isLoopbackHost(parsed.hostname.toLowerCase()) ? PUBLIC_WEB : null;
        } catch {
            return null;
        }
    }

    function match(url) {
        try {
            const parsed = new URL(String(url || ""));
            if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return null;
            const host = parsed.hostname.toLowerCase();
            return policies.find(policy => host === policy.canonicalHost || host.endsWith("." + policy.canonicalHost)) || null;
        } catch {
            return null;
        }
    }

    function isSupported(url, capability) {
        const policy = match(url) || publicWeb(url);
        return !!policy && (!capability || policy[capability] === true);
    }

    globalThis.AdmSocialSitePolicy = Object.freeze({ policies, match, publicWeb, isSupported });
})();
