"use strict";

function callDownload(downloads, method, id) {
  return new Promise(resolve => {
    try {
      downloads[method](id, () => resolve(!globalThis.chrome?.runtime?.lastError));
    } catch { resolve(false); }
  });
}

function eraseDownload(downloads, id) {
  return new Promise(resolve => {
    try {
      downloads.erase({ id }, () => resolve(!globalThis.chrome?.runtime?.lastError));
    } catch { resolve(false); }
  });
}

export function browserDownloadIdentity(download) {
  if (!download || !Number.isInteger(download.id) || download.id < 0) throw new Error("InvalidBrowserDownloadId");
  const started = download.startTime || "unknown-start";
  const url = download.finalUrl || download.url || "unknown-url";
  return `chromium:${download.id}:${started}:${url}`;
}

const CONFIRMATION_POLL_MS = 1000;
const CONFIRMATION_MAX_FAILURES = 3;
const CANCEL_RETRY_DELAYS_MS = [250, 750, 2000, 5000];

function searchDownload(downloads, id) {
  return new Promise(resolve => {
    if (typeof downloads?.search !== "function") { resolve(undefined); return; }
    try {
      downloads.search({ id }, items => {
        if (globalThis.chrome?.runtime?.lastError || !Array.isArray(items)) { resolve(undefined); return; }
        resolve(items[0] || null);
      });
    } catch { resolve(undefined); }
  });
}

export function ownedDesktopDownloadId(result) {
  if (!result || (result.status !== "accepted" && result.status !== "duplicate")) return null;
  const id = result.desktopDownloadId;
  if (!id || String(id).startsWith("prompt-")) return null;
  return String(id);
}

export function awaitsDesktopConfirmation(result) {
  return result?.status === "busy" && (result.code === "ConfirmationRequired" || result.code === "OwnershipPending");
}

export default class DownloadTakeoverTransaction {
  constructor(downloads, orphanStore, connector, options = {}) {
    this.downloads = downloads;
    this.orphanStore = orphanStore;
    this.connector = connector;
    this.enrich = options.enrich || null;
    this.pollMs = options.pollMs || CONFIRMATION_POLL_MS;
    this.cancelRetryDelays = Array.isArray(options.cancelRetryDelays) ? options.cancelRetryDelays : CANCEL_RETRY_DELAYS_MS;
    this.delay = options.delay || (ms => new Promise(resolve => setTimeout(resolve, ms)));
  }

  async execute(download, payload) {
    const identity = payload.browserDownloadIdentity;
    if (!await callDownload(this.downloads, "pause", download.id)) {
      return { accepted: false, reason: "PauseFailed" };
    }

    await this.orphanStore.markPaused(download.id, identity);
    if (this.enrich) {
      try {
        Object.assign(payload, await this.enrich(download));
      } catch { }
    }
    let response;
    try {
      response = await this.connector.requestDownloadTakeover(payload);
    } catch {
      return this.resolveAfterTransportFailure(download.id, identity);
    }

    const result = response?.payload || {};
    const desktopDownloadId = ownedDesktopDownloadId(result);
    if (desktopDownloadId) return this.finishOwned(download.id, desktopDownloadId);
    if (awaitsDesktopConfirmation(result)) {
      if (typeof this.orphanStore.markAwaitingConfirmation === "function") {
        await this.orphanStore.markAwaitingConfirmation(download.id);
      }
      return this.waitForConfirmation(download.id, identity);
    }
    if (result.code === "DesktopUnavailable") return this.resolveAfterTransportFailure(download.id, identity);
    await this.resumeOrRetain(download.id);
    return { accepted: false, reason: result.code || "TakeoverRejected" };
  }

  async waitForConfirmation(browserDownloadId, identity) {
    let failures = 0;
    for (;;) {
      await this.delay(this.pollMs);
      let result = null;
      try {
        result = (await this.connector.queryTakeoverOwnership(identity))?.payload || {};
      } catch { }
      if (result === null || result.code === "DesktopUnavailable") {
        failures += 1;
        if (failures < CONFIRMATION_MAX_FAILURES) continue;
        await this.resumeOrRetain(browserDownloadId);
        return { accepted: false, reason: "TransportFailure" };
      }
      failures = 0;
      const desktopDownloadId = ownedDesktopDownloadId(result);
      if (desktopDownloadId) return this.finishOwned(browserDownloadId, desktopDownloadId);
      if (awaitsDesktopConfirmation(result)) continue;
      await this.resumeOrRetain(browserDownloadId);
      return { accepted: false, reason: result.code || "TakeoverDeclined" };
    }
  }

  async resolveAfterTransportFailure(browserDownloadId, identity) {
    if (typeof this.connector?.queryTakeoverOwnership === "function") {
      let result = null;
      try {
        result = (await this.connector.queryTakeoverOwnership(identity))?.payload || {};
      } catch { }
      if (result === null) {
        if (typeof this.orphanStore.markAwaitingConfirmation === "function") {
          await this.orphanStore.markAwaitingConfirmation(browserDownloadId);
        }
        return this.waitForConfirmation(browserDownloadId, identity);
      }
      if (result.code !== "DesktopUnavailable") {
        const desktopDownloadId = ownedDesktopDownloadId(result);
        if (desktopDownloadId) return this.finishOwned(browserDownloadId, desktopDownloadId);
        if (awaitsDesktopConfirmation(result)) {
          if (typeof this.orphanStore.markAwaitingConfirmation === "function") {
            await this.orphanStore.markAwaitingConfirmation(browserDownloadId);
          }
          return this.waitForConfirmation(browserDownloadId, identity);
        }
      }
    }
    await this.resumeOrRetain(browserDownloadId);
    return { accepted: false, reason: "TransportFailure" };
  }

  async finishOwned(browserDownloadId, desktopDownloadId) {
    await this.orphanStore.markDurableAccepted(browserDownloadId, desktopDownloadId);
    if (!await this.completeBrowserCleanup(browserDownloadId)) {
      return { accepted: true, reason: "BrowserCancelPending", browserCleanupPending: true, browserDownloadId, desktopDownloadId };
    }
    return { accepted: true, desktopDownloadId };
  }

  async completeBrowserCleanup(browserDownloadId) {
    if (!await this.cancelUntilStopped(browserDownloadId)) return false;
    await eraseDownload(this.downloads, browserDownloadId);
    await this.orphanStore.clear(browserDownloadId);
    return true;
  }

  async cancelUntilStopped(browserDownloadId) {
    for (let attempt = 0; ; attempt += 1) {
      const cancelled = await callDownload(this.downloads, "cancel", browserDownloadId);
      const current = await searchDownload(this.downloads, browserDownloadId);
      if (current === null) return true;
      if (current && current.state !== "in_progress") return true;
      if (current === undefined && cancelled) return true;
      if (attempt >= this.cancelRetryDelays.length) return false;
      await this.delay(this.cancelRetryDelays[attempt]);
    }
  }

  async resumeOrRetain(browserDownloadId) {
    if (await callDownload(this.downloads, "resume", browserDownloadId)) {
      await this.orphanStore.clear(browserDownloadId);
      return true;
    }
    const current = await searchDownload(this.downloads, browserDownloadId);
    if (current === null || (current && current.state !== "in_progress")) {
      await this.orphanStore.clear(browserDownloadId);
    }
    return false;
  }
}
