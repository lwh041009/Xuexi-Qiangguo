using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace StudyLite
{
    internal sealed partial class MainForm
    {
        private readonly Color accent = Color.FromArgb(221, 35, 44);
        private readonly ListBox eventLog = new ListBox();
        private readonly Label summary = new Label();
        private readonly Label scoreSummary = new Label();
        private readonly CheckBox autoLogout = new CheckBox(), mute = new CheckBox();
        private readonly Panel homePage = new Panel(), settingsPage = new Panel(), engineHost = new Panel();
        private readonly BrowserHostForm engineWindow = new BrowserHostForm();
        private readonly Button homeTab = new Button(), settingsTab = new Button(), saveSettings = new Button();
        private QrLoginForm qrLogin;
        private Preferences preferences;
        private string lastLog = "";

        private void BuildDashboard()
        {
            preferences = Preferences.Load(Path.Combine(dataRoot, "settings.json"));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.White };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty,
                BackColor = accent, Padding = new Padding(12, 0, 6, 0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
            var title = new Label { Text = "学习强国", Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            header.Controls.Add(title, 0, 0);
            StyleTab(homeTab, "主页", delegate { ShowPage(false); });
            StyleTab(settingsTab, "设置", delegate { ShowPage(true); });
            header.Controls.Add(homeTab, 1, 0); header.Controls.Add(settingsTab, 2, 0);
            var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            homePage.Dock = settingsPage.Dock = DockStyle.Fill;
            BuildHome(); BuildSettings();
            pages.Controls.Add(homePage); pages.Controls.Add(settingsPage);
            identity.Dock = DockStyle.Fill;
            identity.TextAlign = ContentAlignment.MiddleLeft;
            identity.Padding = new Padding(12, 0, 8, 0);
            identity.ForeColor = Color.FromArgb(96, 101, 108);
            identity.BackColor = Color.FromArgb(247, 248, 250);
            identity.Text = "未登录";
            identity.AutoEllipsis = true;
            root.Controls.Add(header, 0, 0); root.Controls.Add(pages, 0, 1); root.Controls.Add(identity, 0, 2);
            // Keep native rendering alive in a transparent, noninteractive tool window.
            engineHost.Size = new Size(1100, 800);
            engineHost.Dock = DockStyle.Fill;
            web.Dock = DockStyle.Fill;
            engineHost.Controls.Add(web);
            engineWindow.Controls.Add(engineHost);
            engineWindow.Icon = Program.AppIcon();
            engineWindow.FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (closing) return;
                args.Cancel = true;
                if (running && phase == "exam") Halt("答题窗口已关闭，任务已停止");
                else engineWindow.HideExam();
            };
            Controls.Add(root);
            ShowPage(false);
            SetControls();
        }
        private void StyleTab(Button button, string text, EventHandler handler)
        {
            button.Text = text; button.Dock = DockStyle.Fill; button.Margin = Padding.Empty;
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand; button.Click += handler;
        }
        private void ShowPage(bool settings)
        {
            settingsPage.Visible = settings; homePage.Visible = !settings;
            if (settings) settingsPage.BringToFront(); else homePage.BringToFront();
            homeTab.BackColor = settings ? accent : Color.White;
            homeTab.ForeColor = settings ? Color.White : accent;
            settingsTab.BackColor = settings ? Color.White : accent;
            settingsTab.ForeColor = settings ? accent : Color.White;
        }
        private void BuildHome()
        {
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 4),
                ColumnCount = 1, RowCount = 5 };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            summary.Text = "今日任务：等待登录";
            summary.Dock = DockStyle.Fill; summary.TextAlign = ContentAlignment.MiddleLeft;
            summary.ForeColor = accent; summary.AutoEllipsis = true;
            scoreSummary.Dock = DockStyle.Fill; scoreSummary.TextAlign = ContentAlignment.MiddleLeft;
            scoreSummary.Font = new Font(Font, FontStyle.Bold);
            scoreSummary.AutoEllipsis = true;
            UpdateScoreSummary(null, null);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
            var logs = new GroupBox { Text = "任务日志", Dock = DockStyle.Fill, Padding = new Padding(8),
                Margin = new Padding(0, 0, 7, 0) };
            eventLog.Dock = DockStyle.Fill; eventLog.BorderStyle = BorderStyle.None;
            eventLog.HorizontalScrollbar = true; eventLog.IntegralHeight = false;
            eventLog.BackColor = Color.White; eventLog.ForeColor = Color.FromArgb(79, 85, 94);
            logs.Controls.Add(eventLog);
            var results = new GroupBox { Text = "平台状态", Dock = DockStyle.Fill, Padding = new Padding(10, 8, 8, 6),
                Margin = Padding.Empty };
            var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            foreach (string kind in new[] { "login", "article", "video", "exam" })
            {
                rows.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
                var label = new Label { Text = NameOf(kind) + "\r\n等待登录", Dock = DockStyle.Fill,
                    ForeColor = Color.FromArgb(78, 85, 95), TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true, Margin = new Padding(0, 0, 0, 4) };
                scores[kind] = label; rows.Controls.Add(label);
            }
            results.Controls.Add(rows);
            columns.Controls.Add(logs, 0, 0); columns.Controls.Add(results, 1, 0);
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            status.AutoEllipsis = true; status.Text = "正在启动";
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
                Padding = new Padding(0, 4, 0, 0) };
            ConfigureButton(login, "扫码登录", async delegate { await ReLogin(); });
            ConfigureButton(start, "开始任务", async delegate
            {
                if (busy || actionPending) return;
                actionPending = true;
                try
                {
                    armed = true;
                    if (loggedIn) await Begin();
                    else { ShowLogin(); web.CoreWebView2.Navigate(LoginUrl); Say("等待扫码，登录后自动开始"); }
                }
                finally { actionPending = false; }
            });
            ConfigureButton(stop, "停止", delegate { Halt("已停止"); });
            ConfigureButton(refresh, "刷新进度", async delegate
            {
                if (!initialized || !loggedIn || running || busy || actionPending) return;
                int stamp = generation; actionPending = true;
                try { await LoadProgress(stamp); if (Valid(stamp)) Say("已刷新平台进度"); }
                catch { if (Valid(stamp)) Say("进度读取失败，请重新登录或稍后重试"); }
                finally { actionPending = false; }
            });
            var logout = new LinkLabel { Text = "退出登录", AutoSize = true, Margin = new Padding(10, 7, 0, 0),
                LinkColor = Color.FromArgb(104, 108, 115), ActiveLinkColor = accent };
            logout.LinkClicked += delegate { Logout(false); };
            buttons.Controls.AddRange(new Control[] { login, start, stop, refresh, logout });
            body.Controls.Add(scoreSummary, 0, 0); body.Controls.Add(summary, 0, 1); body.Controls.Add(columns, 0, 2);
            body.Controls.Add(status, 0, 3); body.Controls.Add(buttons, 0, 4);
            homePage.Controls.Add(body);
            start.BackColor = accent; start.ForeColor = Color.White; start.FlatStyle = FlatStyle.Flat;
            start.FlatAppearance.BorderSize = 0;
        }
        private void BuildSettings()
        {
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15, 14, 15, 12),
                ColumnCount = 1, RowCount = 3 };
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            var group = new GroupBox { Text = "任务选项", Dock = DockStyle.Fill };
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 4,
                Height = 184, Padding = new Padding(16, 15, 8, 0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            foreach (string kind in new[] { "article", "video", "exam" })
            {
                var check = new CheckBox { Text = NameOf(kind), AutoSize = true, Dock = DockStyle.Fill,
                    Checked = preferences.TaskEnabled(kind) };
                int index = tasks.Count; tasks[kind] = check;
                grid.Controls.Add(check, index % 2, index / 2);
            }
            autoRun.Text = "登录后自动执行"; autoRun.Checked = preferences.AutoRun;
            autoLogout.Text = "完成后退出登录"; autoLogout.Checked = preferences.AutoLogout;
            mute.Text = "静音播放"; mute.Checked = preferences.Mute;
            foreach (CheckBox check in new[] {autoRun, autoLogout, mute}) { check.AutoSize = true; check.Dock = DockStyle.Fill; }
            grid.Controls.Add(autoRun, 1, 1); grid.Controls.Add(autoLogout, 0, 2); grid.Controls.Add(mute, 1, 2);
            autoRun.CheckedChanged += delegate { armed = autoRun.Checked; };
            mute.CheckedChanged += delegate { if (initialized) web.CoreWebView2.IsMuted = mute.Checked; };
            group.Controls.Add(grid);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            ConfigureButton(saveSettings, "保存设置", delegate
            {
                try
                {
                    preferences.Article = tasks["article"].Checked; preferences.Video = tasks["video"].Checked;
                    preferences.Exam = tasks["exam"].Checked; preferences.AutoRun = autoRun.Checked;
                    preferences.AutoLogout = autoLogout.Checked; preferences.Mute = mute.Checked;
                    preferences.Save(Path.Combine(dataRoot, "settings.json"));
                    Say("设置已保存"); ShowPage(false);
                }
                catch { Say("设置保存失败，请检查目录权限"); }
            });
            saveSettings.BackColor = accent; saveSettings.ForeColor = Color.White;
            saveSettings.FlatStyle = FlatStyle.Flat; saveSettings.FlatAppearance.BorderSize = 0;
            actions.Controls.Add(saveSettings);
            var version = new Label { Text = "学习强国 · 2.11.2", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray };
            body.Controls.Add(group, 0, 0); body.Controls.Add(actions, 0, 1); body.Controls.Add(version, 0, 2);
            settingsPage.Controls.Add(body);
        }
        private void AddLog(string message)
        {
            if (closing || message == lastLog || message.StartsWith("文章浏览中")
                || message.StartsWith("播放中") || message.StartsWith("已填写第") || message.StartsWith("已确认第")) return;
            lastLog = message;
            eventLog.Items.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            if (eventLog.Items.Count > 500) eventLog.Items.RemoveAt(0);
            eventLog.TopIndex = eventLog.Items.Count - 1;
        }
        private void UpdateScoreSummary(double? total, double? today)
        {
            scoreSummary.Text = "总积分：" + ScoreSummary.Display(total) + "    今日积分：" + ScoreSummary.Display(today);
        }
        private void ShowLogin()
        {
            if (qrLogin != null && !qrLogin.IsDisposed)
            {
                if (Visible && !qrLogin.Visible) qrLogin.Show(this);
                qrLogin.CenterOnOwner();
                if (Visible) qrLogin.Activate();
                return;
            }
            qrLogin = new QrLoginForm { Icon = Program.AppIcon() };
            qrLogin.RefreshRequested += async delegate { await RefreshLogin(); };
            qrLogin.FormClosed += delegate
            {
                if (!closing && !loggedIn && !qrLogin.Completed)
                {
                    Halt("已取消扫码登录");
                    ClearLoginSession();
                }
                ClearLoginImages();
            };
            ResetQrPreview();
            if (smokePath != null) qrLogin.Opacity = 0;
            qrLogin.Show(this);
            if (!Visible) qrLogin.Hide();
        }
        private void CloseLogin()
        {
            if (qrLogin != null && !qrLogin.IsDisposed) { qrLogin.Completed = true; qrLogin.Close(); }
            ClearLoginImages();
        }
        private void Logout(bool keepResults)
        {
            Halt(keepResults ? "任务已完成，已退出登录" : "已退出登录");
            loggedIn = false; account = "";
            CloseLogin();
            ClearLoginSession();
            if (initialized) web.CoreWebView2.Navigate("about:blank");
            identity.Text = keepResults ? "已退出登录 · 保留本轮结果" : "未登录";
            if (!keepResults)
            {
                UpdateScoreSummary(null, null);
                progress.Clear(); summary.Text = "今日任务：等待登录";
                foreach (var pair in scores) pair.Value.Text = NameOf(pair.Key) + "\r\n等待登录";
            }
            SetControls();
        }
    }

    internal sealed class BrowserHostForm : Form
    {
        private bool interactive;
        private const int GwlExStyle = -20;
        private const long WsExNoActivate = 0x08000000L;
        private const long WsExTransparent = 0x00000020L;
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);
        internal bool VisuallyHidden
        {
            get
            {
                uint key, flags; byte alpha;
                return IsHandleCreated && Visible && Opacity == 0 && !ShowInTaskbar
                    && GetLayeredWindowAttributes(Handle, out key, out alpha, out flags)
                    && (flags & 2) != 0 && alpha == 0;
            }
        }
        internal bool InteractiveExamWindow
        {
            get
            {
                if (!IsHandleCreated || !Visible || !ShowInTaskbar || Opacity != 1) return false;
                long style = GetWindowLongPtr(Handle, GwlExStyle).ToInt64();
                return (style & (WsExNoActivate | WsExTransparent)) == 0;
            }
        }
        internal BrowserHostForm()
        {
            ClientSize = new Size(1100, 800);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; Opacity = 0;
        }
        internal void ShowExam(Form owner)
        {
            interactive = true;
            Text = "每日答题（需要手动处理）";
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.FromControl(owner).WorkingArea;
            Size = new Size(Math.Min(1020, area.Width), Math.Min(800, area.Height));
            Location = WindowPlacement.Center(owner.Bounds, Size, area);
            SetInteractiveStyles(true);
            Opacity = 1;
            if (!Visible) Show();
            Activate();
        }
        internal void HideExam()
        {
            if (!interactive) return;
            Opacity = 0;
            WindowState = FormWindowState.Normal;
            interactive = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            SetInteractiveStyles(false);
            ClientSize = new Size(1100, 800);
            Location = Screen.FromControl(this).WorkingArea.Location;
            if (!Visible) Show();
        }
        private void SetInteractiveStyles(bool value)
        {
            if (!IsHandleCreated) return;
            long style = GetWindowLongPtr(Handle, GwlExStyle).ToInt64();
            style = value ? style & ~WsExNoActivate & ~WsExTransparent
                : style | WsExNoActivate | WsExTransparent;
            SetWindowLongPtr(Handle, GwlExStyle, new IntPtr(style));
        }
        protected override bool ShowWithoutActivation { get { return !interactive; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                if (interactive) parameters.ExStyle &= ~(0x00000080 | 0x08000000 | 0x00000020);
                else parameters.ExStyle |= 0x00000080 | 0x08000000 | 0x00000020;
                return parameters;
            }
        }
    }

    internal sealed class QrLoginForm : Form
    {
        internal event EventHandler RefreshRequested;
        internal bool Completed;
        internal readonly PictureBox Picture = new PictureBox();
        private readonly Label hint = new Label();
        private string lastPng = "";
        private Form centeredOwner;
        internal QrLoginForm()
        {
            Text = "扫码登录"; Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(340, 382); FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            var header = new Label { Text = "扫码登录", ForeColor = Color.White, BackColor = Color.FromArgb(221, 35, 44),
                Dock = DockStyle.Top, Height = 35, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0) };
            Picture.Bounds = new Rectangle(30, 48, 280, 280); Picture.SizeMode = PictureBoxSizeMode.Zoom;
            Picture.BackColor = Color.White;
            hint.Text = "正在获取二维码"; hint.Bounds = new Rectangle(16, 330, 225, 36);
            hint.TextAlign = ContentAlignment.MiddleLeft; hint.ForeColor = Color.FromArgb(91, 98, 107);
            var refresh = new LinkLabel { Text = "刷新二维码", Bounds = new Rectangle(241, 330, 88, 36),
                TextAlign = ContentAlignment.MiddleCenter, LinkColor = Color.FromArgb(196, 33, 41) };
            refresh.LinkClicked += delegate { if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty); };
            Controls.AddRange(new Control[] { Picture, hint, refresh, header });
        }
        internal void CenterOnOwner()
        {
            if (Owner == null || Owner.IsDisposed || Owner.WindowState == FormWindowState.Minimized) return;
            Location = WindowPlacement.Center(Owner.Bounds, Size, Screen.FromControl(Owner).WorkingArea);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            centeredOwner = Owner;
            if (centeredOwner != null)
            {
                centeredOwner.LocationChanged += OwnerMoved;
                centeredOwner.SizeChanged += OwnerMoved;
            }
            CenterOnOwner();
        }
        private void OwnerMoved(object sender, EventArgs e) { CenterOnOwner(); }
        internal bool SetQr(string png)
        {
            if (png == lastPng) return false;
            if (!png.StartsWith("data:image/png;base64,", StringComparison.Ordinal) || png.Length > 2800000)
                throw new FormatException("Unexpected QR image");
            byte[] data = Convert.FromBase64String(png.Substring("data:image/png;base64,".Length));
            using (var stream = new MemoryStream(data))
            using (var decoded = Image.FromStream(stream))
            {
                if (decoded.Width < 100 || decoded.Height < 100 || decoded.Width > 1200 || decoded.Height > 1200
                    || Math.Abs((double)decoded.Width / decoded.Height - 1) > 0.15) throw new FormatException("Unexpected QR dimensions");
                var old = Picture.Image; Picture.Image = new Bitmap(decoded); if (old != null) old.Dispose();
            }
            lastPng = png; hint.Text = "请使用学习强国 App 扫码";
            return true;
        }
        internal void Reset(string message)
        {
            lastPng = ""; var old = Picture.Image; Picture.Image = null;
            if (old != null) old.Dispose(); hint.Text = message;
        }
        internal void Message(string text) { hint.Text = text; }
        protected override void Dispose(bool disposing)
        {
            if (disposing && centeredOwner != null)
            {
                centeredOwner.LocationChanged -= OwnerMoved;
                centeredOwner.SizeChanged -= OwnerMoved;
                centeredOwner = null;
            }
            if (disposing && Picture.Image != null) { Picture.Image.Dispose(); Picture.Image = null; }
            base.Dispose(disposing);
        }
    }
}
