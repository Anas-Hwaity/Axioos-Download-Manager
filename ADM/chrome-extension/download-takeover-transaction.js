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
      await this.resumeOrRetain(download.id);
      return { accepted: false, reason: "TransportFailure" };
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

  async finishOwned(browserDownloadId, desktopDownloadId) {
    await this.orphanStore.markDurableAccepted(browserDownloadId, desktopDownloadId);
    const cancelled = await callDownload(this.downloads, "cancel", browserDownloadId);
    if (!cancelled) {
      return { accepted: true, reason: "BrowserCancelPending", browserCleanupPending: true, desktopDownloadId };
    }
    await eraseDownload(this.downloads, browserDownloadId);
    await this.orphanStore.clear(browserDownloadId);
    return { accepted: true, desktopDownloadId };
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
