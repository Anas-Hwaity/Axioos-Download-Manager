using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TraceLog;
using ADM.Core.IO;
using ADM.Core.Util;

namespace ADM.Core
{
    public class Config
    {
        private static Config instance;
        private static object lockObj = new();
        public static Config Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (lockObj)
                    {
                        if (instance == null)
                        {
                            LoadConfig();
                        }
                    }
                }

                return instance!;
            }

            private set
            {
                instance = value;
            }
        }

        public static string DataDir { get; set; }
        public static string AppDir { get; set; }

        public static string EnsureDataFolder()
        {
            var loaded = Instance;
            var folder = AppDir;
            return loaded == null || folder == null ? string.Empty : folder;
        }

        public static int DefaultNotificationTimeOut => 30000;

        public int NotificationTimeOut { get; set; }

        public bool IsBrowserMonitoringEnabled { get; set; } = true;

        public static bool DefaultShowNotification => true;

        public bool ShowNotification { get; set; } = true;

        public static string DefaultFallbackUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.99 Safari/537.36";

        public string FallbackUserAgent { get; set; } = DefaultFallbackUserAgent;

        public static string[] DefaultVideoExtensions => new string[]
            {
                "MP4", "M3U8", "F4M", "WEBM", "OGG", "MP3", "AAC", "FLV", "MKV", "DIVX",
                "MOV", "MPG", "MPEG","OPUS", "MPD"
            };

        public string[] VideoExtensions { get; set; }

        public static string[] DefaultFileExtensions => new string[]
            {
                "ZIP", "RAR", "7Z", "TAR", "GZ", "TGZ", "BZ2", "TBZ2", "XZ", "TXZ", "ZST", "LZ", "LZMA", "CAB", "ARJ", "SIT", "SITX",
                "ISO", "IMG", "DMG", "VHD", "VHDX", "VMDK", "OVA", "WIM", "ESD", "BIN", "CUE", "NRG",
                "EXE", "MSI", "MSIX", "MSIXBUNDLE", "APPX", "APPXBUNDLE", "APK", "XAPK", "APKS", "DEB", "RPM", "APPIMAGE", "PKG", "JAR", "RUN",
                "MP4", "MKV", "AVI", "MOV", "WMV", "FLV", "WEBM", "M4V", "MPG", "MPEG", "TS", "M2TS", "VOB", "3GP", "OGV",
                "MP3", "FLAC", "WAV", "M4A", "AAC", "OGG", "OPUS", "WMA", "ALAC", "APE"
            };

        public string[] FileExtensions { get; set; }

        public static string[] DefaultBlockedHosts => new string[]
            {
                "update.microsoft.com","windowsupdate.com","thwawte.com"
            };

        public string[] BlockedHosts { get; set; }

        public string Language { get; set; } = "English";

        public bool AllowSystemDarkTheme { get; set; } = true;
        public int GlassmorphismLevel { get; set; } = 100;
        public string AppearanceTheme { get; set; } = "glacier";
        public string AppearanceBackdrop { get; set; } = "aurora";
        public string AppearanceAccent { get; set; } = "gradient";

        public string AppearanceShell { get; set; } = string.Empty;

        private Config()
        {
            VideoExtensions = DefaultVideoExtensions;
            FileExtensions = DefaultFileExtensions;
            BlockedHosts = DefaultBlockedHosts;
            if(Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                AllowSystemDarkTheme = Environment.OSVersion.Version.Major >= 10;
            }
        }

        public List<string> RecentFolders { get; set; } = new List<string>();

        public FolderSelectionMode FolderSelectionMode { get; set; }

        public FileConflictResolution FileConflictResolution { get; set; }

        public int MaxRetry { get; set; } = 10;

        public int RetryDelay { get; set; } = 10;

        public int MaxParallelDownloads { get; set; } = 3;

        public bool ShowProgressWindow { get; set; } = true;

        public bool ShowDownloadCompleteWindow { get; set; } = true;

        public bool StartDownloadAutomatically { get; set; } = false;

        public bool FetchServerTimeStamp { get; set; } = false;

        public bool MonitorClipboard { get; set; } = false;

        public int MinVideoSize { get; set; } = 1 * 1024;

        public string TempDir { get; set; }

        public int NetworkTimeout { get; set; } = 30;

        public int MaxSegments { get; set; } = 8;

        public int DefaltDownloadSpeed { get; set; } = 0;

        public bool EnableSpeedLimit { get; set; } = false;

        public bool ShutdownAfterAllFinished { get; set; } = false;

        public bool KeepPCAwake { get; set; } = true;

        public bool RunCommandAfterCompletion { get; set; } = false;

        public string AfterCompletionCommand { get; set; }

        public bool ScanWithAntiVirus { get; set; } = false;

        public string AntiVirusExecutable { get; set; }

        public string AntiVirusArgs { get; set; }

        public ProxyInfo? Proxy { get; set; }

        public bool DoubleClickOpenFile { get; set; } = false;

        public bool RunOnLogon
        {
            get => PlatformHelper.IsAutoStartEnabled();
            set => PlatformHelper.EnableAutoStart(value);
        }

        public string UserSelectedDownloadFolder { get; set; }

        public string DefaultDownloadFolder { get; set; } =
            PlatformHelper.GetOsDefaultDownloadFolder();

        public static IEnumerable<Category> DefaultCategories = new[]
        {
            new Category
            {
                Name="CAT_DOCUMENTS",
                DisplayName="Document",
                FileExtensions=new HashSet<string>
                {
                    ".DOC", ".DOCX", ".PDF", ".MD", ".XLSX",".XLS", ".CBZ"
                },
                DefaultFolder=Path.Combine(PlatformHelper.GetOsDefaultDownloadFolder(),
                    "Documents"),
                IsPredefined=true
            },
            new Category
            {
                Name="CAT_MUSIC",
                DisplayName="Music",
                FileExtensions=new HashSet<string>
                {
                    ".MP3", ".AAC",".MPA",".WMA",".MIDI"
                },
                DefaultFolder=Path.Combine(PlatformHelper.GetOsDefaultDownloadFolder(),"Music"),
                IsPredefined=true
            },
            new Category
            {
                Name="CAT_VIDEOS",
                DisplayName="Video",
                FileExtensions=new HashSet<string>
                {
                    ".MP4",  ".WEBM", ".OGG",  ".FLV", ".MKV", ".DIVX",
                    ".MOV", ".MPG", ".MPEG",".OPUS",".AVI",".WMV",".TS"
                },
                DefaultFolder=Path.Combine(PlatformHelper.GetOsDefaultDownloadFolder(),"Video"),
                IsPredefined=true
            },
            new Category
            {
                Name="CAT_COMPRESSED",
                DisplayName="Compressed",
                FileExtensions=new HashSet<string>
                {
                    ".7Z", ".ZIP", ".RAR", ".BZ2", ".GZ",".XZ", ".TAR"
                },
                DefaultFolder=Path.Combine(PlatformHelper.GetOsDefaultDownloadFolder(),"Compressed"),
                IsPredefined=true
            },
            new Category
            {
                Name="CAT_PROGRAMS",
                DisplayName="Application",
                FileExtensions=new HashSet<string>
                {
                    ".EXE", ".DEB", ".RPM", ".MSI"
                },
                DefaultFolder=Path.Combine(PlatformHelper.GetOsDefaultDownloadFolder(),"Programs"),
                IsPredefined=true
            },
        };

        public IEnumerable<Category> Categories = DefaultCategories;

        public IEnumerable<PasswordEntry> UserCredentials { get; set; } = new List<PasswordEntry>();

        private static string ResolveDataFolderName(string dataParent)
        {
            var current = Path.Combine(dataParent, ProductIdentity.AppDataDirectoryName);
            var legacy = Path.Combine(dataParent, ProductIdentity.LegacyAppDataDirectoryName);
            if (Directory.Exists(current) || !Directory.Exists(legacy)) return ProductIdentity.AppDataDirectoryName;
            try
            {
                Directory.Move(legacy, current);
                Log.Debug("Moved the previous data folder to the Axioos data folder");
                return ProductIdentity.AppDataDirectoryName;
            }
            catch (Exception ex)
            {
                if (Directory.Exists(current) && !Directory.Exists(legacy)) return ProductIdentity.AppDataDirectoryName;
                Log.Debug(ex, "The previous data folder is in use and was kept for this session");
                return ProductIdentity.LegacyAppDataDirectoryName;
            }
        }

        public static void LoadConfig(string? path = null)
        {
            Log.Debug("Loading config...");

            if (AcceptanceTestEnvironment.IsEnabled)
            {
                var acceptanceProfile = AcceptanceTestEnvironment.ProfileDirectory;
                if (string.IsNullOrWhiteSpace(acceptanceProfile))
                {
                    throw new InvalidOperationException("Acceptance diagnostic profile path is required.");
                }
                if (!Path.IsPathRooted(acceptanceProfile))
                {
                    throw new InvalidOperationException("Acceptance diagnostic profile path must be absolute.");
                }
                path = Path.GetFullPath(acceptanceProfile);
            }

#if NET35
            var dataParent = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#else
            var dataParent = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#endif
            var dataFolderName = path == null ? ResolveDataFolderName(dataParent) : ProductIdentity.AppDataDirectoryName;
            DataDir = path ?? Path.Combine(Path.Combine(dataParent, dataFolderName), "Data");
            AppDir = path ?? Path.Combine(dataParent, dataFolderName);
            instance = new Config
            {
                TempDir = Path.Combine(DataDir, "temp")
            };
            try
            {
                if (!Directory.Exists(DataDir))
                {
                    Directory.CreateDirectory(DataDir);
                }

                var bytes = TransactedIO.ReadBytes("settings.dat", AppDir);
                if (bytes != null)
                {
                    using var ms = new MemoryStream(bytes);
                    using var reader = new BinaryReader(ms);
                    ConfigIO.DeserializeConfig(instance, reader);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }



        }


        public static void SaveConfig()
        {
            ConfigIO.SerializeConfig();
        }

    }

    public enum FolderSelectionMode
    {
        Auto, Manual
    }

    public enum FileConflictResolution
    {
        AutoRename,
        Overwrite
    }
}
