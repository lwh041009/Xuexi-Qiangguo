using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace StudyLite
{
    // The vault only supplements session cookies from this app's own WebView2 profile.
    internal static class LoginVault
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StudyDeskLite.Session.v1");
        internal static bool OfficialDomain(string domain)
        {
            domain = (domain ?? "").TrimStart('.');
            return domain.Equals("xuexi.cn", StringComparison.OrdinalIgnoreCase)
                || domain.EndsWith(".xuexi.cn", StringComparison.OrdinalIgnoreCase);
        }
        internal static void Save(string path, object[] cookies)
        {
            cookies = cookies.Where(c => OfficialDomain(Json.Text(Json.Get(c, "domain")))
                && Json.Text(Json.Get(c, "name")).Length > 0
                && Json.Text(Json.Get(c, "value")).Length > 0).Take(300).ToArray();
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.Encode(new {
                version = 1, saved = DateTime.UtcNow.ToString("o"), cookies = cookies
            })), Entropy, DataProtectionScope.CurrentUser);
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        internal static object[] Load(string path)
        {
            if (!File.Exists(path)) return new object[0];
            try
            {
                if (new FileInfo(path).Length > 2000000) return new object[0];
                object value = Json.Parse(Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser)));
                DateTime saved;
                if (Json.Number(Json.Get(value, "version")) != 1
                    || !DateTime.TryParse(Json.Text(Json.Get(value, "saved")), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out saved)
                    || DateTime.UtcNow - saved.ToUniversalTime() > TimeSpan.FromDays(7)
                    || saved.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5)) return new object[0];
                return Json.Array(Json.Get(value, "cookies")).Where(c =>
                    OfficialDomain(Json.Text(Json.Get(c, "domain")))
                    && Json.Text(Json.Get(c, "name")).Length > 0
                    && Json.Text(Json.Get(c, "value")).Length > 0).Take(300).ToArray();
            }
            catch { return new object[0]; }
        }
        internal static void Clear(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }

    internal sealed partial class MainForm
    {
        private int loginSessionEpoch;
        private bool loginRecoveryPending;
        private string LoginVaultPath { get { return Path.Combine(dataRoot, "login-session.dat"); } }

        private void ClearLoginSession()
        {
            loginSessionEpoch++;
            loginRecoveryPending = false;
            try { LoginVault.Clear(LoginVaultPath); }
            catch { Say("清除本地登录缓存失败，请检查目录权限"); }
            if (initialized) web.CoreWebView2.CookieManager.DeleteAllCookies();
        }

        private async Task SaveLoginSession()
        {
            if (smokePath != null || !initialized || !loggedIn || closing) return;
            int epoch = loginSessionEpoch;
            try
            {
                var cookies = await web.CoreWebView2.CookieManager.GetCookiesAsync(null);
                if (epoch != loginSessionEpoch || !loggedIn || closing) return;
                var scoped = cookies.Where(c => LoginVault.OfficialDomain(c.Domain)).ToArray();
                if (!scoped.Any(c => c.Name == "__UID__" && c.Value.Length > 0)
                    || !scoped.Any(c => c.Name == "token" && c.Value.Length > 0)) return;
                LoginVault.Save(LoginVaultPath, SnapshotCookies(scoped));
            }
            catch { Say("本次登录缓存保存失败，不影响当前任务"); }
        }

        private static object[] SnapshotCookies(IEnumerable<CoreWebView2Cookie> cookies)
        {
            return cookies.Where(c => LoginVault.OfficialDomain(c.Domain)).Select(c => (object)new {
                    name = c.Name, value = c.Value, domain = c.Domain, path = c.Path,
                    secure = c.IsSecure, httpOnly = c.IsHttpOnly, session = c.IsSession,
                    expires = c.IsSession ? "" : c.Expires.ToUniversalTime().ToString("o"),
                    sameSite = (int)c.SameSite
                }).Select(c => Json.Parse(Json.Encode(c))).ToArray();
        }

        private async Task RestoreSavedCookies()
        {
            int epoch = loginSessionEpoch;
            var manager = web.CoreWebView2.CookieManager;
            var existing = await manager.GetCookiesAsync(null);
            if (closing || epoch != loginSessionEpoch) return;
            foreach (object record in LoginVault.Load(LoginVaultPath))
            {
                string name = Json.Text(Json.Get(record, "name")), domain = Json.Text(Json.Get(record, "domain"));
                string path = Json.Text(Json.Get(record, "path"));
                if (existing.Any(c => c.Name == name && c.Domain == domain && c.Path == path)) continue;
                try
                {
                    var cookie = manager.CreateCookie(name, Json.Text(Json.Get(record, "value")), domain, path);
                    cookie.IsSecure = Object.Equals(Json.Get(record, "secure"), true);
                    cookie.IsHttpOnly = Object.Equals(Json.Get(record, "httpOnly"), true);
                    int sameSite = (int)Json.Number(Json.Get(record, "sameSite"));
                    if (!Enum.IsDefined(typeof(CoreWebView2CookieSameSiteKind), sameSite)) continue;
                    cookie.SameSite = (CoreWebView2CookieSameSiteKind)sameSite;
                    if (!Object.Equals(Json.Get(record, "session"), true))
                    {
                        DateTime expires;
                        if (!DateTime.TryParse(Json.Text(Json.Get(record, "expires")), null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out expires)
                            || expires.ToUniversalTime() <= DateTime.UtcNow) continue;
                        cookie.Expires = expires.ToUniversalTime();
                    }
                    manager.AddOrUpdateCookie(cookie);
                }
                catch { /* Ignore a damaged or unsupported cookie, never weaken its attributes. */ }
            }
        }

        private void PromptIfLoginMissing()
        {
            if (!loginRecoveryPending || loggedIn || closing) return;
            loginRecoveryPending = false;
            ShowLogin();
            web.CoreWebView2.Navigate(LoginUrl);
            Say("等待扫码登录");
        }

        private async Task RestoreLogin()
        {
            busy = true;
            loginRecoveryPending = true;
            int stamp = generation;
            try
            {
                await RestoreSavedCookies();
                if (!Valid(stamp)) return;
                Say("正在检查上次登录状态");
                try { await CheckLogin(stamp); }
                catch (UnauthorizedAccessException)
                {
                    if (!Valid(stamp)) return;
                    LoseLogin(); loginRecoveryPending = true;
                }
                catch { Say("暂时无法校验登录，请检查网络；已保留登录缓存"); return; }
                PromptIfLoginMissing();
            }
            finally { busy = false; }
        }
    }
}
