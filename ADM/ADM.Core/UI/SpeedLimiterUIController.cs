using System;

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
    }
}
