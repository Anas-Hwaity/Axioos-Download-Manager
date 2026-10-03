using System;
using System.Collections.Generic;
using System.Text;

namespace ADM.Core.UI
{
    public interface ISpeedLimiterWindow
    {
        event EventHandler? OkClicked;
        int SpeedLimit { get; set; }
        bool EnableSpeedLimit { get; set; }
        void ShowWindow();
    }
}
