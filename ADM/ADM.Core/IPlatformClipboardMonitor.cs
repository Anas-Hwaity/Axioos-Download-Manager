using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace ADM.Core
{
    public interface IPlatformClipboardMonitor
    {
        void StartClipboardMonitoring();

        void StopClipboardMonitoring();

        event EventHandler? ClipboardChanged;

        string? GetClipboardText();
    }
}
