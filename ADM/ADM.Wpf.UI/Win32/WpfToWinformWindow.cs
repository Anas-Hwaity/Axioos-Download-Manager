using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace ADM.Wpf.UI.Win32
{
    internal class WinformsWindow
        : System.Windows.Forms.IWin32Window
    {
        IntPtr _handle;

        public WinformsWindow(Window window)
        {
            _handle = new WindowInteropHelper(window).Handle;
        }


        IntPtr System.Windows.Forms.IWin32Window.Handle
        {
            get { return _handle; }
        }

    }
}
