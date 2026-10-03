using System;

namespace ADM.Core
{
    public static class RemixIcon
    {
        public static readonly string LinkIcon = "eeb2";

        public static readonly string RemoveIcon = "ec28";

        public static readonly string ResumeIcon = "f00b";

        public static readonly string PauseIcon = "efd8";

        public static readonly string SearchIcon = "f0d1";

        public static readonly string MenuIcon = "ef3e";

        public static readonly string DownArrowIcon = "ea4e";

        public static readonly string FileOpenIcon = "ecaf";

        public static readonly string FolderOpenIcon = "ed78";

        public static readonly string DownloadPausedIcon = "efda";

        public static readonly string DownloadActiveIcon = "ea4a";

        public static readonly string FileIcon = "eceb";

        public static readonly string WifiOnIcon = "f2c0";

        public static readonly string WifiOffIcon = "f2c2";

        public static readonly string HelpIcon = "f045";

        public static readonly string SettingsIcon = "eebd";

        public static readonly string ToggleOnIcon = "f218";

        public static readonly string ToggleOffIcon = "f219";

        public static readonly string ArchiveIcon = "ed1e";

        public static readonly string DocumentIcon = "ed0e";

        public static readonly string MusicIcon = "ecf6";

        public static readonly string VideoIcon = "ef80";

        public static readonly string AppIcon = "ed9d";

        public static readonly string OtherFileIcon = "ece0";

        public static readonly string ScheduledFileIcon = "ea1a";

        public static readonly string ArchiveIconLine = "ed1f";

        public static readonly string DocumentIconLine = "ed0f";

        public static readonly string MusicIconLine = "ecf7";

        public static readonly string VideoIconLine = "ef81";

        public static readonly string AppIconLine = "ed9e";

        public static readonly string OtherFileIconLine = "eceb";

        public static readonly string ScheduledFileIconLine = "ea1b";

        public static readonly string NotificationIcon = "f063";

        public static string GetFontIcon(string code) => ((char)Int32.Parse(code, System.Globalization.NumberStyles.HexNumber)).ToString();
    }
}
