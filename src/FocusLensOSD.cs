using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Text.RegularExpressions;

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
        private System.Windows.Forms.Timer processCheckTimer;
        private System.Windows.Forms.Timer testPreviewTimer;

        private string configPath;

        private bool isObsActive = false;
        private bool isZoomed = false;
        private bool isAlertEnabled = true;
        private DateTime lastToggleTime = DateTime.MinValue;

        // Hook setup
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12; // ALT

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        // Config state
        private bool reqCtrl = true;
        private bool reqAlt = false;
        private bool reqShift = false;
        private bool reqWin = false;
        private string triggerType = "mouse"; // or "hotkey"
        private string mouseButton = "left";
        private double zoomFactor = 2.0;

        public FocusLensAppContext()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            configPath = Path.Combine(appData, @"obs-studio\plugin_config\FocusLens\FocusLens.json");

            LoadZoominatorConfig();

            try
            {
                object val = Registry.GetValue(@"HKEY_CURRENT_USER\Software\FocusLensOSD", "AlertEnabled", 1);
                if (val != null && (int)val == 0) isAlertEnabled = false;
            }
            catch { }

            overlayForm = new OverlayForm(zoomFactor);
            overlayForm.Hide();

            trayIcon = new NotifyIcon();
            trayIcon.Text = "FocusLens Alert System (By Saiful Islam)";
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

            isObsActive = IsObsRunning();
            trayIcon.Visible = isObsActive;

            processCheckTimer = new System.Windows.Forms.Timer();
            processCheckTimer.Interval = 2000;
            processCheckTimer.Tick += (s, e) => CheckObsProcess();
            processCheckTimer.Start();

            _proc = HookCallback;
            _hookID = SetHook(_proc);
        }

        private void LoadZoominatorConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var matchType = Regex.Match(json, @"""trigger_type""\s*:\s*""(.*?)""");
                    if (matchType.Success) triggerType = matchType.Groups[1].Value;

                    var matchBtn = Regex.Match(json, @"""mouse_button""\s*:\s*""(.*?)""");
                    if (matchBtn.Success) mouseButton = matchBtn.Groups[1].Value;

                    reqCtrl = json.Contains("\"mod_ctrl\":true") || json.Contains("\"mod_ctrl\": true");
                    reqAlt = json.Contains("\"mod_alt\":true") || json.Contains("\"mod_alt\": true");
                    reqShift = json.Contains("\"mod_shift\":true") || json.Contains("\"mod_shift\": true");
                    
                    reqWin = json.Contains("\"mod_win\":true") || json.Contains("\"mod_win\": true");
                    
                    var matchFactor = Regex.Match(json, @"""zoom_factor""\s*:\s*([0-9.]+)");
                    if (matchFactor.Success) double.TryParse(matchFactor.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out zoomFactor);

                }
            }
            catch { }
        }

        private IntPtr SetHook(LowLevelProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                int hookType = triggerType == "mouse" ? WH_MOUSE_LL : WH_KEYBOARD_LL;
                return SetWindowsHookEx(hookType, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                bool isTriggered = false;

                if (triggerType == "mouse" && wParam == (IntPtr)WM_LBUTTONDOWN && mouseButton == "left")
                {
                    isTriggered = true;
                }
                // Extend for keyboard or other mouse buttons if needed, but defaults are left click.

                if (isTriggered)
                {
                    bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool altPressed = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    bool shiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                    if ((ctrlPressed == reqCtrl) && (altPressed == reqAlt) && (shiftPressed == reqShift))
                    {
                        if (isObsActive && (DateTime.Now - lastToggleTime).TotalMilliseconds > 400) // 400ms debounce
                        {
                            lastToggleTime = DateTime.Now;
                            isZoomed = !isZoomed;
                            SetAlertVisible(isZoomed && isAlertEnabled);
                        }
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void CheckObsProcess()
        {
            bool obsRunning = IsObsRunning();
            if (obsRunning != isObsActive)
            {
                isObsActive = obsRunning;
                trayIcon.Visible = isObsActive;
                if (!isObsActive && isZoomed)
                {
                    isZoomed = false;
                    SetAlertVisible(false);
                }
            }
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
                if (!overlayForm.Visible) overlayForm.Show();
            }
            else
            {
                if (overlayForm.Visible) overlayForm.Hide();
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
                    SetAlertVisible(isZoomed && isAlertEnabled && isObsActive);
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
            LoadZoominatorConfig(); // Reload config in case they changed it
        }

        private void OnExit(object sender, EventArgs e)
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
            }
            if (processCheckTimer != null)
            {
                processCheckTimer.Stop();
                processCheckTimer.Dispose();
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

        public OverlayForm(double zoomLvl)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;

            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;

            int badgeWidth = 120;
            int badgeHeight = 24;
            this.Size = new Size(badgeWidth, badgeHeight);

            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            int posX = (workingArea.Width - badgeWidth) / 2 + workingArea.Left;
            int posY = workingArea.Bottom - badgeHeight - 37;
            this.Location = new Point(posX, posY);

            lblStatus = new Label();
            lblStatus.Text = "🔍 ZOOM: " + zoomLvl.ToString() + "X";
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
        }
    }
}
