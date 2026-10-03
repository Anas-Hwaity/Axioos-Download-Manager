using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace ADM.Core.UI
{
    public interface IProgressWindowRuntimeContext
    {
        IApplicationCore CoreService { get; }
        IApplication Application { get; }
        IPlatformUIService PlatformUIService { get; }
        bool EnableSpeedLimit { get; }
        int DefaultDownloadSpeed { get; }
        void SubscribeApplicationEvent(EventHandler<ApplicationEvent> handler);
        void UnsubscribeApplicationEvent(EventHandler<ApplicationEvent> handler);
    }

    public interface IProgressWindow
    {
        public string FileNameText { get; set; }

        public string UrlText { get; set; }

        public string FileSizeText { get; set; }

        public string DownloadSpeedText { get; set; }

        public string DownloadETAText { get; set; }

        public int DownloadProgress { get; set; }

        public string DownloadId { get; set; }

        public void ShowProgressWindow();

        public void DownloadCancelled();

        public void DownloadFailed(ErrorDetails error);

        public void DownloadStarted();

        public void DestroyWindow();
    }

    public struct ErrorDetails
    {
        public int Code { get; set; }
        public string Message { get; set; }
    }
}
