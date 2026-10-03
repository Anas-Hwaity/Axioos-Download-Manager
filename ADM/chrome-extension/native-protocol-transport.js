"use strict";

import { NATIVE_HOST_NAME } from './product-identity.js';
export { NATIVE_HOST_NAME };
export const PROTOCOL_VERSION = 1;
export const MAX_APPLICATION_MESSAGE_BYTES = 512 * 1024;
export const HANDSHAKE_TIMEOUT_MS = 3000;
export const TAKEOVER_ACK_TIMEOUT_MS = 5000;
export const COMMAND_RESPONSE_TIMEOUT_MS = 10000;
export const RECONNECT_BACKOFF_MS = Object.freeze([250, 500, 1000, 2000, 5000]);

function defaultMessageId() {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
  const hex = "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx";
  return hex.replace(/[xy]/g, ch => {
    const r = Math.floor(Math.random() * 16);
    const v = ch === "x" ? r : ((r & 0x3) | 0x8);
    return v.toString(16);
  });
}

function utcNow() {
  return new Date().toISOString();
}

function encodedSize(value) {
  return new TextEncoder().encode(JSON.stringify(value)).byteLength;
}

export default class NativeProtocolTransport {
  constructor(onMessage = () => {}, onDisconnect = () => {}, options = {}) {
    this.onMessage = onMessage;
    this.onDisconnect = onDisconnect;
    this.hostName = options.hostName || NATIVE_HOST_NAME;
    this.idFactory = options.idFactory || defaultMessageId;
    this.handshakeTimeoutMs = options.handshakeTimeoutMs || HANDSHAKE_TIMEOUT_MS;
    this.reconnectDelay = options.reconnectDelay || (ms => new Promise(resolve => setTimeout(resolve, ms)));
    this.reconnectJitter = options.reconnectJitter || (ms => Math.floor(Math.random() * Math.max(1, Math.floor(ms * 0.2))));
    this.port = null;
    this.sessionId = null;
    this.pendingHandshake = null;
    this.pendingRequests = new Map();
  }

  isConnected() {
    return Boolean(this.port && this.sessionId);
  }

  connect() {
    if (this.isConnected()) return Promise.resolve(true);
    if (this.pendingHandshake) return this.pendingHandshake.promise;
    if (!globalThis.chrome?.runtime?.connectNative) return Promise.resolve(false);

    let port;
    try {
      port = chrome.runtime.connectNative(this.hostName);
    } catch {
      return Promise.resolve(false);
    }
    if (!port) return Promise.resolve(false);

    this.port = port;
    port.onMessage.addListener(message => {
      if (this.port === port) this.handleMessage(message);
    });
    port.onDisconnect.addListener(() => {
      if (this.port === port) this.handleDisconnect();
    });

    const messageId = this.idFactory();
    let resolveHandshake;
    const promise = new Promise(resolve => { resolveHandshake = resolve; });
    const timer = setTimeout(() => this.finishHandshake(false), this.handshakeTimeoutMs);
    this.pendingHandshake = { messageId, resolve: resolveHandshake, timer, promise };

    const hello = {
      protocolVersion: PROTOCOL_VERSION,
      messageId,
      sessionId: null,
      type: "Hello",
      sentAtUtc: utcNow(),
      replyTo: null,
      payload: {
        supportedProtocolVersions: [PROTOCOL_VERSION],
        extensionVersion: chrome.runtime.getManifest?.().version || "unknown",
        browserFamily: "chromium",
        capabilities: [],
      },
    };

    if (encodedSize(hello) > MAX_APPLICATION_MESSAGE_BYTES) {
      this.finishHandshake(false);
      return promise;
    }

    try {
      port.postMessage(hello);
    } catch {
      this.finishHandshake(false);
    }
    return promise;
  }

  async connectWithBackoff(deadlineMs = Date.now() + 5000) {
    if (await this.connect()) return true;
    for (const baseDelay of RECONNECT_BACKOFF_MS) {
      const remaining = deadlineMs - Date.now();
      if (remaining <= 0) break;
      const waitMs = Math.min(remaining, baseDelay + this.reconnectJitter(baseDelay));
      if (waitMs > 0) await this.reconnectDelay(waitMs);
      if (Date.now() >= deadlineMs) break;
      if (await this.connect()) return true;
    }
    return false;
  }

  disconnect() {
    const port = this.port;
    this.port = null;
    this.sessionId = null;
    this.finishHandshake(false);
    this.failPendingRequests("NativeProtocolDisconnected");
    try { port?.disconnect(); } catch {}
  }

  createMessage(type, payload = {}) {
    if (!this.isConnected()) throw new Error("Native protocol transport is not connected");
    const message = {
      protocolVersion: PROTOCOL_VERSION,
      messageId: this.idFactory(),
      sessionId: this.sessionId,
      type,
      sentAtUtc: utcNow(),
      replyTo: null,
      payload,
    };
    if (encodedSize(message) > MAX_APPLICATION_MESSAGE_BYTES) {
      throw new Error("PayloadTooLarge");
    }
    return message;
  }

  send(type, payload = {}) {
    const message = this.createMessage(type, payload);
    this.port.postMessage(message);
    return message.messageId;
  }

  queryTakeoverOwnership(browserDownloadIdentity) {
    if (!browserDownloadIdentity) return Promise.reject(new Error("MissingBrowserDownloadIdentity"));
    return this.request("QueryTakeoverOwnership", { browserDownloadIdentity });
  }

  requestSyncState() {
    return this.requestIdempotent("SyncState", {}, COMMAND_RESPONSE_TIMEOUT_MS);
  }

  ensureDesktop() {
    return this.requestIdempotent("EnsureDesktop", {}, COMMAND_RESPONSE_TIMEOUT_MS);
  }

  async requestIdempotent(type, payload = {}, timeoutMs = COMMAND_RESPONSE_TIMEOUT_MS) {
    const run = async () => {
      if (!this.isConnected()) {
        const ready = await this.connectWithBackoff(Date.now() + Math.min(timeoutMs, 5000));
        if (!ready) throw new Error("NativeProtocolUnavailable");
      }
      return this.request(type, payload, timeoutMs);
    };
    try {
      return await run();
    } catch (error) {
      if (String(error) !== "Error: NativeProtocolDisconnected") throw error;
      this.disconnect();
      return run();
    }
  }

  request(type, payload = {}, timeoutMs = COMMAND_RESPONSE_TIMEOUT_MS) {
    const message = this.createMessage(type, payload);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pendingRequests.delete(message.messageId);
        reject(new Error("NativeProtocolTimeout"));
      }, timeoutMs);
      this.pendingRequests.set(message.messageId, { resolve, reject, timer });
      try {
        this.port.postMessage(message);
      } catch (error) {
        clearTimeout(timer);
        this.pendingRequests.delete(message.messageId);
        reject(error);
      }
    });
  }

  handleMessage(message) {
    if (!message || message.protocolVersion !== PROTOCOL_VERSION) return;
    if (encodedSize(message) > MAX_APPLICATION_MESSAGE_BYTES) {
      this.disconnect();
      return;
    }

    const pending = this.pendingHandshake;
    if (pending && message.type === "HelloAck" && message.replyTo === pending.messageId && message.sessionId) {
      this.sessionId = message.sessionId;
      this.finishHandshake(true);
      return;
    }

    if (!this.isConnected() || message.sessionId !== this.sessionId) return;

    if (message.replyTo && this.pendingRequests.has(message.replyTo)) {
      const pendingRequest = this.pendingRequests.get(message.replyTo);
      this.pendingRequests.delete(message.replyTo);
      clearTimeout(pendingRequest.timer);
      pendingRequest.resolve(message);
      return;
    }

    this.onMessage(message);
  }

  handleDisconnect() {
    this.port = null;
    this.sessionId = null;
    this.finishHandshake(false);
    this.failPendingRequests("NativeProtocolDisconnected");
    this.onDisconnect();
  }

  failPendingRequests(code) {
    for (const pendingRequest of this.pendingRequests.values()) {
      clearTimeout(pendingRequest.timer);
      pendingRequest.reject(new Error(code));
    }
    this.pendingRequests.clear();
  }

  finishHandshake(ok) {
    const pending = this.pendingHandshake;
    if (!pending) return;
    this.pendingHandshake = null;
    clearTimeout(pending.timer);
    if (!ok) {
      this.sessionId = null;
      const port = this.port;
      this.port = null;
      try { port?.disconnect(); } catch {}
    }
    pending.resolve(Boolean(ok));
  }
}
