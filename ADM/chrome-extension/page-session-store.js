"use strict";

const STORAGE_KEY = "adm.pageSessions.v1";

function validTabId(tabId) {
    return (typeof tabId === "number" && Number.isSafeInteger(tabId) && tabId >= 0) ||
        (typeof tabId === "string" && /^(0|[1-9][0-9]*)$/.test(tabId));
}

function defaultIdFactory() {
    if (globalThis.crypto && typeof globalThis.crypto.randomUUID === "function") {
        return globalThis.crypto.randomUUID();
    }
    throw new Error("Secure page-session UUID generation is unavailable");
}

export default class PageSessionStore {
    constructor(storageArea = globalThis.chrome?.storage?.session, idFactory = defaultIdFactory, onPersistenceError = null) {
        this.storageArea = storageArea;
        this.idFactory = idFactory;
        this.onPersistenceError = onPersistenceError;
        this.sessions = new Map();
        this.persistChain = Promise.resolve();
    }

    async restore() {
        if (!this.storageArea || typeof this.storageArea.get !== "function") {
            return;
        }
        const stored = await this.storageArea.get(STORAGE_KEY);
        const items = stored && stored[STORAGE_KEY];
        if (!Array.isArray(items)) {
            return;
        }
        for (const item of items) {
            if (!item || !validTabId(item.tabId) || typeof item.pageSessionId !== "string" ||
                item.pageSessionId.length < 8 || this.sessions.has(String(item.tabId))) {
                continue;
            }
            this.sessions.set(String(item.tabId), {
                tabId: String(item.tabId),
                pageSessionId: item.pageSessionId,
                pageUrl: typeof item.pageUrl === "string" ? item.pageUrl : "",
                documentId: typeof item.documentId === "string" && item.documentId.length > 0
                    ? item.documentId : null
            });
        }
    }

    current(tabId) {
        if (!validTabId(tabId)) {
            return null;
        }
        return this.sessions.get(String(tabId)) || null;
    }

    all() {
        return [...this.sessions.values()].map(item => ({ ...item }));
    }

    begin(tabId, pageUrl = "", documentId = null) {
        if (!validTabId(tabId)) {
            return null;
        }
        const item = {
            tabId: String(tabId),
            pageSessionId: this.idFactory(),
            pageUrl: typeof pageUrl === "string" ? pageUrl : "",
            documentId: typeof documentId === "string" && documentId.length > 0
                ? documentId : null
        };
        this.sessions.set(item.tabId, item);
        this.persistSafely();
        return item;
    }

    ensure(tabId, pageUrl = "", documentId = null) {
        const current = this.current(tabId);
        if (current) {
            return current;
        }
        return this.begin(tabId, pageUrl, documentId);
    }

    adoptDocument(tabId, documentId) {
        const current = this.current(tabId);
        if (!current || current.documentId || typeof documentId !== "string" || documentId.length === 0) {
            return current;
        }
        current.documentId = documentId;
        this.persistSafely();
        return current;
    }

    close(tabId) {
        if (!validTabId(tabId)) {
            return false;
        }
        const removed = this.sessions.delete(String(tabId));
        if (removed) {
            this.persistSafely();
        }
        return removed;
    }

    persistSafely() {
        if (!this.storageArea || typeof this.storageArea.set !== "function") {
            return;
        }
        const snapshot = [...this.sessions.values()].map(item => ({ ...item }));
        this.persistChain = this.persistChain
            .then(() => this.storageArea.set({ [STORAGE_KEY]: snapshot }))
            .catch(error => {
                if (this.onPersistenceError) {
                    this.onPersistenceError(error);
                }
            });
        return this.persistChain;
    }
}

export { STORAGE_KEY, validTabId };
