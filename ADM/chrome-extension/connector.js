"use strict";
import Logger from './logger.js';
import NativeProtocolTransport, { TAKEOVER_ACK_TIMEOUT_MS, COMMAND_RESPONSE_TIMEOUT_MS } from './native-protocol-transport.js';
import { NATIVE_HOST_NAME, OFFICIAL_EXTENSION_ID } from './product-identity.js';

const CONNECT_RETRY_MS = 250;
const CONNECT_TIMEOUT_MS = 5000;
const SYNC_ALARM = "adm-sync";
const SYNC_ALARM_MINUTES = 0.5;
const LIVE_SYNC_MS = 5000;
const LEGACY_ALARM_COUNT = 12;

function delay(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

export default class Connector {
    constructor(onMessage, onDisconnect) {
        this.logger = new Logger();
        this.onMessage = onMessage;
        this.onDisconnect = onDisconnect;
        this.connected = undefined;
        this.launchInFlight = null;
        this.nativeProtocol = new NativeProtocolTransport(() => {}, () => {});
    }

    connect() {
        const ignore = () => { void chrome.runtime?.lastError; };
        for (let i = 0; i < LEGACY_ALARM_COUNT; i++) {
            try { chrome.alarms.clear?.("alerm-" + i, ignore); } catch { }
        }
        chrome.alarms.create(SYNC_ALARM, { periodInMinutes: SYNC_ALARM_MINUTES });
        void this.onTimer();
    }

    scheduleLiveSync() {
        if (this.liveSyncTimer) return;
        this.liveSyncTimer = setTimeout(() => {
            this.liveSyncTimer = null;
            if (this.connected) void this.onTimer();
        }, LIVE_SYNC_MS);
        this.liveSyncTimer?.unref?.();
    }

    stopLiveSync() {
        if (!this.liveSyncTimer) return;
        clearTimeout(this.liveSyncTimer);
        this.liveSyncTimer = null;
    }

    registerAlarmListener() {
        if (this.alarmListener || !chrome.alarms?.onAlarm) return;
        this.alarmListener = () => { void this.onTimer(); };
        chrome.alarms.onAlarm.addListener(this.alarmListener);
    }

    async onTimer() {
        try {
            if (!await this.nativeProtocol.connect()) {
                this.disconnect(true);
                return null;
            }
            const response = await this.nativeProtocol.requestSyncState();
            const payload = response?.payload || {};
            if (payload.status !== "accepted" || !payload.config) {
                this.disconnect(true);
                return null;
            }
            this.connected = true;
            this.onMessage(payload.config);
            this.scheduleLiveSync();
            return payload.config;
        } catch {
            this.disconnect(true);
            return null;
        }
    }

    disconnect(closeTransport = false) {
        if (closeTransport) this.nativeProtocol.disconnect?.();
        this.stopLiveSync();
        this.connected = false;
        this.onDisconnect();
    }

    isConnected() {
        return this.connected;
    }

    postMessage(url, data) {
        const deadline = Date.now() + CONNECT_TIMEOUT_MS;
        return this.nativeProtocol.connectWithBackoff(deadline)
            .then(ready => {
                if (!ready) throw new Error("NativeProtocolUnavailable");
                return this.nativeProtocol.request(
                    "BrowserControlCommand",
                    { route: url, data: data || {} },
                    COMMAND_RESPONSE_TIMEOUT_MS
                );
            })
            .then(response => {
                const payload = response?.payload || {};
                if (payload.status !== "accepted" || !payload.config) return null;
                this.connected = true;
                this.onMessage(payload.config);
                return payload.config;
            }, error => {
                const code = String(error?.message || error);
                if (code === "NativeProtocolUnavailable" || code === "NativeProtocolDisconnected") this.disconnect(true);
                return null;
            })
            .catch(() => null);
    }

    async postMessageWithDesktop(url, data) {
        if (!this.isConnected()) {
            const ready = await this.launchApp();
            if (!ready) return null;
        }
        return this.postMessage(url, data);
    }

    async requestDownloadTakeover(payload) {
        const deadline = Date.now() + CONNECT_TIMEOUT_MS;
        if (!await this.nativeProtocol.connectWithBackoff(deadline)) throw new Error("NativeProtocolUnavailable");
        return this.nativeProtocol.request("DownloadTakeoverRequest", payload, TAKEOVER_ACK_TIMEOUT_MS);
    }

    async queryTakeoverOwnership(browserDownloadIdentity) {
        const deadline = Date.now() + CONNECT_TIMEOUT_MS;
        if (!await this.nativeProtocol.connectWithBackoff(deadline)) throw new Error("NativeProtocolUnavailable");
        return this.nativeProtocol.queryTakeoverOwnership(browserDownloadIdentity);
    }

    async submitSocialAnalysisSession(payload) {
        const deadline = Date.now() + CONNECT_TIMEOUT_MS;
        if (!await this.nativeProtocol.connectWithBackoff(deadline)) throw new Error("NativeProtocolUnavailable");
        return this.nativeProtocol.request("SocialAnalysisSessionMaterial", payload, COMMAND_RESPONSE_TIMEOUT_MS);
    }

    async launchNativeHost() {
        const deadline = Date.now() + CONNECT_TIMEOUT_MS;
        if (await this.nativeProtocol.connectWithBackoff(deadline)) {
            try {
                const response = await this.nativeProtocol.ensureDesktop();
                if (response?.payload?.status === "accepted") return true;
            } catch {
            }
            this.nativeProtocol.disconnect();
        }
        return this.launchLegacyNativeHost();
    }

    launchLegacyNativeHost() {
        return new Promise(resolve => {
            if (!chrome.runtime?.sendNativeMessage) {
                resolve(false);
                return;
            }
            let settled = false;
            const finish = value => {
                if (settled) return;
                settled = true;
                resolve(value);
            };
            const timer = setTimeout(() => finish(false), 1000);
            try {
                chrome.runtime.sendNativeMessage(NATIVE_HOST_NAME, { type: "launch" }, () => {
                    clearTimeout(timer);
                    const failed = Boolean(chrome.runtime.lastError);
                    void chrome.runtime.lastError;
                    finish(!failed);
                });
            } catch {
                clearTimeout(timer);
                finish(false);
            }
        });
    }

    launchProtocolFallback() {
        if (chrome.runtime?.id !== OFFICIAL_EXTENSION_ID) return Promise.resolve(false);
        return new Promise(resolve => {
            if (!chrome.tabs?.create) {
                resolve(false);
                return;
            }
            try {
                chrome.tabs.create({ url: "adm-app://launch", active: false }, () => {
                    const failed = Boolean(chrome.runtime?.lastError);
                    void chrome.runtime?.lastError;
                    resolve(!failed);
                });
            } catch {
                resolve(false);
            }
        });
    }

    launchApp() {
        if (this.isConnected()) return Promise.resolve(true);
        if (this.launchInFlight) return this.launchInFlight;

        this.launchInFlight = (async () => {
            if (await this.onTimer()) return true;
            const nativeStarted = await this.launchNativeHost();
            if (!nativeStarted) await this.launchProtocolFallback();

            const deadline = Date.now() + CONNECT_TIMEOUT_MS;
            while (Date.now() < deadline) {
                if (await this.onTimer()) return true;
                await delay(CONNECT_RETRY_MS);
            }
            return false;
        })().finally(() => {
            this.launchInFlight = null;
        });
        return this.launchInFlight;
    }
}
