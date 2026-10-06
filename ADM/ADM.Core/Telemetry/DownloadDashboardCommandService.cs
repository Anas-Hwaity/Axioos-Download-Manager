using System;
using System.Collections.Generic;
using ADM.Core.DataAccess;
using ADM.Core.Downloader;

namespace ADM.Core.Telemetry
{
    public enum DownloadDashboardCommandResult
    {
        Accepted,
        NotFound,
        NotInProgress,
        Unavailable
    }

    public interface IDownloadDashboardCommandService
    {
        DownloadDashboardCommandResult Pause(string downloadId);
        DownloadDashboardCommandResult Resume(string downloadId);
        DownloadDashboardCommandResult Retry(string downloadId);
        DownloadDashboardCommandResult Cancel(string downloadId, DownloadCancellationPolicy policy = DownloadCancellationPolicy.RetainPartial);
        DownloadDashboardCommandResult RefreshSource(string downloadId);
        DownloadDashboardCommandResult ShowProgress(string downloadId);
    }

    public sealed class DownloadDashboardCommandService : IDownloadDashboardCommandService
    {
        private readonly IApplicationCore core;
        private readonly IPlatformUIService platformUiService;
        private readonly Func<string, DownloadItemBase?> lookup;

        public DownloadDashboardCommandService(
            IApplicationCore core,
            IPlatformUIService platformUiService,
            Func<string, DownloadItemBase?>? lookup = null)
        {
            this.core = core ?? throw new ArgumentNullException(nameof(core));
            this.platformUiService = platformUiService ?? throw new ArgumentNullException(nameof(platformUiService));
            this.lookup = lookup ?? (id => AppDB.Instance.Downloads.GetDownloadById(id));
        }

        public DownloadDashboardCommandResult Pause(string downloadId)
        {
            var entry = GetInProgress(downloadId, out var failure);
            if (entry == null) return failure;
            core.PauseDownloads(new[] { entry.Id }, false);
            return DownloadDashboardCommandResult.Accepted;
        }

        public DownloadDashboardCommandResult Resume(string downloadId)
        {
            var entry = GetInProgress(downloadId, out var failure);
            if (entry == null) return failure;
            if (entry.Status == DownloadStatus.Cancelled || entry.Status == DownloadStatus.Cancelling) return DownloadDashboardCommandResult.Unavailable;
            core.ResumeDownload(new Dictionary<string, DownloadItemBase> { [entry.Id] = entry });
            return DownloadDashboardCommandResult.Accepted;
        }

        public DownloadDashboardCommandResult Retry(string downloadId)
        {
            var entry = GetInProgress(downloadId, out var failure);
            if (entry == null) return failure;
            return core.RestartDownload(entry)
                ? DownloadDashboardCommandResult.Accepted
                : DownloadDashboardCommandResult.Unavailable;
        }

        public DownloadDashboardCommandResult Cancel(string downloadId, DownloadCancellationPolicy policy = DownloadCancellationPolicy.RetainPartial)
        {
            var entry = GetInProgress(downloadId, out var failure);
            if (entry == null) return failure;
            return core.CancelDownload(entry.Id, policy)
                ? DownloadDashboardCommandResult.Accepted
                : DownloadDashboardCommandResult.Unavailable;
        }

        public DownloadDashboardCommandResult RefreshSource(string downloadId)
        {
            var entry = GetInProgress(downloadId, out var failure);
            if (entry == null) return failure;
            platformUiService.ShowRefreshLinkDialog(entry);
            return DownloadDashboardCommandResult.Accepted;
        }

        public DownloadDashboardCommandResult ShowProgress(string downloadId)
        {
            var entry = GetInProgress(downloadId, out var failure);
            if (entry == null) return failure;
            if (!core.IsDownloadActive(entry.Id)) return DownloadDashboardCommandResult.Unavailable;
            core.ShowProgressWindow(entry.Id);
            return DownloadDashboardCommandResult.Accepted;
        }

        private InProgressDownloadItem? GetInProgress(string downloadId, out DownloadDashboardCommandResult failure)
        {
            failure = DownloadDashboardCommandResult.NotFound;
            if (string.IsNullOrWhiteSpace(downloadId)) return null;
            var entry = lookup(downloadId);
            if (entry == null) return null;
            if (entry is InProgressDownloadItem inProgress)
            {
                failure = DownloadDashboardCommandResult.Accepted;
                return inProgress;
            }
            failure = DownloadDashboardCommandResult.NotInProgress;
            return null;
        }
    }
}
