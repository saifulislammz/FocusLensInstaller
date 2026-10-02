using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("FocusLens OSD Alert System")]
[assembly: System.Reflection.AssemblyDescription("On-Screen Zoom & Focus Indicator for OBS Studio")]
[assembly: System.Reflection.AssemblyCompany("Saiful Islam (saifulislam.net)")]
[assembly: System.Reflection.AssemblyProduct("FocusLens")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) Saiful Islam - saifulislam.net")]
[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]

namespace FocusLensOSD
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "FocusLensOSD_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new FocusLensAppContext());
            }
        }
    }

    public class FocusLensAppContext : ApplicationContext
    {
        private OverlayForm overlayForm;
        private NotifyIcon trayIcon;
        private MenuItem mnuAlertEnabled;
        private System.Windows.Forms.Timer pollTimer;
        private FileSystemWatcher fileWatcher;
        private System.Windows.Forms.Timer testPreviewTimer;

        private string configPath;
        private string configDir;

        private bool isObsActive = false;
        private bool isZoomed = false;
        private bool isAlertEnabled = true;

        public FocusLensAppContext()
        {
            // Resolve zoominator.json config path
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            configDir = Path.Combine(appData, @"obs-studio\plugin_config\zoominator");
            configPath = Path.Combine(configDir, "zoominator.json");

            // Read user preferences
            try
            {
                object val = Registry.GetValue(@"HKEY_CURRENT_USER\Software\FocusLensOSD", "AlertEnabled", 1);
                if (val != null && (int)val == 0) isAlertEnabled = false;
            }
            catch { }

            // Create Overlay Form (starts 100% hidden by default)
            overlayForm = new OverlayForm();
            overlayForm.Hide();

            // Create System Tray Icon
            trayIcon = new NotifyIcon();
            trayIcon.Text = "FocusLens Alert System (By Saiful Islam - saifulislam.net)";
            try
            {
                Bitmap bmp = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Brush b = new SolidBrush(Color.FromArgb(220, 20, 40)))
                    {
                        g.FillEllipse(b, 1, 1, 14, 14);
                    }
                    using (Pen p = new Pen(Color.White, 1.5f))
                    {
                        g.DrawEllipse(p, 3.5f, 3.5f, 5.5f, 5.5f);
                        g.DrawLine(p, 8.5f, 8.5f, 12f, 12f);
                    }
                }
                trayIcon.Icon = Icon.FromHandle(bmp.GetHicon());
            }
            catch
            {
                trayIcon.Icon = SystemIcons.Information;
            }

            ContextMenu trayMenu = new ContextMenu();
            mnuAlertEnabled = new MenuItem("Alert Enabled (Show on Zoom)", OnToggleAlert);
            mnuAlertEnabled.Checked = isAlertEnabled;
            trayMenu.MenuItems.Add(mnuAlertEnabled);
            trayMenu.MenuItems.Add("Test Alert (5s Preview)", OnTestPreview);
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Sync (Reset to OFF)", OnSync);
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Exit", OnExit);
            trayIcon.ContextMenu = trayMenu;

            // Check if OBS is running right now
            isObsActive = IsObsRunning();
            trayIcon.Visible = isObsActive;

            // Setup FileSystemWatcher for instant 0ms state changes
            SetupFileWatcher();

            // High frequency polling timer (50ms) to ensure 100% reliability
            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 50;
            pollTimer.Tick += (s, e) => CheckState();
            pollTimer.Start();

            // Run initial check
            CheckState();
        }

        private void SetupFileWatcher()
        {
            try
            {
                if (Directory.Exists(configDir))
                {
                    fileWatcher = new FileSystemWatcher(configDir, "zoominator.json");
                    fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
                    fileWatcher.Changed += (s, e) => CheckState();
                    fileWatcher.Created += (s, e) => CheckState();
                    fileWatcher.EnableRaisingEvents = true;
                }
            }
            catch { }
        }

        private void CheckState()
        {
            bool obsRunning = IsObsRunning();

            if (obsRunning != isObsActive)
            {
                isObsActive = obsRunning;
                trayIcon.Visible = isObsActive;

                if (isObsActive && fileWatcher == null)
                {
                    SetupFileWatcher();
                }
            }

            if (!isObsActive)
            {
                if (isZoomed)
                {
                    isZoomed = false;
                    SetAlertVisible(false);
                }
                return;
            }

            // If test preview is running, do not override
            if (testPreviewTimer != null && testPreviewTimer.Enabled)
            {
                return;
            }

            // Real OBS zoom state from zoominator.json (recovery_active: true/false)
            bool zoomedInObs = ReadZoomActiveFromConfig();
            if (zoomedInObs != isZoomed)
            {
                isZoomed = zoomedInObs;
                SetAlertVisible(isZoomed && isAlertEnabled && isObsActive);
            }
        }

        private bool ReadZoomActiveFromConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    using (var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs))
                    {
                        string text = reader.ReadToEnd();
                        var match = System.Text.RegularExpressions.Regex.Match(text, @"""recovery_active""\s*:\s*(true|false)");
                        if (match.Success)
                        {
                            return match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private void SetAlertVisible(bool visible)
        {
            if (overlayForm.InvokeRequired)
            {
                overlayForm.Invoke(new Action(() => SetAlertVisible(visible)));
                return;
            }

            if (visible)
            {
                if (!overlayForm.Visible)
                {
                    overlayForm.Show();
                }
            }
            else
            {
                if (overlayForm.Visible)
                {
                    overlayForm.Hide();
                }
            }
        }

        private bool IsObsRunning()
        {
            return Process.GetProcessesByName("obs64").Length > 0 ||
                   Process.GetProcessesByName("obs32").Length > 0 ||
                   Process.GetProcessesByName("obs").Length > 0;
        }

        private void OnToggleAlert(object sender, EventArgs e)
        {
            isAlertEnabled = !isAlertEnabled;
            mnuAlertEnabled.Checked = isAlertEnabled;
            try
            {
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\FocusLensOSD", "AlertEnabled", isAlertEnabled ? 1 : 0);
            }
            catch { }

            SetAlertVisible(isZoomed && isAlertEnabled && isObsActive);
        }

        private void OnTestPreview(object sender, EventArgs e)
        {
            SetAlertVisible(true);

            if (testPreviewTimer == null)
            {
                testPreviewTimer = new System.Windows.Forms.Timer();
                testPreviewTimer.Interval = 5000;
                testPreviewTimer.Tick += (s, ev) =>
                {
                    testPreviewTimer.Stop();
                    CheckState();
                };
            }
            testPreviewTimer.Stop();
            testPreviewTimer.Start();
        }

        private void OnSync(object sender, EventArgs e)
        {
            if (testPreviewTimer != null) testPreviewTimer.Stop();
            isZoomed = false;
            SetAlertVisible(false);
            CheckState();
        }

        private void OnExit(object sender, EventArgs e)
        {
            if (pollTimer != null)
            {
                pollTimer.Stop();
                pollTimer.Dispose();
            }
            if (fileWatcher != null)
            {
                fileWatcher.Dispose();
            }
            if (testPreviewTimer != null)
            {
                testPreviewTimer.Dispose();
            }
            trayIcon.Visible = false;
            overlayForm.Hide();
            overlayForm.Dispose();
            Application.Exit();
        }
    }

    public class OverlayForm : Form
    {
        private Label lblStatus;

        [DllImport("user32.dll")]
        private static extern uint SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED
                cp.ExStyle |= 0x20;    // WS_EX_TRANSPARENT (click-through)
                cp.ExStyle |= 0x80;    // WS_EX_TOOLWINDOW (hide from Alt+Tab)
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; } // Do not steal focus from OBS/games
        }

        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;

            int badgeWidth = 120;
            int badgeHeight = 24;
            this.Size = new Size(badgeWidth, badgeHeight);

            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            int posX = (workingArea.Width - badgeWidth) / 2 + workingArea.Left;
            int posY = workingArea.Bottom - badgeHeight - 37;
            this.Location = new Point(posX, posY);

            lblStatus = new Label();
            lblStatus.Text = "🔍 ZOOM ACTIVE";
            lblStatus.Font = new Font("Segoe UI", 9.0f, FontStyle.Bold);
            lblStatus.ForeColor = Color.White;
            lblStatus.BackColor = Color.FromArgb(220, 20, 40);
            lblStatus.AutoSize = false;
            lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblStatus.Dock = DockStyle.Fill;

            this.Controls.Add(lblStatus);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                // Ensures the overlay badge is completely invisible in OBS recordings & streams!
                SetWindowDisplayAffinity(this.Handle, WDA_EXCLUDEFROMCAPTURE);
            }
            catch { }
        }
    }
}
