using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Axioos.Setup
{
    internal static class Program
    {
        private const string Title = "Axioos Download Manager Setup";
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
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SetupPayload payload = options.Quiet ? SetupPayload.Unpack(null) : UnpackWithProgress();
                if (options.ExtractFolder != null)
                {
                    string folder = Path.GetFullPath(options.ExtractFolder);
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, payload.FileName), payload.Content);
                    return 0;
                }
                return Install(payload, options);
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
            if (options.Quiet || options.Verify)
            {
                Console.Error.WriteLine(message);
                return;
            }
            MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static SetupPayload UnpackWithProgress()
        {
            SetupPayload payload = null;
            Exception failure = null;
            bool closedEarly;
            using (var form = new ProgressForm(Title))
            {
                var worker = new Thread(() =>
                {
                    try
                    {
                        payload = SetupPayload.Unpack(form.Report);
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                    finally
                    {
                        form.Finish();
                    }
                });
                worker.IsBackground = true;
                form.Shown += (sender, e) => worker.Start();
                Application.Run(form);
                if (worker.IsAlive) worker.Join();
                closedEarly = form.ClosedEarly;
            }
            if (closedEarly) throw new IOException("Setup was closed before it was ready.");
            if (failure is InvalidDataException) throw new InvalidDataException(failure.Message, failure);
            if (failure != null) throw new IOException(failure.Message, failure);
            if (payload == null) throw new IOException("Setup was closed before it was ready.");
            return payload;
        }

        private static int Install(SetupPayload payload, SetupOptions options)
        {
            string folder = Path.Combine(Path.GetTempPath(), "Axioos-Setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string installer = Path.Combine(folder, payload.FileName);
            try
            {
                File.WriteAllBytes(installer, payload.Content);
                var arguments = new StringBuilder("/i \"" + installer + "\"");
                if (options.Quiet) arguments.Append(" /qn /norestart");
                foreach (string extra in options.PassThrough) arguments.Append(' ').Append(Quote(extra));
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), arguments.ToString());
                start.UseShellExecute = false;
                using (Process process = Process.Start(start))
                {
                    if (process == null) throw new IOException("Windows Installer could not be started.");
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            finally
            {
                TryDelete(folder);
            }
        }

        private static string Quote(string value)
        {
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return value;
            int equals = value.IndexOf('=');
            if (equals > 0 && value.IndexOf('"') < 0 && IsPropertyName(value.Substring(0, equals)))
            {
                return value.Substring(0, equals + 1) + "\"" + value.Substring(equals + 1) + "\"";
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static bool IsPropertyName(string name)
        {
            foreach (char character in name)
            {
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '.') return false;
            }
            return name.Length > 0;
        }

        private static void TryDelete(string folder)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(400);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(400);
                }
            }
        }
    }

    internal sealed class SetupOptions
    {
        internal bool Verify;
        internal bool Quiet;
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

    internal sealed class ProgressForm : Form
    {
        private readonly ProgressBar bar;
        private bool finished;

        internal bool ClosedEarly { get; private set; }

        internal ProgressForm(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(420, 96);
            Font = SystemFonts.MessageBoxFont;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                Icon = SystemIcons.Application;
            }
            var label = new Label();
            label.Text = "Getting Axioos Download Manager ready to install";
            label.AutoSize = false;
            label.SetBounds(18, 16, 384, 24);
            bar = new ProgressBar();
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.SetBounds(18, 50, 384, 20);
            Controls.Add(label);
            Controls.Add(bar);
        }

        internal void Report(int percent)
        {
            Post(() => bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, percent)));
        }

        internal void Finish()
        {
            Post(() =>
            {
                finished = true;
                Close();
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!finished && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
            if (!finished && !e.Cancel) ClosedEarly = true;
        }

        private void Post(Action action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }
    }
}
