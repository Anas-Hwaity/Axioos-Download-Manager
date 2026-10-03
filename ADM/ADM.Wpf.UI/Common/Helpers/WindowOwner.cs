using System;
using System.Windows;
using System.Windows.Interop;

namespace ADM.Wpf.UI.Common.Helpers
{
    internal static class WindowOwner
    {
        internal static Window? Usable(Window? candidate)
        {
            if (candidate == null) return null;
            return new WindowInteropHelper(candidate).Handle == IntPtr.Zero ? null : candidate;
        }
    }
}
