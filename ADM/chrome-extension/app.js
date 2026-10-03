"use strict";
import './social-site-policy-core.js';
import './media-identity-core.js';
import Logger from './logger.js';
import RequestWatcher from './request-watcher.js';
import Connector from './connector.js';
import { mediaForPage } from './media-tab-view.js';
import PageSessionStore from './page-session-store.js';
import TakeoverOrphanStore from './takeover-orphan-store.js';
import DownloadTakeoverTransaction, { browserDownloadIdentity } from './download-takeover-transaction.js';
import { buildDownloadTakeoverPayload } from './download-takeover-request.js';

const DESKTOP_FAILURE_VISIBILITY_MS = 5000;
const LOCAL_MEDIA_EXTENSIONS = Object.freeze([
    "MP4", "M3U8", "WEBM", "MPD", "MOV", "MPEG", "MPG", "MKV", "FLV", "OGG", "OPUS", "MP3", "AAC", "M4A"
]);
const LOCAL_MEDIA_TYPES = Object.freeze(["video/", "audio/"]);
const MAX_BROWSER_MEDIA_PER_TAB = 64;
const MAX_FORWARDED_MEDIA_KEYS = 2048;
const identity = globalThis.AdmMediaIdentity;

export default class App {
    constructor() {
        this.logger = new Logger();
        this.videoList = [];
        this.browserDetectedMedia = new Map();
        this.forwardedMediaKeys = new Set();
        this.browserMediaSequence = 0;
        this.analysisList = [];
        this.tabTitles = new Map();
        this.appearance = null;
        this.blockedHosts = [];
        this.fileExts = [];
        this.takeoverOrphans = new TakeoverOrphanStore();
        this.pageSessions = new PageSessionStore(
            undefined, undefined,
            () => this.logger.log("page-session persistence failed"));
        this.requestWatcher = new RequestWatcher(
            this.onRequestDataReceived.bind(this),
            tabId => this.pageSessions.current(tabId));
        this.requestWatcher.updateConfig({
            mediaExts: [...LOCAL_MEDIA_EXTENSIONS],
            mediaTypes: [...LOCAL_MEDIA_TYPES]
        });
        this.tabsWatcher = [];
        this.userDisabled = false;
        this.appEnabled = false;
        this.onDownloadCreatedCallback = this.onDownloadCreated.bind(this);
        this.onDeterminingFilenameCallback = this.onDeterminingFilename.bind(this);
        this.onTabUpdateCallback = this.onTabUpdate.bind(this);
        this.activeTabId = -1;
        this.activeTabRevision = 0;
        this.needsSessionResync = true;
        this.connector = new Connector(this.onMessage.bind(this), this.onDisconnect.bind(this));
        this.takeoverTransaction = new DownloadTakeoverTransaction(chrome.downloads, this.takeoverOrphans, this.connector,
            { enrich: download => this.takeoverSessionMaterial(download) });
        this.takeoverInFlight = new Set();
    }

    start() {
        this.logger.log("starting...");
        this.ready = this.initialize();
        this.register();
        return this.ready;
    }

    async initialize() {
        try {
            await this.pageSessions.restore();
        } catch {
            this.logger.log("page-session restore failed");
        }
        this.starAppConnector();
        try {
            const recovered = await this.takeoverOrphans.recoverOrphans(key => this.connector.queryTakeoverOwnership(key));
            for (const item of recovered?.awaiting || []) this.resumeTakeoverConfirmation(item);
        } catch {
            this.logger.log("takeover orphan recovery failed");
        }
        this.logger.log("started.");
    }

    resumeTakeoverConfirmation(item) {
        const id = item?.browserDownloadId;
        if (!Number.isInteger(id) || !item.idempotencyKey || this.takeoverInFlight.has(id)) return;
        this.takeoverInFlight.add(id);
        void this.takeoverTransaction.waitForConfirmation(id, item.idempotencyKey)
            .catch(() => { })
            .finally(() => { this.takeoverInFlight.delete(id); });
    }

    async takeoverSessionMaterial(download) {
        const material = { userAgent: globalThis.navigator?.userAgent || null, cookieHeader: null };
        if (download?.incognito) return material;
        const url = download?.finalUrl || download?.url;
        if (!url) return material;
        const header = await this.collectSessionCookieHeader(url);
        if (header) material.cookieHeader = header;
        return material;
    }

    starAppConnector() {
        this.connector.connect();
    }

    onMessage(msg) {
        this.logger.log("message from Axioos");
        this.appEnabled = msg.enabled === true;
        this.fileExts = msg.fileExts;
        this.blockedHosts = msg.blockedHosts;
        this.tabsWatcher = msg.tabsWatcher;
        this.videoList = Array.isArray(msg.videoList) ? msg.videoList : [];
        this.analysisList = Array.isArray(msg.analysisList) ? msg.analysisList : [];
        this.rememberAppearance(msg.appearance);
        this.requestWatcher.updateConfig({
            mediaExts: [...new Set([...LOCAL_MEDIA_EXTENSIONS, ...(Array.isArray(msg.requestFileExts) ? msg.requestFileExts : [])])],
            blockedHosts: msg.blockedHosts,
            matchingHosts: msg.matchingHosts,
            mediaTypes: [...new Set([...LOCAL_MEDIA_TYPES, ...(Array.isArray(msg.mediaTypes) ? msg.mediaTypes : [])])]
        });
        this.updateActionIcon();
        this.broadcastMediaLists();
        if (this.needsSessionResync) {
            this.needsSessionResync = false;
            for (const session of this.pageSessions.all()) {
                this.announcePageSession(session, "transport-resync");
            }
        }
    }

    onDisconnect() {
        this.needsSessionResync = true;
        this.logger.log("Disconnected from native host!");
        this.logger.log("Disconnected...");
        this.updateActionIcon();
        this.broadcastMediaLists();
    }

    isMonitoringEnabled() {
        this.logger.log(this.appEnabled + " " + this.userDisabled);
        return this.appEnabled === true && this.userDisabled === false && this.connector.isConnected();
    }

    onRequestDataReceived(data) {
        this.logger.log("onRequestDataReceived");
        const classification = identity.classifyMediaRequest(data);
        if (classification.kind !== "progressive" && classification.kind !== "manifest") return;
        this.recordBrowserDetectedMedia(data);
        if (!this.isMonitoringEnabled() || !this.connector.isConnected()) return;
        if (!this.claimMediaForward(data, classification)) return;
        this.connector.postMessage("/media", data);
    }

    claimMediaForward(data, classification) {
        const key = `${data?.tabId ?? ""}|${data?.pageSessionId ?? ""}|${classification.canonicalUrl}`;
        if (this.forwardedMediaKeys.has(key)) return false;
        this.forwardedMediaKeys.add(key);
        if (this.forwardedMediaKeys.size > MAX_FORWARDED_MEDIA_KEYS) {
            this.forwardedMediaKeys.delete(this.forwardedMediaKeys.values().next().value);
        }
        return true;
    }

    forgetForwardedMedia(tabId) {
        const prefix = `${tabId}|`;
        for (const key of [...this.forwardedMediaKeys]) {
            if (key.startsWith(prefix)) this.forwardedMediaKeys.delete(key);
        }
    }

    desktopBrowserItems(desktop) {
        return desktop.filter(item => identity.mediaSourceOf(item) === "browser");
    }

    responseHeaderValue(headers, name) {
        if (!headers || typeof headers !== "object") return "";
        const key = Object.keys(headers).find(item => item.toLowerCase() === name.toLowerCase());
        const values = key ? headers[key] : null;
        return Array.isArray(values) && values.length > 0 ? String(values[0] || "") : "";
    }

    contentDispositionFilename(headers) {
        const value = this.responseHeaderValue(headers, "content-disposition");
        if (!value) return "";
        const utf8 = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(value);
        const basic = /filename\s*=\s*(?:"([^"]+)"|([^;]+))/i.exec(value);
        const raw = utf8?.[1] || basic?.[1] || basic?.[2] || "";
        try { return decodeURIComponent(String(raw).trim()).replace(/[\r\n]/g, "").slice(0, 512); }
        catch { return String(raw).trim().replace(/[\r\n]/g, "").slice(0, 512); }
    }

    browserMediaName(data) {
        const pageTitle = identity.conciseTitle(typeof data?.file === "string" ? data.file : "");
        if (pageTitle) return pageTitle;
        const headerName = this.contentDispositionFilename(data?.responseHeaders);
        if (headerName) return headerName;
        try {
            const url = new URL(data.url);
            return decodeURIComponent(url.pathname.split("/").filter(Boolean).pop() || url.hostname).slice(0, 512);
        } catch {
            return "Detected media";
        }
    }

    browserMediaDetail(data, classification = identity.classifyMediaRequest(data)) {
        if (classification.kind === "manifest") {
            const dash = /mpd$/i.test(new URL(classification.canonicalUrl).pathname) || /dash/i.test(classification.mime);
            const dims = classification.dimensions;
            const quality = dims ? `${dims.width}×${dims.height}` : "all qualities";
            return `${dash ? "DASH" : "HLS"} stream • ${quality}`;
        }
        const parts = [];
        const dims = classification.dimensions;
        if (dims) parts.push(`${dims.width}×${dims.height}`);
        const mime = classification.mime;
        if (mime) parts.push(mime.replace(/^(video|audio)\//i, "").toUpperCase());
        const size = identity.humanBytes(classification.totalBytes);
        if (size) parts.push(size);
        return parts.join(" • ");
    }

    browserMediaLabel(data) {
        return this.browserMediaName(data).slice(0, 240);
    }

    browserMediaMetadata(item) {
        if (!item) return null;
        const mimeType = this.responseHeaderValue(item.responseHeaders, "content-type").split(";")[0].trim();
        const sizeBytes = item.totalBytes ?? null;
        const dims = item.dimensions || null;
        return {
            name: item.name || item.label || "Detected media",
            link: item.url || "",
            pageLink: item.referer || "",
            mimeType: mimeType || "",
            sizeBytes: Number.isSafeInteger(sizeBytes) ? sizeBytes : null,
            durationSeconds: null,
            width: dims ? dims.width : null,
            height: dims ? dims.height : null,
            aspectRatio: "",
            bitrateKbps: null,
            detail: item.detail || "",
            detectedBy: "Browser network monitoring"
        };
    }

    recordBrowserDetectedMedia(data) {
        if (!data || !this.isSupportedProtocol(data.url) || data.tabId === undefined || data.tabId === null || !data.pageSessionId) return;
        const tabId = String(data.tabId);
        const session = this.pageSessions.current(tabId);
        if (!session || session.pageSessionId !== data.pageSessionId) return;
        const classification = identity.classifyMediaRequest(data);
        if (classification.kind !== "progressive" && classification.kind !== "manifest") return;
        const url = classification.rangeResponse ? classification.canonicalUrl : data.url;
        const fields = {
            url,
            name: this.browserMediaName(data),
            detail: this.browserMediaDetail(data, classification),
            kind: classification.kind,
            role: classification.role,
            totalBytes: classification.totalBytes,
            dimensions: classification.dimensions,
            requestData: classification.kind === "manifest" ? { ...data } : null
        };
        const existing = [...this.browserDetectedMedia.values()].find(item =>
            item.tabId === tabId && item.pageSessionId === data.pageSessionId &&
            identity.canonicalMediaUrl(item.url) === classification.canonicalUrl);
        if (existing) {
            Object.assign(existing, fields, {
                label: fields.name,
                totalBytes: fields.totalBytes ?? existing.totalBytes,
                referer: data.tabUrl || existing.referer,
                responseHeaders: data.responseHeaders || existing.responseHeaders
            });
            this.sendMediaListToTab(tabId);
            return;
        }
        const id = `browser-${tabId}-${++this.browserMediaSequence}`;
        this.browserDetectedMedia.set(id, {
            id, tabId, pageSessionId: data.pageSessionId, referer: data.tabUrl || session.pageUrl || "",
            ...fields, label: fields.name,
            responseHeaders: data.responseHeaders || {}
        });
        const sameTab = [...this.browserDetectedMedia.values()].filter(item => item.tabId === tabId);
        if (sameTab.length > MAX_BROWSER_MEDIA_PER_TAB) this.browserDetectedMedia.delete(sameTab[0].id);
        this.sendMediaListToTab(tabId);
        this.updateActionIcon();
    }

    browserMediaForTab(tabId) {
        const session = this.pageSessions.current(tabId);
        if (!session) return [];
        return [...this.browserDetectedMedia.values()].filter(item =>
            item.tabId === String(tabId) && item.pageSessionId === session.pageSessionId);
    }

    visibleBrowserMediaForTab(tabId, desktopItemCount = this.mediaListForTab(tabId).length) {
        const all = this.browserMediaForTab(tabId);
        const hasMaster = all.some(item => item.kind === "manifest" && item.role === "master");
        const visible = all.filter(item => {
            if (item.kind !== "manifest") return true;
            if (desktopItemCount > 0) return false;
            if (item.role === "audio") return false;
            if (item.role === "variant") return !hasMaster;
            return true;
        });
        const height = item => Number(item?.dimensions?.height) || 0;
        return visible
            .map((item, index) => ({ item, index }))
            .sort((a, b) => {
                const am = a.item.kind === "manifest" ? 1 : 0;
                const bm = b.item.kind === "manifest" ? 1 : 0;
                if (am !== bm) return am - bm;
                if (height(a.item) !== height(b.item)) return height(b.item) - height(a.item);
                const sizeDelta = (Number(b.item.totalBytes) || 0) - (Number(a.item.totalBytes) || 0);
                if (sizeDelta) return sizeDelta;
                return a.index - b.index;
            })
            .map(entry => entry.item);
    }

    onDeterminingFilename(download, suggest) {
        this.logger.log("onDeterminingFilename");
        if (this.userDisabled) return;
        const url = download.finalUrl || download.url;
        const handOffIfEligible = () => {
            if (!this.appEnabled || this.userDisabled || !this.shouldTakeOver(url, download.filename)) return;
            if (this.takeoverInFlight.has(download.id)) return;
            const session = this.pageSessions.current(this.activeTabId);
            let payload;
            try {
                payload = buildDownloadTakeoverPayload(download, browserDownloadIdentity(download), session?.pageSessionId || null);
            } catch {
                return;
            }
            this.takeoverInFlight.add(download.id);
            void this.takeoverTransaction.execute(download, payload).then(result => {
                if (!result.accepted && result.reason === "TransportFailure") this.showDesktopUnavailable();
            }).catch(() => {
                this.showDesktopUnavailable();
            }).finally(() => {
                this.takeoverInFlight.delete(download.id);
            });
        };
        if (!this.connector.isConnected()) {
            void this.connector.launchApp().then(ready => {
                if (!ready) { this.showDesktopUnavailable(); return; }
                handOffIfEligible();
            });
            return;
        }
        handOffIfEligible();
    }

    onDownloadCreated(download) {
        this.logger.log("onDownloadCreated");
    }

    onTabUpdate(tabId, changeInfo, tab) {
        if (changeInfo?.title || tab?.title) this.tabTitles.set(String(tabId), String(changeInfo?.title || tab?.title));
        if (!this.isMonitoringEnabled()) {
            return;
        }
        if (changeInfo.title) {
            if (this.tabsWatcher &&
                this.tabsWatcher.find(t => tab.url.indexOf(t) > 0)) {
                this.logger.log("Tab changed");
                try {
                    const session = this.pageSessions.current(tabId);
                    this.connector.postMessage("/tab-update", {
                        tabUrl: tab.url,
                        tabTitle: identity.conciseTitle(changeInfo.title, { maxLength: 120, ellipsis: false }) || changeInfo.title,
                        tabId: tabId + "",
                        pageSessionId: session?.pageSessionId
                    });
                } catch (ex) {
                    this.logger.log("Tab update failed");
                }
            }
        }
    }

    register() {
        chrome.downloads.onCreated.addListener(
            this.onDownloadCreatedCallback
        );
        chrome.downloads.onDeterminingFilename.addListener(
            this.onDeterminingFilenameCallback
        );
        chrome.tabs.onUpdated.addListener(
            this.onTabUpdateCallback
        );
        chrome.runtime.onMessage.addListener(this.onPopupMessage.bind(this));
        this.requestWatcher.register();
        this.attachContextMenu();
        chrome.tabs.onActivated.addListener(this.onTabActivated.bind(this));
        chrome.tabs.onRemoved.addListener(this.onTabRemoved.bind(this));
        chrome.tabs.onReplaced.addListener(this.onTabReplaced.bind(this));
        chrome.webNavigation.onCommitted.addListener(this.onNavigationCommitted.bind(this));
        chrome.webNavigation.onHistoryStateUpdated.addListener(this.onHistoryStateUpdated.bind(this));
        chrome.webNavigation.onReferenceFragmentUpdated.addListener(this.onReferenceFragmentUpdated.bind(this));
        this.connector.registerAlarmListener();
        const initialRevision = this.activeTabRevision;
        chrome.tabs.query({ active: true, currentWindow: true }, tabs => {
            void chrome.runtime?.lastError;
            void Promise.resolve(this.ready).then(() => {
                if (this.activeTabRevision === initialRevision && tabs && tabs[0] && tabs[0].id !== undefined) {
                    this.ensurePageSession(tabs[0].id, tabs[0].url || "", "startup");
                    this.onTabActivated({ tabId: tabs[0].id });
                }
            });
        });
    }

    collectSessionCookieHeader(url) {
        return new Promise(resolve => {
            if (!chrome.cookies?.getAll) { resolve(""); return; }
            try {
                chrome.cookies.getAll({ url }, cookies => {
                    if (chrome.runtime?.lastError || !Array.isArray(cookies)) { resolve(""); return; }
                    const parts = [];
                    for (const cookie of cookies) {
                        const name = String(cookie?.name || "");
                        const value = String(cookie?.value || "");
                        if (!name || /[\r\n;]/.test(name) || /[\r\n]/.test(value)) continue;
                        parts.push(`${name}=${value}`);
                    }
                    const header = parts.join("; ");
                    resolve(header.length <= 65536 ? header : "");
                });
            } catch {
                resolve("");
            }
        });
    }

    isSupportedProtocol(url) {
        if (!url) return false;
        let u = new URL(url);
        return u.protocol === 'http:' || u.protocol === 'https:';
    }

    shouldTakeOver(url, file) {
        let u = new URL(url);
        if (!this.isSupportedProtocol(url)) {
            return false;
        }
        let hostName = u.host;
        if (this.blockedHosts.find(item => hostName.indexOf(item) >= 0)) {
            return false;
        }
        let path = file || u.pathname;
        let upath = path.toUpperCase();
        if (this.fileExts.find(ext => upath.endsWith(String(ext).startsWith(".") ? String(ext).toUpperCase() : "." + String(ext).toUpperCase()))) {
            return true;
        }
        return false;
    }

    updateActionIcon() {
        chrome.action.setIcon({ path: this.getActionIcon() });
        let vc = "";
        const currentMedia = this.currentMediaList();
        if (currentMedia.length > 0) {
            let len = currentMedia.length;
            if (len > 0) {
                vc = len + "";
            }
        }
        chrome.action.setBadgeText({ text: vc });
        if (!this.connector.isConnected()) {
            this.logger.log("Not connected...");
            if (this.visibleBrowserMediaForTab(this.activeTabId).length > 0) {
                chrome.action.setPopup({ popup: "./popup.html" });
            } else {
                chrome.action.setPopup({ popup: "./error.html" });
            }
            return;
        }
        if (!this.appEnabled) {
            chrome.action.setPopup({ popup: "./disabled.html" });
            return;
        }
        else {
            chrome.action.setPopup({ popup: "./popup.html" });
            return;
        }
    }

    getActionIconName(icon) {
        return this.isMonitoringEnabled() ? icon + ".png" : icon + "-mono.png";
    }

    currentMediaList() {
        const session = this.pageSessions.current(this.activeTabId);
        return mediaForPage(this.videoList, this.activeTabId, session?.pageSessionId, session?.pageUrl || "");
    }

    mediaListForTab(tabId) {
        const session = this.pageSessions.current(tabId);
        if (!session) {
            return [];
        }
        return mediaForPage(this.videoList, tabId, session.pageSessionId, session.pageUrl || "");
    }

    rememberAppearance(appearance) {
        if (!appearance || typeof appearance !== "object") return;
        const next = {
            theme: typeof appearance.theme === "string" ? appearance.theme.slice(0, 32) : "glacier",
            accent: appearance.accent === "solid" ? "solid" : "gradient"
        };
        if (this.appearance && this.appearance.theme === next.theme && this.appearance.accent === next.accent) return;
        this.appearance = next;
        chrome.storage?.local?.set?.({ "adm.appearance.v1": next }, () => { void chrome.runtime?.lastError; });
    }

    readableName(tabId, label, detail, index) {
        const session = this.pageSessions.current(tabId);
        return identity.displayName(label, this.tabTitles.get(String(tabId)) || "", detail, index, session?.pageUrl || "");
    }

    popupMediaList() {
        const desktop = this.currentMediaList();
        const desktopIds = new Set(desktop.map(item => String(item?.id ?? "")));
        const projected = desktop.map((item, index) => ({
            ...item,
            text: this.readableName(this.activeTabId, typeof item?.text === "string" ? item.text : "", item?.info, index),
            source: identity.mediaSourceOf(item)
        }));
        for (const item of this.visibleBrowserMediaForTab(this.activeTabId, this.desktopBrowserItems(desktop).length)) {
            if (desktopIds.has(String(item.id))) continue;
            projected.push({
                id: item.id,
                text: this.readableName(this.activeTabId, item.label, item.detail, projected.length),
                info: item.detail || "Detected by browser monitoring",
                source: "browser"
            });
        }
        return projected;
    }

    analysesForTab(tabId) {
        const matches = this.analysisList.filter(item => String(item?.tabId ?? "") === String(tabId) && typeof item?.url === "string");
        matches.sort((a, b) => Date.parse(b.updatedAt || b.startedAt || 0) - Date.parse(a.updatedAt || a.startedAt || 0));
        return matches;
    }

    analysisForTab(tabId) {
        return this.analysesForTab(tabId)[0] || null;
    }

    isSocialMediaUrl(url) {
        if (!this.isSupportedProtocol(url)) return false;
        return !!globalThis.AdmSocialSitePolicy?.isSupported(url, "externalAnalysis");
    }

    detectBrowserForYtDlp(userAgent) {
        const ua = String(userAgent || "");
        if (/Edg\//i.test(ua)) return "edge";
        if (/OPR\//i.test(ua)) return "opera";
        if (/Vivaldi/i.test(ua)) return "vivaldi";
        if (/Firefox\//i.test(ua)) return "firefox";
        if (/Chrome\//i.test(ua) || /Chromium/i.test(ua)) return "chrome";
        return null;
    }

    overlayMediaListForTab(tabId) {
        const desktop = this.mediaListForTab(tabId);
        const desktopUrls = new Set(desktop.map(item => typeof item?.text === "string" ? item.text : "").filter(Boolean));
        const projected = desktop.map((item, index) => {
            const info = typeof item?.info === "string" ? item.info.trim() : "";
            const text = typeof item?.text === "string" ? item.text.trim() : "";
            const concise = text && !/^https?:\/\//i.test(text) && !identity.looksOpaqueName(text) ? identity.conciseTitle(text.replace(/\.[a-z0-9]{2,5}$/i, "")) : "";
            const label = this.readableName(tabId, concise, info, index);
            return { id: String(item.id), label: label.slice(0, 240), detail: info, source: identity.mediaSourceOf(item) };
        });
        for (const item of this.visibleBrowserMediaForTab(tabId, this.desktopBrowserItems(desktop).length)) {
            if (!desktopUrls.has(item.url)) projected.push({
                id: String(item.id),
                label: this.readableName(tabId, item.name || item.label || "", item.detail, projected.length),
                detail: item.detail || "Detected by browser monitoring",
                source: "browser"
            });
        }
        return projected;
    }

    overlayEnabled(tabId) {
        if (this.isMonitoringEnabled() || this.browserMediaForTab(tabId).length > 0) return true;
        return this.appEnabled === true && this.userDisabled === false && this.mediaListForTab(tabId).length > 0;
    }

    sendMediaListToTab(tabId) {
        if (tabId === undefined || tabId === null || !chrome.tabs || typeof chrome.tabs.sendMessage !== "function") {
            return;
        }
        const enabled = this.overlayEnabled(tabId);
        chrome.tabs.sendMessage(Number(tabId), {
            type: "adm-media-list",
            enabled: enabled,
            list: enabled ? this.overlayMediaListForTab(tabId) : [],
            analysis: this.analysisForTab(tabId),
            analyses: this.analysesForTab(tabId),
            analysisAllowed: this.userDisabled === false,
            appearance: this.appearance
        }, () => {
            void chrome.runtime?.lastError;
        });
    }

    broadcastMediaLists() {
        for (const session of this.pageSessions.all()) {
            this.sendMediaListToTab(session.tabId);
        }
    }

    getActionIcon() {
        return {
            "16": this.getActionIconName("icon16"),
            "48": this.getActionIconName("icon48"),
            "128": this.getActionIconName("icon128")
        }
    }

    triggerDownload(url, file, referer, size, mime) {
        return new Promise(resolve => chrome.cookies.getAll({ "url": url }, cookies => {
            let cookieStr = undefined;
            if (cookies) {
                cookieStr = cookies.map(cookie => cookie.name + "=" + cookie.value).join("; ");
            }
            let requestHeaders = { "User-Agent": [navigator.userAgent] };
            if (referer) {
                requestHeaders["Referer"] = [referer];
            }
            let responseHeaders = {};
            if (size) {
                let fz = +size;
                if (fz > 0) {
                    responseHeaders["Content-Length"] = [fz];
                }
            }
            if (mime) {
                responseHeaders["Content-Type"] = [mime];
            }
            let data = {
                url: url,
                cookie: cookieStr,
                requestHeaders: requestHeaders,
                responseHeaders: responseHeaders,
                filename: file,
                fileSize: size,
                mimeType: mime
            };
            void this.connector.postMessageWithDesktop("/download", data).then(result => {
                if (!result) this.showDesktopUnavailable();
                resolve(!!result);
            }).catch(() => {
                this.showDesktopUnavailable();
                resolve(false);
            });
        }));
    }

    diconnect() {
        this.onDisconnect();
    }

    showDesktopUnavailable() {
        try {
            chrome.action.setPopup({ popup: "./error.html" });
            chrome.action.setBadgeText({ text: "!" });
            if (chrome.action.setBadgeBackgroundColor) {
                chrome.action.setBadgeBackgroundColor({ color: "#C62828" });
            }
            if (chrome.action.setTitle) {
                chrome.action.setTitle({ title: "Axioos desktop app is unavailable" });
            }
        } catch { }
        setTimeout(() => {
            try {
                this.updateActionIcon();
            } catch { }
        }, DESKTOP_FAILURE_VISIBILITY_MS);
    }

    onPopupMessage(request, sender, sendResponse) {
        this.logger.log("Popup message received");
        if (request.type === "stat") {
            if (Number.isSafeInteger(request.tabId) && String(request.tabId) !== this.activeTabId) {
                this.onTabActivated({ tabId: request.tabId });
            }
            let resp = {
                enabled: this.isMonitoringEnabled() || this.browserMediaForTab(this.activeTabId).length > 0,
                list: this.popupMediaList(),
                appearance: this.appearance
            };
            sendResponse(resp);
        }
        else if (request.type === "media-stat") {
            const tabId = sender?.tab?.id;
            if (tabId === undefined || tabId === null) {
                sendResponse({ enabled: false, list: [] });
                return;
            }
            const documentId = typeof sender?.documentId === "string" ? sender.documentId : null;
            const current = this.pageSessions.current(tabId);
            if (documentId && current && current.documentId && current.documentId !== documentId) {
                this.beginPageSession(tabId, sender?.tab?.url || sender?.url || "",
                    "content-document-change", documentId);
            } else {
                const session = this.ensurePageSession(tabId, sender?.tab?.url || sender?.url || "",
                    "content-script-bootstrap", documentId);
                if (session && documentId && !session.documentId) this.pageSessions.adoptDocument(tabId, documentId);
            }
            const enabled = this.overlayEnabled(tabId);
            const reply = () => sendResponse({
                enabled: enabled,
                list: enabled ? this.overlayMediaListForTab(tabId) : [],
                analysis: this.analysisForTab(tabId),
                analyses: this.analysesForTab(tabId),
                analysisAllowed: this.userDisabled === false,
                appearance: this.appearance
            });
            const pendingAnalysis = this.analysesForTab(tabId).some(item => item?.state === "queued" || item?.state === "running");
            if (request.refresh === true && (this.connector.isConnected() || pendingAnalysis || request.pending === true)) {
                this.connector.onTimer().finally(reply);
                return true;
            }
            reply();
        }
        else if (request.type === "ensure-desktop") {
            void this.connector.launchApp().then(ready => {
                if (ready) this.updateActionIcon();
                else this.showDesktopUnavailable();
                if (typeof sendResponse === "function") sendResponse({ accepted: !!ready });
            }).catch(() => {
                this.showDesktopUnavailable();
                if (typeof sendResponse === "function") sendResponse({ accepted: false });
            });
            return true;
        }
        else if (request.type === "cmd") {
            this.userDisabled = request.enabled === false;
            this.logger.log("request.enabled:" + request.enabled);
            if (request.enabled && !this.connector.isConnected()) {
                this.connector.launchApp();
                return;
            }
            this.updateActionIcon();
            this.broadcastMediaLists();
        }
        else if (request.type === "vid") {
            const vid = String(request.itemId ?? "");
            const browserMedia = this.browserDetectedMedia.get(vid);
            if (browserMedia && browserMedia.kind === "manifest" && browserMedia.requestData) {
                void this.connector.postMessageWithDesktop("/media", browserMedia.requestData).then(result => {
                    if (!result) this.showDesktopUnavailable();
                    if (typeof sendResponse === "function") sendResponse({
                        accepted: !!result,
                        reason: result ? "Axioos is reading the stream qualities for this video." : "Axioos desktop did not accept the stream."
                    });
                }).catch(() => {
                    this.showDesktopUnavailable();
                    if (typeof sendResponse === "function") sendResponse({ accepted: false, reason: "Could not contact the Axioos desktop app." });
                });
                return true;
            }
            if (browserMedia) {
                const mime = this.responseHeaderValue(browserMedia.responseHeaders, "content-type").split(";")[0].trim() || null;
                const size = browserMedia.totalBytes ? String(browserMedia.totalBytes) : null;
                void Promise.resolve(this.triggerDownload(browserMedia.url, browserMedia.name || null, browserMedia.referer, size, mime)).then(accepted => {
                    if (typeof sendResponse === "function") sendResponse({ accepted, reason: accepted ? "" : "Axioos desktop did not accept the selected media." });
                });
                return true;
            }
            void this.connector.postMessageWithDesktop("/vid", { vid }).then(result => {
                if (!result) this.showDesktopUnavailable();
                if (typeof sendResponse === "function") sendResponse({ accepted: !!result, reason: result ? "" : "Axioos desktop did not accept the selected media." });
            }).catch(() => {
                this.showDesktopUnavailable();
                if (typeof sendResponse === "function") sendResponse({ accepted: false, reason: "Could not contact the Axioos desktop app." });
            });
            return true;
        }
        else if (request.type === "clear" || request.type === "clear-detected-media") {
            this.browserDetectedMedia.clear();
            this.videoList = [];
            this.analysisList = [];
            this.updateActionIcon();
            this.broadcastMediaLists();
            if (this.connector.isConnected()) void this.connector.postMessage("/clear", {}).catch(() => {});
            if (typeof sendResponse === "function") sendResponse({ accepted: true });
        }
        else if (request.type === "media-metadata") {
            const id = String(request.itemId || "");
            const browserMedia = this.browserDetectedMedia.get(id);
            if (browserMedia) {
                sendResponse({ accepted: true, metadata: this.browserMediaMetadata(browserMedia) });
                return;
            }
            const desktop = this.videoList.find(item => String(item?.id ?? "") === id);
            if (desktop) {
                sendResponse({ accepted: true, metadata: {
                    name: desktop.text || "Detected media",
                    detail: desktop.info || "",
                    link: "", pageLink: sender?.tab?.url || "", mimeType: "", sizeBytes: null,
                    durationSeconds: null, width: null, height: null, aspectRatio: "", bitrateKbps: null,
                    detectedBy: "Axioos desktop media parser"
                }});
                return;
            }
            sendResponse({ accepted: false, reason: "Metadata is no longer available for this detected item." });
        }
        else if (request.type === "analyze-page-video") {
            const tabId = sender?.tab?.id;
            const url = request.url || sender?.tab?.url || sender?.url || "";
            if (tabId === undefined || tabId === null || !this.isSocialMediaUrl(url)) {
                sendResponse({ accepted: false, reason: "yt-dlp analysis is available for public web pages only." });
                return;
            }
            const session = this.ensurePageSession(tabId, sender?.tab?.url || sender?.url || url, "analysis-request", sender?.documentId || null);
            const payload = {
                url,
                tabId: String(tabId),
                pageSessionId: session?.pageSessionId,
                browser: request.browser || this.detectBrowserForYtDlp(request.userAgent || navigator.userAgent),
                userAgent: request.userAgent || navigator.userAgent || ""
            };
            let answered = false;
            const deliver = sendResponse;
            const answer = value => {
                if (answered) return;
                answered = true;
                clearInterval(keepAlive);
                clearTimeout(deadline);
                try { deliver(value); } catch { }
            };
            const keepAlive = setInterval(() => { try { chrome.runtime.getPlatformInfo(() => { void chrome.runtime.lastError; }); } catch { } }, 20000);
            const deadline = setTimeout(() => answer({ accepted: false, reason: "Axioos did not answer within 40 seconds. Make sure the app is running, then try again." }), 40000);
            sendResponse = answer;
            void (async () => {
                if (request.browserSessionAssistance === true) {
                    const prior = this.analysisList.find(item => String(item?.tabId) === String(tabId) && item?.url === url &&
                        item?.state === "failed" && item?.code === "AuthenticationRequired");
                    if (!prior) {
                        sendResponse({ accepted: false, reason: "Browser-session retry is only available after an authentication-required analysis failure." });
                        return;
                    }
                    const cookieHeader = await this.collectSessionCookieHeader(url);
                    if (!cookieHeader) {
                        sendResponse({ accepted: false, reason: "No browser session cookies are available for this page." });
                        return;
                    }
                    let sessionResult = null;
                    try {
                        sessionResult = await this.connector.submitSocialAnalysisSession({
                            targetUrl: url, tabId: String(tabId), pageSessionId: session?.pageSessionId, cookieHeader
                        });
                    } catch {
                        sendResponse({ accepted: false, reason: "Axioos could not accept browser-session material over the native channel." });
                        return;
                    }
                    if (sessionResult?.payload?.status !== "accepted") {
                        sendResponse({ accepted: false, reason: sessionResult?.payload?.detail || "Axioos rejected browser-session material." });
                        return;
                    }
                }
                const result = await this.submitPageAnalysis(session, payload);
                if (!result) this.showDesktopUnavailable();
                sendResponse(result ? { accepted: true } : { accepted: false, reason: "The desktop app did not answer. Make sure it is running, then try again." });
            })().catch(error => {
                this.logger.log("Analysis request failed: " + String(error?.message || error));
                sendResponse({ accepted: false, reason: "The Axioos extension could not send this request. Try again." });
            });
            return true;
        }
        else if (request.type === "cancel-page-video-analysis") {
            const tabId = sender?.tab?.id;
            const url = request.url || sender?.tab?.url || sender?.url || "";
            if (tabId === undefined || tabId === null || !this.isSocialMediaUrl(url)) {
                sendResponse({ accepted: false, reason: "No cancellable social analysis is associated with this page." });
                return;
            }
            const session = this.ensurePageSession(tabId, sender?.tab?.url || sender?.url || url, "analysis-cancel", sender?.documentId || null);
            void this.connector.postMessage("/cancel-page-video-analysis", {
                url,
                tabId: String(tabId),
                pageSessionId: session?.pageSessionId
            }).then(result => {
                sendResponse(result ? { accepted: true } : { accepted: false, reason: "Axioos desktop is not connected to an active analysis." });
            });
            return true;
        }
    }

    sendLinkToADM(info, tab) {
        let url = info.linkUrl;
        if (!this.isSupportedProtocol(url)) {
            url = info.srcUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            url = info.pageUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            return;
        }
        this.triggerDownload(url, null, info.pageUrl, null, null);
    }

    sendImageToADM(info, tab) {
        let url = info.srcUrl;
        if (!this.isSupportedProtocol(url))
            url = info.linkUrl;
        if (!this.isSupportedProtocol(url)) {
            url = info.pageUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            return;
        }
        this.triggerDownload(url, null, info.pageUrl, null, null);
    }

    onMenuClicked(info, tab) {
        if (info.menuItemId == "download-any-link") {
            this.sendLinkToADM(info, tab);
        }
        if (info.menuItemId == "download-image-link") {
            this.sendImageToADM(info, tab);
        }
    }

    attachContextMenu() {
        chrome.contextMenus.onClicked.addListener(this.onMenuClicked.bind(this));
        chrome.contextMenus.create({
            id: 'download-any-link',
            title: "Download with Axioos",
            contexts: ["link", "video", "audio", "all"]
        }, () => { void chrome.runtime.lastError; });

        chrome.contextMenus.create({
            id: 'download-image-link',
            title: "Download Image with Axioos",
            contexts: ["image"]
        }, () => { void chrome.runtime.lastError; });
    }

    async submitPageAnalysis(session, payload) {
        const announce = session ? {
            tabId: session.tabId,
            pageSessionId: session.pageSessionId,
            tabUrl: session.pageUrl,
            sessionReason: "analysis-request"
        } : null;
        for (let attempt = 0; attempt < 2; attempt++) {
            if (announce && !await this.connector.postMessageWithDesktop("/page-session", announce)) continue;
            const result = await this.connector.postMessageWithDesktop("/analyze-page-video", payload);
            if (result) return result;
        }
        return null;
    }

    announcePageSession(session, reason) {
        if (!session) {
            return;
        }
        if (!this.connector.isConnected()) {
            this.needsSessionResync = true;
            return;
        }
        this.connector.postMessage("/page-session", {
            tabId: session.tabId,
            pageSessionId: session.pageSessionId,
            tabUrl: session.pageUrl,
            sessionReason: reason
        });
    }

    ensurePageSession(tabId, pageUrl, reason, documentId = null) {
        let session = this.pageSessions.current(tabId);
        if (!session) {
            session = this.pageSessions.begin(tabId, pageUrl, documentId);
            this.announcePageSession(session, reason);
        }
        return session;
    }

    beginPageSession(tabId, pageUrl, reason, documentId = null) {
        this.forgetForwardedMedia(String(tabId));
        for (const [id, item] of this.browserDetectedMedia.entries()) {
            if (item.tabId === String(tabId)) this.browserDetectedMedia.delete(id);
        }
        const session = this.pageSessions.begin(tabId, pageUrl, documentId);
        this.announcePageSession(session, reason);
        this.updateActionIcon();
        this.sendMediaListToTab(tabId);
        return session;
    }

    onNavigationCommitted(details) {
        if (!details || details.frameId !== 0 || details.tabId === -1) {
            return;
        }
        this.beginPageSession(details.tabId, details.url || "", "navigation-commit", details.documentId || null);
    }

    onHistoryStateUpdated(details) {
        if (!details || details.frameId !== 0 || details.tabId === -1) {
            return;
        }
        const current = this.pageSessions.current(details.tabId);
        const url = details.url || "";
        if (!current || current.pageUrl !== url) {
            this.beginPageSession(details.tabId, url, "history-url-change", details.documentId || current?.documentId || null);
        }
    }

    onReferenceFragmentUpdated(details) {
        if (!details || details.frameId !== 0 || details.tabId === -1) {
            return;
        }
        const current = this.pageSessions.current(details.tabId);
        const withoutHash = value => String(value || "").split("#")[0];
        const hash = String(details.url || "").split("#").slice(1).join("#");
        if (current && withoutHash(current.pageUrl) === withoutHash(details.url) && !/^[/!]/.test(hash)) {
            return;
        }
        this.onHistoryStateUpdated(details);
    }

    onTabRemoved(tabId) {
        this.analysisList = this.analysisList.filter(item => String(item?.tabId ?? "") !== String(tabId));
        for (const [id, item] of this.browserDetectedMedia.entries()) {
            if (item.tabId === String(tabId)) this.browserDetectedMedia.delete(id);
        }
        this.pageSessions.close(tabId);
        this.tabTitles.delete(String(tabId));
        if (this.connector.isConnected()) {
            this.connector.postMessage("/tab-closed", { tabId: tabId + "" });
        }
        if (String(tabId) === this.activeTabId) {
            this.activeTabId = "-1";
            this.activeTabRevision++;
            this.updateActionIcon();
        }
    }

    onTabReplaced(addedTabId, removedTabId) {
        this.onTabRemoved(removedTabId);
        chrome.tabs.get(addedTabId, tab => {
            this.beginPageSession(addedTabId, tab?.url || "", "tab-replaced");
        });
    }

    onTabActivated(activeInfo) {
        this.activeTabId = activeInfo.tabId + "";
        this.activeTabRevision++;
        this.logger.log("Active tab: " + this.activeTabId);
        if (!this.pageSessions.current(activeInfo.tabId) && chrome.tabs && typeof chrome.tabs.get === "function") {
            chrome.tabs.get(activeInfo.tabId, tab => {
                this.ensurePageSession(activeInfo.tabId, tab?.url || "", "activation-bootstrap");
                this.updateActionIcon();
            });
        }
        this.updateActionIcon();
    }
}
