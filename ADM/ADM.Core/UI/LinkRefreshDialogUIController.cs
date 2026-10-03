using System;
using System.Collections.Generic;
using System.Linq;
using TraceLog;
using ADM.Core;
using ADM.Core.Downloader;
using ADM.Core.Downloader.Progressive;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;
using ADM.Core.IO;
using ADM.Core.Util;

namespace ADM.Core.UI
{
    public static class LinkRefreshDialogUIController
    {
        public static bool RefreshLink(DownloadItemBase item, IRefreshLinkDialog dialog, ILinkRefresher linkRefresher)
        {
            HTTPDownloaderBase? watchedDownloader = null;
            try
            {
                if (item.DownloadType != "Http" && item.DownloadType != "Dash") return false;

                string? referer;
                if (item.DownloadType == "Http")
                {
                    var state = DownloadStateIO.LoadSingleSourceHTTPDownloaderState(item.Id);
                    referer = GetReferer(state.Headers);
                    watchedDownloader = new SingleSourceHTTPDownloader(item.Id);
                }
                else
                {
                    var state = DownloadStateIO.LoadDualSourceHTTPDownloaderState(item.Id);
                    referer = GetReferer(state.Headers1);
                    watchedDownloader = new DualSourceHTTPDownloader(item.Id);
                }

                Log.Debug("Referer: " + SensitiveDataRedactor.UrlForLog(referer));
                watchedDownloader.RestoreState();
                var owner = watchedDownloader;
                EventHandler refreshedHandler = (_, _) => dialog.LinkReceived();
                if (!linkRefresher.TryAddToWatchList(owner, refreshedHandler)) return false;

                dialog.WatchingStopped += (_, _) => linkRefresher.ClearWatchList(owner);

                if (referer != null) OpenBrowser(referer);
                dialog.ShowWindow();
                return true;
            }
            catch (Exception e)
            {
                if (watchedDownloader != null) linkRefresher.ClearWatchList(watchedDownloader);
                Log.Debug(e, e.Message);
                return false;
            }
        }

        private static string GetReferer(Dictionary<string, List<string>> headers)
        {
            return headers?.Where(header => header.Key.ToLowerInvariant() == "referer").FirstOrDefault().Value?.FirstOrDefault();
        }

        private static void OpenBrowser(string url)
        {
            PlatformHelper.OpenBrowser(url);
        }
    }
}
