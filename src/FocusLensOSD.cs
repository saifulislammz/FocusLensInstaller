using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("FocusLens OSD Alert System")]
[assembly: System.Reflection.AssemblyDescription("On-Screen Zoom & Focus Indicator for OBS Studio")]
[assembly: System.Reflection.AssemblyCompany("Saiful Islam (saifulislam.net)")]
[assembly: System.Reflection.AssemblyProduct("FocusLens")]
[assembly: System.Reflection.AssemblyCopyright("Saiful Islam - saifulislam.net")]
[assembly: System.Reflection.AssemblyVersion("2.1.0.0")]

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
                Application.Run(new OverlayForm());
            }
        }
    }

    public class OverlayForm : Form
    {
        private bool isZoomed = false;
        private bool isAlertEnabled = true;
        private bool isObsActive = false;
        private Label lblStatus;
        private NotifyIcon trayIcon;
        private MenuItem mnuAlertEnabled;
        private System.Windows.Forms.Timer processCheckTimer;

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

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

        [DllImport("user32.dll")]
        private static extern uint SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED
                cp.ExStyle |= 0x20;    // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x80;    // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        public OverlayForm()
        {
            _proc = HookCallback;

            try
            {
                object val = Registry.GetValue(@"HKEY_CURRENT_USER\Software\FocusLensOSD", "AlertEnabled", 1);
                if (val != null && (int)val == 0) isAlertEnabled = false;
            }
            catch { }

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;
            this.ShowInTaskbar = false;
            
            int badgeWidth = 110;
            int badgeHeight = 22;
            this.Size = new Size(badgeWidth, badgeHeight);

            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            int posX = (workingArea.Width - badgeWidth) / 2 + workingArea.Left;
            int posY = workingArea.Bottom - badgeHeight - 37;
            this.Location = new Point(posX, posY);

            lblStatus = new Label();
            lblStatus.Text = "🔍 ZOOM ACTIVE";
            lblStatus.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblStatus.ForeColor = Color.White;
            lblStatus.BackColor = Color.FromArgb(220, 20, 30);
            lblStatus.AutoSize = false;
            lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Visible = false;

            this.Controls.Add(lblStatus);

            trayIcon = new NotifyIcon();
            trayIcon.Text = "FocusLens Alert System (By Saiful Islam - saifulislam.net)";
            
            try
            {
                Bitmap bmp = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Brush b = new SolidBrush(Color.FromArgb(220, 20, 30)))
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
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Sync (Reset to OFF)", OnSync);
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Exit", OnExit);
            trayIcon.ContextMenu = trayMenu;

            // Check if OBS is running right now
            isObsActive = IsObsRunning();
            trayIcon.Visible = isObsActive;

            if (isObsActive)
            {
                _hookID = SetHook(_proc);
            }

            // Periodic timer to monitor OBS process
            processCheckTimer = new System.Windows.Forms.Timer();
            processCheckTimer.Interval = 1000;
            processCheckTimer.Tick += ProcessCheckTimer_Tick;
            processCheckTimer.Start();
        }

        private void ProcessCheckTimer_Tick(object sender, EventArgs e)
        {
            bool obsRunning = IsObsRunning();
            if (obsRunning && !isObsActive)
            {
                isObsActive = true;
                trayIcon.Visible = true;
                if (_hookID == IntPtr.Zero)
                {
                    _hookID = SetHook(_proc);
                }
            }
            else if (!obsRunning && isObsActive)
            {
                isObsActive = false;
                trayIcon.Visible = false;
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }
                isZoomed = false;
                UpdateOverlay();
            }
        }

        private bool IsObsRunning()
        {
            return Process.GetProcessesByName("obs64").Length > 0 ||
                   Process.GetProcessesByName("obs32").Length > 0 ||
                   Process.GetProcessesByName("obs").Length > 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                SetWindowDisplayAffinity(this.Handle, WDA_EXCLUDEFROMCAPTURE);
            }
            catch { }
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

            UpdateOverlay();
        }

        private void OnSync(object sender, EventArgs e)
        {
            isZoomed = false;
            UpdateOverlay();
        }

        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Exit();
        }

        private IntPtr SetHook(LowLevelMouseProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
            {
                bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                if (ctrlPressed)
                {
                    if (isObsActive)
                    {
                        isZoomed = !isZoomed;
                        this.Invoke(new Action(UpdateOverlay));
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void UpdateOverlay()
        {
            lblStatus.Visible = (isZoomed && isAlertEnabled && isObsActive);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
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
            trayIcon.Visible = false;
            base.OnFormClosing(e);
        }
    }
}
