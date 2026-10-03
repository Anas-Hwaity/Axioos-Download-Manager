using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace Axioos.Setup
{
    internal static class Program
    {
        internal const string Title = "Axioos Download Manager Setup";
        private const int DamagedExitCode = 2;
        private const int FailedExitCode = 1;

        [STAThread]
        private static int Main(string[] args)
        {
            var options = SetupOptions.Parse(args);
            try
            {
                if (options.ExtractFolderMissing) throw new IOException("Name the folder to extract into after /extract.");
                if (options.Verify)
                {
                    SetupPayload.Unpack(null);
                    return 0;
                }
                if (options.ExtractFolder != null)
                {
                    SetupPayload payload = SetupPayload.Unpack(null);
                    string folder = Path.GetFullPath(options.ExtractFolder);
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, payload.FileName), payload.Content);
                    return 0;
                }
                if (options.Quiet)
                {
                    return Installer.Run(null, options.PassThrough, SetupMode.Quiet, null).Code;
                }
                if (options.Classic)
                {
                    return Installer.Run(null, options.PassThrough, SetupMode.Classic, null).Code;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var window = new SetupWindow(options))
                {
                    Application.Run(window);
                    return window.ExitCode;
                }
            }
            catch (InvalidDataException error)
            {
                Fail(options, "This setup file is damaged. Download it again.\n\n" + error.Message);
                return DamagedExitCode;
            }
            catch (Exception error)
            {
                Fail(options, "Setup could not start.\n\n" + error.Message);
                return FailedExitCode;
            }
        }

        private static void Fail(SetupOptions options, string message)
        {
            if (options.Quiet || options.Verify || options.ExtractFolder != null || options.ExtractFolderMissing)
            {
                Console.Error.WriteLine(message);
                return;
            }
            MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal sealed class SetupOptions
    {
        internal bool Verify;
        internal bool Quiet;
        internal bool Classic;
        internal string ExtractFolder;
        internal bool ExtractFolderMissing;
        internal readonly List<string> PassThrough = new List<string>();

        internal static SetupOptions Parse(string[] args)
        {
            var options = new SetupOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string value = args[i];
                string lower = value.ToLowerInvariant();
                if (lower == "--verify")
                {
                    options.Verify = true;
                }
                else if (lower == "/quiet" || lower == "/silent" || lower == "/qn" || lower == "/q" || lower == "/s")
                {
                    options.Quiet = true;
                }
                else if (lower == "/classic" || lower == "--classic")
                {
                    options.Classic = true;
                }
                else if (lower == "/extract" || lower == "--extract")
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("/", StringComparison.Ordinal) && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        options.ExtractFolder = args[++i];
                    }
                    else
                    {
                        options.ExtractFolderMissing = true;
                    }
                }
                else
                {
                    options.PassThrough.Add(value);
                }
            }
            return options;
        }
    }
}
