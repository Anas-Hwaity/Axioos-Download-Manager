using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Axioos.Setup
{
    internal static class Palette
    {
        internal static readonly Color Background = Color.FromArgb(10, 17, 32);
        internal static readonly Color GlowOne = Color.FromArgb(27, 58, 92);
        internal static readonly Color GlowTwo = Color.FromArgb(51, 32, 90);
        internal static readonly Color Ink = Color.FromArgb(232, 241, 255);
        internal static readonly Color Muted = Color.FromArgb(142, 166, 196);
        internal static readonly Color AccentOne = Color.FromArgb(94, 231, 247);
        internal static readonly Color AccentTwo = Color.FromArgb(123, 140, 255);
        internal static readonly Color OnAccent = Color.FromArgb(7, 16, 31);
        internal static readonly Color Danger = Color.FromArgb(255, 138, 150);

        internal static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Max(1f, Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class PillButton : Control
    {
        private bool hover;
        private bool pressed;

        internal bool Primary { get; set; }

        internal PillButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Focus();
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width < 4 || Height < 4) return;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = Palette.Rounded(bounds, Height / 3f))
            {
                if (Primary)
                {
                    using (var fill = new LinearGradientBrush(bounds, Palette.AccentOne, Palette.AccentTwo, LinearGradientMode.Horizontal))
                    {
                        fill.WrapMode = WrapMode.TileFlipX;
                        graphics.FillPath(fill, path);
                    }
                    Color veilColor = Color.Empty;
                    if (!Enabled) veilColor = Color.FromArgb(150, Palette.Background);
                    else if (pressed) veilColor = Color.FromArgb(60, Color.Black);
                    else if (hover) veilColor = Color.FromArgb(46, Color.White);
                    if (!veilColor.IsEmpty)
                    {
                        using (var veil = new SolidBrush(veilColor)) graphics.FillPath(veil, path);
                    }
                }
                else
                {
                    int alpha = !Enabled ? 14 : pressed ? 64 : hover ? 46 : 26;
                    using (var fill = new SolidBrush(Color.FromArgb(alpha, Palette.Ink))) graphics.FillPath(fill, path);
                    using (var outline = new Pen(Color.FromArgb(Enabled ? 96 : 44, Palette.Muted))) graphics.DrawPath(outline, path);
                }
                if (Focused && ShowFocusCues)
                {
                    using (var ring = new Pen(Color.FromArgb(210, Palette.Ink))) graphics.DrawPath(ring, path);
                }
            }
            Color textColor = Primary ? Palette.OnAccent : (Enabled ? Palette.Ink : Palette.Muted);
            TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class ThinProgress : Control
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private int percent;
        private bool marquee;
        private float offset = -0.3f;

        internal ThinProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            timer.Interval = 30;
            timer.Tick += (sender, e) =>
            {
                offset += 0.022f;
                if (offset > 1.05f) offset = -0.3f;
                Invalidate();
            };
        }

        internal int Percent
        {
            get { return percent; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (next == percent) return;
                percent = next;
                Invalidate();
            }
        }

        internal bool Marquee
        {
            get { return marquee; }
            set
            {
                if (marquee == value) return;
                marquee = value;
                offset = -0.3f;
                if (marquee) timer.Start();
                else timer.Stop();
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width < 4 || Height < 2) return;
            var track = new RectangleF(0f, 0f, Width, Height);
            using (GraphicsPath path = Palette.Rounded(track, Height / 2f))
            {
                using (var fill = new SolidBrush(Color.FromArgb(46, Palette.Muted))) graphics.FillPath(fill, path);
                float start = marquee ? offset * Width : 0f;
                float length = marquee ? 0.3f * Width : Width * percent / 100f;
                if (length < 1f) return;
                Region previous = graphics.Clip;
                graphics.SetClip(path, CombineMode.Intersect);
                using (var accent = new LinearGradientBrush(track, Palette.AccentOne, Palette.AccentTwo, LinearGradientMode.Horizontal))
                {
                    graphics.FillRectangle(accent, start, 0f, length, Height);
                }
                graphics.Clip = previous;
                previous.Dispose();
            }
        }
    }

    internal sealed class SetupWindow : Form
    {
        private readonly SetupOptions options;
        private readonly Label status;
        private readonly Label detail;
        private readonly Label folderText;
        private readonly LinkLabel change;
        private readonly CheckBox launch;
        private readonly PillButton primary;
        private readonly PillButton secondary;
        private readonly ThinProgress progress;
        private readonly Bitmap logo;
        private string folder;
        private bool folderChanged;
        private bool working;
        private bool finished;
        private bool freshInstall;

        internal int ExitCode { get; private set; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal SetupWindow(SetupOptions options)
        {
            this.options = options;
            ExitCode = 1602;
            folder = Installer.DefaultFolder();
            logo = LoadLogo();

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Program.Title;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 392);
            BackColor = Palette.Background;
            ForeColor = Palette.Ink;
            Font = new Font("Segoe UI", 9.75F);
            DoubleBuffered = true;
            KeyPreview = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                Icon = SystemIcons.Application;
            }

            var picture = new PictureBox();
            picture.BackColor = Color.Transparent;
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            picture.Image = logo;
            picture.SetBounds(36, 32, 80, 80);

            var title = NewLabel("Axioos Download Manager", 17F, FontStyle.Bold, Palette.Ink);
            title.AutoSize = true;
            title.Location = new Point(132, 34);

            var version = NewLabel("Version " + VersionText(), 10F, FontStyle.Regular, Palette.Muted);
            version.AutoSize = true;
            version.Location = new Point(135, 70);

            var credit = NewLabel("Developed by Anas Al Hwaity", 9F, FontStyle.Regular, Palette.Muted);
            credit.AutoSize = true;
            credit.Location = new Point(135, 92);

            status = NewLabel("Ready to install", 12F, FontStyle.Bold, Palette.Ink);
            status.SetBounds(36, 140, 528, 26);

            detail = NewLabel(string.Empty, 9F, FontStyle.Regular, Palette.Muted);
            detail.SetBounds(36, 168, 528, 38);

            progress = new ThinProgress();
            progress.SetBounds(36, 214, 528, 6);

            var folderCaption = NewLabel("Install location", 9F, FontStyle.Regular, Palette.Muted);
            folderCaption.AutoSize = true;
            folderCaption.Location = new Point(36, 238);

            folderText = NewLabel(folder, 9.75F, FontStyle.Regular, Palette.Ink);
            folderText.AutoEllipsis = true;
            folderText.SetBounds(36, 258, 450, 22);

            change = new LinkLabel();
            change.Text = "Change";
            change.AutoSize = true;
            change.BackColor = Color.Transparent;
            change.LinkColor = Palette.AccentOne;
            change.ActiveLinkColor = Palette.Ink;
            change.VisitedLinkColor = Palette.AccentOne;
            change.LinkBehavior = LinkBehavior.HoverUnderline;
            change.Location = new Point(500, 258);
            change.LinkClicked += (sender, e) => ChooseFolder();

            launch = new CheckBox();
            launch.Text = "Start Axioos when setup finishes";
            launch.Checked = true;
            launch.AutoSize = true;
            launch.BackColor = Color.Transparent;
            launch.ForeColor = Palette.Ink;
            launch.Location = new Point(38, 296);

            secondary = new PillButton();
            secondary.Text = "Cancel";
            secondary.SetBounds(304, 334, 116, 38);
            secondary.Click += (sender, e) => Close();

            primary = new PillButton();
            primary.Primary = true;
            primary.Text = "Install";
            primary.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            primary.SetBounds(432, 334, 132, 38);
            primary.Click += (sender, e) => PrimaryAction();

            Controls.Add(picture);
            Controls.Add(title);
            Controls.Add(version);
            Controls.Add(credit);
            Controls.Add(status);
            Controls.Add(detail);
            Controls.Add(progress);
            Controls.Add(folderCaption);
            Controls.Add(folderText);
            Controls.Add(change);
            Controls.Add(launch);
            Controls.Add(secondary);
            Controls.Add(primary);
            ResumeLayout(false);
            PerformLayout();

            ShowReadyDetail();
            ActiveControl = primary;
        }

        private static Label NewLabel(string text, float size, FontStyle style, Color color)
        {
            var label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", size, style);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.UseMnemonic = false;
            return label;
        }

        private static string VersionText()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return version.Major + "." + version.Minor + "." + version.Build;
        }

        private static Bitmap LoadLogo()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
                {
                    if (stream == null) return null;
                    using (Image image = Image.FromStream(stream))
                    {
                        return new Bitmap(image);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
            }
            catch (Exception)
            {
                return;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && logo != null) logo.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Rectangle area = ClientRectangle;
            using (var fill = new SolidBrush(Palette.Background)) graphics.FillRectangle(fill, area);
            if (area.Width < 8 || area.Height < 8) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Glow(graphics, new RectangleF(-area.Width * 0.35f, -area.Height * 0.65f, area.Width * 1.15f, area.Height * 1.4f), Palette.GlowOne);
            Glow(graphics, new RectangleF(area.Width * 0.42f, area.Height * 0.3f, area.Width * 0.95f, area.Height * 1.2f), Palette.GlowTwo);
        }

        private static void Glow(Graphics graphics, RectangleF bounds, Color color)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(215, color);
                    brush.SurroundColors = new[] { Color.FromArgb(0, color) };
                    graphics.FillPath(brush, path);
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (working && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        private void ShowReadyDetail()
        {
            detail.ForeColor = Palette.Muted;
            detail.Text = Installer.AppIsRunning()
                ? "Axioos is running and will be closed while setup installs the new version. Windows will ask for permission."
                : "Setup installs Axioos for everyone who uses this computer. Windows will ask for permission.";
        }

        private void ChooseFolder()
        {
            if (working || finished) return;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where to install Axioos Download Manager";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(folder)) dialog.SelectedPath = folder;
                if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dialog.SelectedPath)) return;
                string chosen = dialog.SelectedPath.TrimEnd('\\');
                if (!string.Equals(Path.GetFileName(chosen), "Axioos", StringComparison.OrdinalIgnoreCase)) chosen = Path.Combine(chosen + "\\", "Axioos");
                folder = chosen;
                folderChanged = true;
                folderText.Text = folder;
            }
        }

        private void PrimaryAction()
        {
            if (working) return;
            if (finished)
            {
                Finish();
                return;
            }
            StartInstall();
        }

        private void StartInstall()
        {
            working = true;
            freshInstall = !File.Exists(Path.Combine(folder, Installer.AppExecutable));
            primary.Enabled = false;
            secondary.Enabled = false;
            change.Enabled = false;
            status.ForeColor = Palette.Ink;
            status.Text = "Getting ready";
            detail.ForeColor = Palette.Muted;
            detail.Text = "Unpacking the installer.";
            progress.Marquee = false;
            progress.Percent = 0;
            string customFolder = folderChanged ? folder : null;
            var worker = new Thread(() =>
            {
                InstallResult result;
                try
                {
                    result = Installer.Run(customFolder, options.PassThrough, SetupMode.Branded, Report);
                }
                catch (InvalidDataException error)
                {
                    result = InstallResult.Failure("This setup file is damaged. Download it again. " + error.Message);
                }
                catch (Exception error)
                {
                    result = InstallResult.Failure(error.Message);
                }
                Post(() => Complete(result));
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        private void Report(SetupStep step, int percent)
        {
            Post(() =>
            {
                if (step == SetupStep.Unpacking)
                {
                    progress.Percent = percent;
                    return;
                }
                progress.Marquee = true;
                status.Text = "Installing";
                detail.Text = step == SetupStep.Closing
                    ? "Installing Axioos. This takes a moment."
                    : "Approve the Windows permission prompt to continue.";
            });
        }

        private void Complete(InstallResult result)
        {
            working = false;
            progress.Marquee = false;
            ExitCode = result.Code;
            if (result.Success)
            {
                finished = true;
                progress.Percent = 100;
                status.Text = "Axioos Download Manager is installed";
                detail.Text = result.RestartNeeded
                    ? "Restart Windows to finish replacing files that were in use."
                    : "You will find Axioos in the Start menu and on the desktop.";
                primary.Text = "Finish";
                primary.Enabled = true;
                secondary.Visible = false;
                change.Visible = false;
                primary.Focus();
                return;
            }
            progress.Percent = 0;
            primary.Enabled = true;
            secondary.Enabled = true;
            change.Enabled = true;
            primary.Focus();
            if (result.Cancelled)
            {
                status.Text = "Setup was cancelled";
                detail.Text = "Axioos was not installed. Choose Install to try again.";
                return;
            }
            status.ForeColor = Palette.Danger;
            status.Text = "Setup could not finish";
            detail.Text = result.Message ?? "Windows Installer reported a problem.";
            primary.Text = "Try again";
        }

        private void Finish()
        {
            if (launch.Checked)
            {
                string application = Path.Combine(folder, Installer.AppExecutable);
                try
                {
                    if (File.Exists(application))
                    {
                        var start = new ProcessStartInfo(application);
                        start.UseShellExecute = true;
                        start.WorkingDirectory = folder;
                        if (freshInstall) start.Arguments = "--first-run";
                        Process started = Process.Start(start);
                        if (started != null) started.Dispose();
                    }
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, "Axioos is installed, but it could not be started from here.\n\n" + error.Message, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            Close();
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
