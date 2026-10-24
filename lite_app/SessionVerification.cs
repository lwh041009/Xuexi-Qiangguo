using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace StudyLite
{
    internal sealed partial class MainForm
    {
        private static void SessionCheck(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Session fixture: " + label);
        }

        private async Task VerifyLoginSession()
        {
            SessionCheck(!web.CoreWebView2.Profile.IsInPrivateModeEnabled, "persistent profile");
            var manager = web.CoreWebView2.CookieManager;
            if (sessionTestStage == "seed")
            {
                manager.DeleteAllCookies();
                var uid = manager.CreateCookie("__UID__", "fixture-user", ".xuexi.cn", "/");
                uid.IsSecure = true; uid.Expires = DateTime.UtcNow.AddDays(2);
                uid.SameSite = CoreWebView2CookieSameSiteKind.Lax;
                var token = manager.CreateCookie("token", "synthetic-session-not-a-real-login", ".xuexi.cn", "/");
                token.IsSecure = true; token.IsHttpOnly = true;
                token.SameSite = CoreWebView2CookieSameSiteKind.Lax;
                manager.AddOrUpdateCookie(uid); manager.AddOrUpdateCookie(token);
                var foreign = manager.CreateCookie("foreign", "must-not-be-saved", "example.invalid", "/");
                manager.AddOrUpdateCookie(foreign);
                object[] cookies = SnapshotCookies(await manager.GetCookiesAsync(null));
                SessionCheck(cookies.Length == 2, "only official cookies captured");
                object expired = Json.Parse(Json.Encode(new { name = "expired", value = "expired-fixture", domain = ".xuexi.cn",
                    path = "/", secure = true, httpOnly = true, session = false,
                    sameSite = (int)CoreWebView2CookieSameSiteKind.Lax, expires = DateTime.UtcNow.AddDays(-1).ToString("o") }));
                LoginVault.Save(LoginVaultPath, cookies.Concat(new[] { expired }).ToArray());
                manager.DeleteCookie(foreign);
                SessionCheck(!Encoding.UTF8.GetString(File.ReadAllBytes(LoginVaultPath)).Contains("synthetic-session"), "encrypted file");
            }
            else if (sessionTestStage == "after-logout")
            {
                await RestoreSavedCookies();
                SessionCheck(!File.Exists(LoginVaultPath) && !(await manager.GetCookiesAsync(null)).Any(), "logout persists over restart");
            }
            else
            {
                var persisted = await manager.GetCookiesAsync(null);
                SessionCheck(persisted.Any(c => c.Name == "__UID__" && c.Value == "fixture-user"), "browser profile survives process restart");
                // Force the session-cookie fallback to prove that the vault, not browser session recovery, restores it.
                manager.DeleteAllCookies();
                await RestoreSavedCookies();
                var restored = await manager.GetCookiesAsync(null);
                SessionCheck(restored.Count == 2 && !restored.Any(c => c.Name == "expired" || c.Name == "foreign"), "expired and foreign cookies excluded");
                var token = restored.Single(c => c.Name == "token");
                SessionCheck(token.Value == "synthetic-session-not-a-real-login" && token.IsSession
                    && token.IsSecure && token.IsHttpOnly && token.SameSite == CoreWebView2CookieSameSiteKind.Lax,
                    "session value and security attributes preserved");
                var uid = restored.Single(c => c.Name == "__UID__");
                SessionCheck(!uid.IsSession && uid.Expires.ToUniversalTime() > DateTime.UtcNow.AddDays(1), "persistent expiry preserved");
                if (sessionTestStage == "logout")
                {
                    Logout(false);
                    SessionCheck(!File.Exists(LoginVaultPath) && !(await manager.GetCookiesAsync(null)).Any(), "explicit logout clears profile and vault");
                }
            }
            File.WriteAllText(Path.Combine(smokePath, "session-" + sessionTestStage + ".json"), Json.Encode(new {
                ok = true, stage = sessionTestStage, externalNetwork = false, syntheticCredentialsOnly = true,
                persistentProfile = true, encryptedVault = true
            }), Encoding.UTF8);
        }
    }
}
