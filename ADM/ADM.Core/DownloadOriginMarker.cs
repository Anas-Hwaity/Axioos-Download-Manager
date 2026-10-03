using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TraceLog;

namespace ADM.Core
{
    public static class DownloadOriginMarker
    {
        public const string StreamName = "Axioos.Origin";
        public const string Comment = "Downloaded by Axioos Download Manager";
        private const uint GenericWrite = 0x40000000;
        private const uint GenericRead = 0x80000000;
        private const uint CreateAlways = 2;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x80;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        public static bool IsSupported => Environment.OSVersion.Platform == PlatformID.Win32NT;

        public static bool TryMark(string? path)
        {
            if (!IsSupported || path == null || path.Length == 0) return false;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                var lastWrite = info.LastWriteTimeUtc;
                var length = info.Length;
                var text = Comment + "\r\nDate=" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) + "\r\n";
                var payload = Encoding.UTF8.GetBytes(text);
                using (var handle = CreateFileW(path + ":" + StreamName, GenericWrite, 0, IntPtr.Zero, CreateAlways, FileAttributeNormal, IntPtr.Zero))
                {
                    if (handle.IsInvalid) return false;
                    using var stream = new FileStream(handle, FileAccess.Write);
                    stream.Write(payload, 0, payload.Length);
                }
                var after = new FileInfo(path);
                if (after.Length != length)
                {
                    Log.Debug("Origin marker changed the main stream length; this must never happen");
                    return false;
                }
                if (after.LastWriteTimeUtc != lastWrite) File.SetLastWriteTimeUtc(path, lastWrite);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Origin marker could not be written");
                return false;
            }
        }

        public static string? TryRead(string? path)
        {
            if (!IsSupported || path == null || path.Length == 0) return null;
            try
            {
                using var handle = CreateFileW(path + ":" + StreamName, GenericRead, 1, IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
                if (handle.IsInvalid) return null;
                using var stream = new FileStream(handle, FileAccess.Read);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Origin marker could not be read");
                return null;
            }
        }
    }
}
