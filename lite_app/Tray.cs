using System;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace StudyLite
{
    internal sealed partial class MainForm
    {
        private NotifyIcon tray;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayShow, trayExit;
        private bool exitRequested, exitPending, trayTesting, restoreExam, restoring;
        private FormWindowState restoreState = FormWindowState.Normal;

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip();
            trayShow = new ToolStripMenuItem("显示窗口", null, delegate { RestoreFromTray(); });
            trayExit = new ToolStripMenuItem("退出", null, async delegate { await ExitApplication(); });
            trayMenu.Items.Add(trayShow);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(trayExit);
            tray = new NotifyIcon { Icon = Icon, Text = "学习强国", ContextMenuStrip = trayMenu,
                Visible = smokePath == null };
            tray.MouseClick += OnTrayMouseClick;
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && !closing && !exitRequested
                    && (smokePath == null || trayTesting)) HideToTray();
                else if (!restoring && Visible && WindowState != FormWindowState.Minimized) restoreState = WindowState;
            };
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left) RestoreFromTray();
        }

        private void VerifyTrayClick(MouseButtons button)
        {
            typeof(NotifyIcon).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic).Invoke(tray,
                    new object[] { new MouseEventArgs(button, 1, 0, 0, 0) });
        }

        private void HideToTray()
        {
            if (closing || exitPending) return;
            tray.Visible = true;
            if (engineWindow.InteractiveExamWindow)
            {
                restoreExam = true;
                engineWindow.HideExam();
            }
            if (qrLogin != null && !qrLogin.IsDisposed) qrLogin.Hide();
            Hide();
        }

        private void RestoreFromTray()
        {
            if (closing || exitPending) return;
            restoring = true;
            try { Show(); WindowState = restoreState; }
            finally { restoring = false; }
            Activate();
            if (qrLogin != null && !qrLogin.IsDisposed)
            {
                if (!qrLogin.Visible) qrLogin.Show(this);
                qrLogin.CenterOnOwner();
                qrLogin.Activate();
            }
            if (restoreExam)
            {
                restoreExam = false;
                engineWindow.ShowExam(this);
            }
        }

        private bool InterceptClose(FormClosingEventArgs args)
        {
            if (args.CloseReason != CloseReason.UserClosing || exitRequested
                || (smokePath != null && !trayTesting)) return false;
            args.Cancel = true;
            HideToTray();
            return true;
        }

        private async Task ExitApplication()
        {
            if (closing || exitPending) return;
            exitPending = true;
            try { await SaveLoginSession(); }
            finally { exitRequested = true; Close(); }
        }

        private void DisposeTray()
        {
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (trayMenu != null) trayMenu.Dispose();
        }

        private async Task<bool> VerifyTray()
        {
            trayTesting = true;
            try
            {
                int stamp = generation;
                bool wasRunning = running;
                Show(); WindowState = FormWindowState.Normal;
                await Task.Delay(150);
                Close();
                await Task.Delay(150);
                RequireTray(!Visible && !IsDisposed && !closing && tray.Visible, "close hides");
                VerifyTrayClick(MouseButtons.Right);
                RequireTray(!Visible, "right click does not restore before menu selection");
                VerifyTrayClick(MouseButtons.Left);
                await Task.Delay(150);
                RequireTray(Visible && WindowState == FormWindowState.Normal, "single left click restores normal");
                WindowState = FormWindowState.Minimized;
                await Task.Delay(150);
                RequireTray(!Visible && !closing, "minimize hides");
                trayShow.PerformClick();
                await Task.Delay(150);
                RequireTray(Visible && WindowState == FormWindowState.Normal, "restore minimized");
                WindowState = FormWindowState.Maximized;
                await Task.Delay(150);
                WindowState = FormWindowState.Minimized;
                await Task.Delay(150);
                RequireTray(!Visible, "maximized minimize hides");
                trayShow.PerformClick();
                await Task.Delay(150);
                RequireTray(Visible && WindowState == FormWindowState.Maximized, "restore maximized");
                WindowState = FormWindowState.Normal;
                await VerifyQrTray();
                return generation == stamp && running == wasRunning
                    && trayShow.Text == "显示窗口" && trayExit.Text == "退出";
            }
            finally { trayTesting = false; }
        }

        private async Task VerifyQrTray()
        {
            ShowLogin();
            var originalQr = qrLogin;
            await Task.Delay(100);
            // Restoring an already visible owner must not try to show its visible child again.
            VerifyTrayClick(MouseButtons.Left);
            VerifyTrayClick(MouseButtons.Left);
            RequireTray(qrLogin == originalQr && qrLogin.Visible, "visible QR repeated restore");
            Close();
            await Task.Delay(100);
            RequireTray(!Visible && !qrLogin.Visible, "close hides QR");
            trayShow.PerformClick();
            await Task.Delay(100);
            RequireTray(Visible && qrLogin == originalQr && qrLogin.Visible, "restore owned QR");
            AssertQrCentered();
            WindowState = FormWindowState.Minimized;
            await Task.Delay(100);
            RequireTray(!Visible && !qrLogin.Visible, "minimize hides QR");
            VerifyTrayClick(MouseButtons.Left);
            await Task.Delay(100);
            RequireTray(Visible && qrLogin == originalQr && qrLogin.Visible, "restore minimized QR");
            AssertQrCentered();
            CloseLogin();
            RestoreFromTray();
            RequireTray(qrLogin.IsDisposed, "completed QR stays closed");
        }

        private void RequireTray(bool condition, string step)
        {
            if (!condition) throw new InvalidOperationException("Tray: " + step + "; visible=" + Visible
                + "; state=" + WindowState + "; restore=" + restoreState + "; closing=" + closing);
        }
    }
}
