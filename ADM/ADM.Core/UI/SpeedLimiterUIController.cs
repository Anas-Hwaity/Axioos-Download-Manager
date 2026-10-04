using System;
using ADM.Core.Downloader;

namespace ADM.Core.UI
{
    public static class SpeedLimiterUIController
    {
        public static void Run(ISpeedLimiterWindow window, IApplicationRuntimeContext runtimeContext)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (runtimeContext == null) throw new ArgumentNullException(nameof(runtimeContext));
            EventHandler? okClicked = null;
            okClicked = (sender, _) =>
            {
                var dialog = sender as ISpeedLimiterWindow;
                if (dialog == null) return;
                dialog.OkClicked -= okClicked;
                runtimeContext.UpdateSpeedLimit(dialog.EnableSpeedLimit, dialog.SpeedLimit);
            };
            window.OkClicked += okClicked;
            window.EnableSpeedLimit = runtimeContext.EnableSpeedLimit && runtimeContext.DefaultDownloadSpeed > 0;
            window.SpeedLimit = runtimeContext.DefaultDownloadSpeed;
            window.ShowWindow();
        }

        public static void RunForDownload(ISpeedLimiterWindow window, IProgressWindowRuntimeContext runtimeContext, string downloadId, Action changed)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (runtimeContext == null) throw new ArgumentNullException(nameof(runtimeContext));
            var globalEnabled = runtimeContext.EnableSpeedLimit;
            var globalSpeed = runtimeContext.DefaultDownloadSpeed;
            EventHandler? okClicked = null;
            okClicked = (sender, _) =>
            {
                var dialog = sender as ISpeedLimiterWindow;
                if (dialog == null) return;
                dialog.OkClicked -= okClicked;
                var setting = SpeedLimiter.SettingFromDialog(dialog.EnableSpeedLimit, dialog.SpeedLimit, globalEnabled, globalSpeed);
                runtimeContext.CoreService.SetDownloadSpeedLimit(downloadId, setting);
                changed?.Invoke();
            };
            window.OkClicked += okClicked;
            var effective = SpeedLimiter.EffectiveLimit(runtimeContext.CoreService.GetDownloadSpeedLimit(downloadId), globalEnabled, globalSpeed);
            window.EnableSpeedLimit = effective > 0;
            window.SpeedLimit = effective > 0 ? effective : globalSpeed > 0 ? globalSpeed : 0;
            window.ShowWindow();
        }
    }
}
