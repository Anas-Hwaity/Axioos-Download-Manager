using System;
using System.Diagnostics;
using System.IO;

namespace ADM.Msix.AutoLaunch
{
    static class Program
    {
        static void Main()
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "adm-app.exe";
            psi.UseShellExecute = true;
            psi.Arguments = "--background";
            Process.Start(psi);
        }
    }
}
