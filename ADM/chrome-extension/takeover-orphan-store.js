"use strict";

const STORAGE_KEY = "adm.takeover.paused.v1";

function callStorageGet(storage, key) {
  return new Promise((resolve, reject) => {
    try {
      storage.get(key, result => {
        if (globalThis.chrome?.runtime?.lastError) {
          reject(new Error(chrome.runtime.lastError.message || "StorageReadFailed"));
          return;
        }
        resolve(result?.[key] || {});
      });
    } catch (error) { reject(error); }
  });
}

function callStorageSet(storage, value) {
  return new Promise((resolve, reject) => {
    try {
      storage.set(value, () => {
        if (globalThis.chrome?.runtime?.lastError) {
          reject(new Error(chrome.runtime.lastError.message || "StorageWriteFailed"));
          return;
        }
        resolve();
      });
    } catch (error) { reject(error); }
  });
}

function resumeDownload(downloads, id) {
  return new Promise(resolve => {
    try {
      downloads.resume(id, () => resolve(!globalThis.chrome?.runtime?.lastError));
    } catch { resolve(false); }
  });
}

function cancelDownload(downloads, id) {
  return new Promise(resolve => {
    try {
      downloads.cancel(id, () => resolve(!globalThis.chrome?.runtime?.lastError));
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

export default class TakeoverOrphanStore {
  constructor(storage = globalThis.chrome?.storage?.local, downloads = globalThis.chrome?.downloads) {
    this.storage = storage;
    this.downloads = downloads;
    this.queue = Promise.resolve();
  }

  update(mutator) {
    const run = this.queue.then(async () => {
      const records = await this.read();
      const result = mutator(records);
      await this.write(records);
      return result;
    });
    this.queue = run.catch(() => {});
    return run;
  }

  async read() {
    if (!this.storage) return {};
    return callStorageGet(this.storage, STORAGE_KEY);
  }

  async write(records) {
    if (!this.storage) return;
    await callStorageSet(this.storage, { [STORAGE_KEY]: records });
  }

  async markPaused(browserDownloadId, idempotencyKey) {
    if (!Number.isInteger(browserDownloadId) || browserDownloadId < 0 || !idempotencyKey) {
      throw new Error("InvalidTakeoverOrphan");
    }
    await this.update(records => {
      records[String(browserDownloadId)] = {
        browserDownloadId,
        idempotencyKey: String(idempotencyKey),
        durableAccepted: false,
        desktopDownloadId: null,
        pausedAtUtc: new Date().toISOString(),
      };
    });
  }

  async markDurableAccepted(browserDownloadId, desktopDownloadId) {
    await this.update(records => {
      const key = String(browserDownloadId);
      const record = records[key];
      if (!record) throw new Error("TakeoverOrphanNotFound");
      record.durableAccepted = true;
      record.desktopDownloadId = desktopDownloadId || null;
      record.acceptedAtUtc = new Date().toISOString();
    });
  }

  async markAwaitingConfirmation(browserDownloadId) {
    await this.update(records => {
      const record = records[String(browserDownloadId)];
      if (!record) throw new Error("TakeoverOrphanNotFound");
      record.awaitingConfirmation = true;
    });
  }

  async clear(browserDownloadId) {
    await this.update(records => {
      delete records[String(browserDownloadId)];
    });
  }

  async recoverOrphans(queryOwnership = null) {
    const records = await this.read();
    if (!this.downloads) return { resumed: 0, retained: Object.keys(records).length, awaiting: [] };
    const original = new Set(Object.keys(records));
    let resumed = 0;
    let cancelled = 0;
    let changed = false;
    const awaiting = [];
    for (const [key, record] of Object.entries(records)) {
      const id = Number(record?.browserDownloadId);
      if (!Number.isInteger(id) || id < 0) continue;
      let waiting = false;
      if (record?.durableAccepted !== true && queryOwnership) {
        try {
          const ownership = await queryOwnership(record.idempotencyKey);
          const result = ownership?.payload || {};
          const owned = ((result.status === "accepted" || result.status === "duplicate") || result.code === "DurableAccepted") &&
            result.desktopDownloadId && !String(result.desktopDownloadId).startsWith("prompt-");
          if (owned) {
            record.durableAccepted = true;
            record.desktopDownloadId = result.desktopDownloadId;
            record.acceptedAtUtc = new Date().toISOString();
            changed = true;
          } else if (result.status === "busy" && (result.code === "OwnershipPending" || result.code === "ConfirmationRequired")) {
            waiting = true;
          }
        } catch { }
      }
      if (waiting) {
        awaiting.push({ browserDownloadId: id, idempotencyKey: record.idempotencyKey });
        continue;
      }
      if (record?.durableAccepted === true) {
        if (await cancelDownload(this.downloads, id)) {
          await eraseDownload(this.downloads, id);
          delete records[key];
          cancelled += 1;
          changed = true;
        }
        continue;
      }
      if (await resumeDownload(this.downloads, id)) {
        delete records[key];
        resumed += 1;
        changed = true;
      }
    }
    if (changed) {
      await this.update(current => {
        for (const key of original) {
          if (!Object.prototype.hasOwnProperty.call(records, key)) delete current[key];
          else if (records[key]?.durableAccepted === true) current[key] = records[key];
        }
      });
    }
    return { resumed, cancelled, retained: Object.keys(records).length, awaiting };
  }
}

export { STORAGE_KEY };
