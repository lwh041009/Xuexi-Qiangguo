using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace StudyLite
{
    internal sealed partial class MainForm
    {
        private readonly Dictionary<string, string> loginImages = new Dictionary<string, string>();
        private int qrGeneration;
        private double qrStarted;

        private void ClearLoginImages()
        {
            qrGeneration++;
            loginImages.Clear();
        }
        private void ResetQrPreview()
        {
            ClearLoginImages();
            qrStarted = clock.Elapsed.TotalSeconds;
            if (qrLogin != null && !qrLogin.IsDisposed) qrLogin.Reset("正在获取二维码");
        }
        private async Task RefreshLogin()
        {
            if (!initialized || loggedIn || closing) return;
            bool pending = armed;
            Halt("正在刷新二维码");
            armed = pending;
            ResetQrPreview();
            web.CoreWebView2.Navigate(LoginUrl);
            await Task.CompletedTask;
        }
        private async void CaptureLoginImage(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (loggedIn || closing || qrLogin == null || qrLogin.IsDisposed || e.Response.StatusCode != 200) return;
            Uri uri;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out uri) || !Rules.Navigation(uri.AbsoluteUri)) return;
            int epoch = qrGeneration;
            try
            {
                if (!e.Response.Headers.GetHeader("Content-Type").StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return;
                using (var stream = await e.Response.GetContentAsync())
                using (var memory = new MemoryStream())
                {
                    if (closing || epoch != qrGeneration) return;
                    byte[] buffer = new byte[16384];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        if (memory.Length + read > 1000000) return;
                        memory.Write(buffer, 0, read);
                    }
                    if (closing || epoch != qrGeneration) return;
                    memory.Position = 0;
                    using (var image = Image.FromStream(memory))
                    using (var output = new MemoryStream())
                    {
                        if (image.Width < 100 || image.Width > 1200 || image.Height < 100 || image.Height > 1200
                            || Math.Abs((double)image.Width / image.Height - 1) > 0.15) return;
                        image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                        if (loginImages.Count >= 8) loginImages.Remove(loginImages.Keys.First());
                        loginImages[uri.AbsoluteUri] = "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
                    }
                }
            }
            catch { }
        }
        private bool ApplyQrResult(object result, int epoch)
        {
            if (epoch != qrGeneration || closing || loggedIn || qrLogin == null || qrLogin.IsDisposed) return false;
            if (Object.Equals(Json.Get(result, "verification"), true))
            {
                qrLogin.Reset("登录需要手动验证");
                Say("登录需要验证，已暂停；请通过官方页面处理");
                return false;
            }
            if (Object.Equals(Json.Get(result, "expired"), true))
            {
                qrLogin.Reset("二维码已过期，请刷新");
                return false;
            }
            string png = Json.Text(Json.Get(result, "png"));
            if (png.Length == 0)
            {
                string source = Json.Text(Json.Get(result, "source"));
                if (!loginImages.TryGetValue(source, out png)) return false;
            }
            try { qrLogin.SetQr(png); return true; }
            catch { return false; }
        }
        private async Task RefreshQrPreview(int stamp)
        {
            if (!navigationReady || qrLogin == null || qrLogin.IsDisposed || loggedIn || closing) return;
            int epoch = qrGeneration;
            string script = Program.Asset("login-preview.js");
            try
            {
                object result = Json.Parse(await web.CoreWebView2.ExecuteScriptAsync(script));
                if (!Valid(stamp) || epoch != qrGeneration) return;
                if (ApplyQrResult(result, epoch)) return;
            }
            catch { }
            foreach (var frame in frames.ToArray())
            {
                if (!Valid(stamp) || epoch != qrGeneration) return;
                try
                {
                    object result = Json.Parse(await frame.ExecuteScriptAsync(script));
                    if (!Valid(stamp) || epoch != qrGeneration) return;
                    if (ApplyQrResult(result, epoch)) return;
                }
                catch { }
            }
            if (clock.Elapsed.TotalSeconds - qrStarted > 35 && qrLogin != null && !qrLogin.IsDisposed
                && qrLogin.Picture.Image == null) qrLogin.Message("二维码加载较慢，请刷新");
        }
        internal async Task VerifyQrView(string output)
        {
            ResetQrPreview();
            for (int i = 0; i < 60 && qrLogin.Picture.Image == null; i++)
            {
                await Task.Delay(100);
                await RefreshQrPreview(generation);
            }
            if (qrLogin.Picture.Image == null) throw new InvalidOperationException("QR extraction from hidden browser failed");
            using (var bitmap = new Bitmap(qrLogin.Width, qrLogin.Height))
            {
                qrLogin.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                if (bitmap.GetPixel(24, 55).R > 240)
                    throw new InvalidOperationException("QR dialog header did not render");
                bitmap.Save(Path.Combine(output, "qr-window.png"));
            }
            // Invalid images must not replace the current authentic QR.
            bool rejected = false;
            try { qrLogin.SetQr("data:image/svg+xml;base64,PHN2Zy8+"); }
            catch (FormatException) { rejected = true; }
            if (!rejected || !engineWindow.VisuallyHidden) throw new InvalidOperationException("QR-only view contract failed");
            Rectangle original = Bounds;
            AssertQrCentered();
            Location = new Point(Left + 24, Top + 16);
            await Task.Delay(100);
            AssertQrCentered();
            Size = new Size(Width + 32, Height + 20);
            await Task.Delay(100);
            AssertQrCentered();
            Bounds = original;
            await Task.Delay(100);
            AssertQrCentered();
            int previous = qrGeneration;
            ResetQrPreview();
            if (qrLogin.Picture.Image != null) throw new InvalidOperationException("QR refresh did not clear old image");
            if (ApplyQrResult(new { png = "data:image/png;base64,stale" }, previous))
                throw new InvalidOperationException("Stale QR preview was accepted");
            await RefreshQrPreview(generation);
            if (qrLogin.Picture.Image == null) throw new InvalidOperationException("QR refresh failed");
            CloseLogin();
            ShowLogin();
            await Task.Delay(100);
            AssertQrCentered();
            CloseLogin();
        }
        private void AssertQrCentered()
        {
            Point expected = WindowPlacement.Center(Bounds, qrLogin.Size, System.Windows.Forms.Screen.FromControl(this).WorkingArea);
            if (qrLogin.Location != expected) throw new InvalidOperationException("QR window is not centered over its owner");
        }
    }

    internal static class QrVerification
    {
        internal const string PageUrl = "https://pc.xuexi.cn/__study_lite_qr_test__";
        // A public, non-login fixture encoding "StudyLite offline login UI test".
        private const string Qr = "iVBORw0KGgoAAAANSUhEUgAAAXIAAAFyAQAAAADAX2ykAAAC8klEQVR4nO1aUY6jMAx9MUj9bKU5QI9CbzBHGu2R9gbkKHOAlchnJZBXdkLL8AFhtGVh8PugSf1UWbw6cew4xhJ4WkQHjD8NmrGPYfxp0Ix9DONPg2bsYxh/GjRjH8P4R3g/LqGMI3h5IJQAQm+7rehPPmgB98D8igWNjJqCo5b+encACjXxtv3PBh2UHx4RGlEwqk+J3xjd5db9zwUdnM+/JGqrpnPwF+CL5v/DnznQLOPY/HI0dxK17K9tNDDCuv6Q8V+i71k22CCCguGAk8yL1lW/9btv/z4Zfwvvx2uKrAuyZM1VA7gPSbeALqbP6/qTDcqnHpJf6vMZoQy0/SikcF7THzL+P30/iIefipOqOuL6OTqLVQ9OQqzt/U+DtqkvzqplG2XUQzCz6CuP5yHY9N1rfhVO7GTGQJe+8+9NXJsZ4W0tf8j4r6lf3V1ci2NVEp0kVJ3UPAp27nq3+uQ+/5/lI0KlYOXA/p3h/KWQZblgh3DRDEuiew1/yPiv6C/cZOuVTdh9SFUypluhjJmWcxcN5+//fj5oAVdg/Oz8mTVr1kxLOg3pEZMspVh+tb//G1JerKr2ByKutb/Aqac0mNr5aJf6Ar2qGsQpapv+EMzxuGT67lpfDJZm1RKpqvHkWfzuVt/2MU0FjWSPBluf997fP8UkS/sLXJ+1CYxeWmkHr+tPLiibeUw+BvVJ2WbTTtwv1wNYfrVfPidpvwrapFV50H3YqP/5oEPWr1yaSdUqyOTclewvf0ogvLXw7rEdb81/Mv6y+5PQpVnzqyCXOGJhS29SDvjZIONv7f5k57iW/kLF93QTWlfqdf3JBWUzIw7Prz5PrEVod4vxK+lWsPs5P+T+JPvr434OWmknye58bktX1Vv0n4y/6P4kpL8v0vaCQtr9sfsvsPc5DdoYv4wfPspXSKw+jZ1YT6xK8zb9J+MvvT+JPn9Gyqrs/nM+aAFXYPxp0Ix9DONPg2bsYxh/GjRjH8P406AZ+0/j/wVzK566bY7iBQAAAABJRU5ErkJggg==";
        internal static string Response(string url)
        {
            if (url != PageUrl) return null;
            return "<!doctype html><meta charset='utf-8'><title>Offline QR fixture</title><body>"
                + "<h1>This whole page must never appear in the login dialog</h1>"
                + "<div class='qrcode-box'><img id='qrcode' width='240' height='240' src='data:image/png;base64,"
                + Qr + "'></div></body>";
        }
        internal static Task Run(MainForm form, string output) { return form.VerifyQrView(output); }
    }
}
