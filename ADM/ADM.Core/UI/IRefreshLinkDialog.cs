using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace ADM.Core
{
    public interface IRefreshLinkDialog
    {
        event EventHandler? WatchingStopped;

        void ShowWindow();

        void LinkReceived();
    }
}
