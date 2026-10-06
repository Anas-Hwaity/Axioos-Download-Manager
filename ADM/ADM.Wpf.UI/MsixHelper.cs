using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ADM.Core;
using TraceLog;

namespace ADM.Wpf.UI
{
    internal static class MsixHelper
    {
        public static readonly bool IsAppContainer = IsMsixPackage();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder packageFullName);

        const long APPMODEL_ERROR_NO_PACKAGE = 15700L;

        private static bool IsMsixPackage()
        {
            try
            {
                int length = 0;
                StringBuilder sb = new StringBuilder(0);
                int result = GetCurrentPackageFullName(ref length, sb);

                sb = new StringBuilder(length);
                result = GetCurrentPackageFullName(ref length, sb);

                return result != APPMODEL_ERROR_NO_PACKAGE;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "MSIX package detection failed");
                return false;
            }
        }


        public static bool CopyExtension()
        {
            return CopyFilesRecursively(AppDomain.CurrentDomain.BaseDirectory,
                        Config.AppDir, "chrome-extension");
        }

        private static bool CopyFilesRecursively(string sourcePath, string targetPath, string basePath)
        {
            var complete = true;
            var srcPath = Path.Combine(sourcePath, basePath);
            var dstPath = Path.Combine(targetPath, basePath);
            Directory.CreateDirectory(dstPath);
            foreach (string newPath in Directory.GetFiles(srcPath, "*.*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(newPath);
                if (!CopyOverExisting(newPath, Path.Combine(dstPath, name))) complete = false;
            }
            foreach (string newPath in Directory.GetDirectories(srcPath, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(newPath);
                if (!CopyFilesRecursively(sourcePath, targetPath, Path.Combine(basePath, name))) complete = false;
            }
            return complete;
        }

        private static bool CopyOverExisting(string source, string target)
        {
            const FileAttributes blocking = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
            try
            {
                if (File.Exists(target))
                {
                    var existing = File.GetAttributes(target);
                    if ((existing & blocking) != 0) File.SetAttributes(target, existing & ~blocking);
                }
                File.Copy(source, target, true);
                var copied = File.GetAttributes(target);
                if ((copied & blocking) != 0) File.SetAttributes(target, copied & ~blocking);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug(ex, "An extension file could not be refreshed: " + Path.GetFileName(target));
                return false;
            }
        }
    }
}
