"use strict";

const APP_BASE_URL = "http://127.0.0.1:8597";
const REQUEST_TIMEOUT_MS = 5000;

class Connector {
    constructor(onMessage, onDisconnect) {
        this.logger = new Logger();
        this.onMessage = onMessage;
        this.onDisconnect = onDisconnect;
        this.connected = undefined;
        this.syncInFlight = false;
        this.timer = undefined;
    }

    connect() {
        if (this.timer) return;
        this.onTimer();
        this.timer = setInterval(this.onTimer.bind(this), 5000);
    }

    async request(path, options = {}) {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
        try {
            const response = await fetch(APP_BASE_URL + path, {
                ...options,
                signal: controller.signal,
                cache: "no-store",
                credentials: "omit"
            });
            if (!response.ok) throw new Error("ADM local control returned HTTP " + response.status);
            const json = await response.json();
            this.connected = true;
            this.onMessage(json);
            return true;
        } finally {
            clearTimeout(timeout);
        }
    }

    async onTimer() {
        if (this.syncInFlight) return;
        this.syncInFlight = true;
        try {
            await this.request("/sync");
        } catch (_) {
            this.disconnect();
        } finally {
            this.syncInFlight = false;
        }
    }

    disconnect() {
        const changed = this.connected !== false;
        this.connected = false;
        if (changed) this.onDisconnect();
    }

    isConnected() {
        return this.connected === true;
    }

    async postMessage(url, data) {
        try {
            await this.request(url, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(data)
            });
        } catch (_) {
            this.disconnect();
        }
    }

    launchApp() {
    }
}
