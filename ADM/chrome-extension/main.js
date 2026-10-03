"use strict";
import App from './app.js';

const app = new App();

globalThis.__admStage05NativeRequest = async (type, payload = {}, timeoutMs = 10000) => {
    try {
        const transport = app.connector?.nativeProtocol;
        if (!transport) return { ok: false, error: "NativeProtocolTransportUnavailable" };
        const connected = await transport.connectWithBackoff(Date.now() + 5000);
        if (!connected) return { ok: false, error: "NativeProtocolUnavailable" };
        let response;
        if (type === "SyncState") response = await transport.requestSyncState();
        else if (type === "EnsureDesktop") response = await transport.ensureDesktop();
        else response = await transport.request(type, payload || {}, timeoutMs);
        return { ok: true, response };
    } catch (error) {
        return { ok: false, error: String(error) };
    }
};

app.start();
