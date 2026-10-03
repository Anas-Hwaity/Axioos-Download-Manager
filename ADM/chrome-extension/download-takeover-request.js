"use strict";

export function buildDownloadTakeoverPayload(download, browserDownloadIdentity, pageSessionId = null) {
  if (!download || !Number.isInteger(download.id) || download.id < 0) {
    throw new Error("InvalidBrowserDownloadId");
  }
  if (!browserDownloadIdentity) {
    throw new Error("MissingBrowserDownloadIdentity");
  }
  const url = download.url || download.finalUrl;
  if (!url) {
    throw new Error("MissingDownloadUrl");
  }
  return {
    browserDownloadId: download.id,
    browserDownloadIdentity: String(browserDownloadIdentity),
    url,
    finalUrl: download.finalUrl || null,
    filename: download.filename || null,
    referrer: download.referrer || null,
    mime: download.mime || null,
    totalSize: Number.isFinite(download.totalBytes) ? download.totalBytes : null,
    incognito: Boolean(download.incognito),
    pageSessionId: pageSessionId || null,
  };
}
