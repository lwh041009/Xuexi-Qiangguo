using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace StudyLite
{
    internal static class Tests
    {
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
        }
        internal static int Run()
        {
            string path = Path.Combine(Path.GetTempPath(), "study-lite-test-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            try
            {
                Check(Rules.Platform("https://www.xuexi.cn/a"), "allow official");
                Check(!Rules.Platform("https://www.xuexi.cn.evil.test/a"), "reject suffix trick");
                Check(!Rules.Platform("https://xuexi.cn@evil.test/a"), "reject credentials");
                Check(!Rules.Platform("http://www.xuexi.cn/a"), "require HTTPS");
                Check(!Rules.Platform("https://www.xuexi.cn:8443/a"), "require normal port");
                object list = Json.Parse(@"[
                  {""itemId"":""a"",""title"":""one"",""itemType"":""kNews"",""type"":""tuwen"",""url"":""https://www.xuexi.cn/a""},
                  {""itemId"":""a"",""title"":""updated"",""itemType"":""kReading"",""type"":""tuwen"",""url"":""https://www.xuexi.cn/a""},
                  {""itemId"":""v"",""itemType"":""kPureVideo"",""type"":""shipin"",""url"":""https://www.xuexi.cn/v""},
                  {""itemId"":""set"",""itemType"":""kVideoSet4Many"",""type"":""shipin"",""url"":""https://www.xuexi.cn/set""},
                  {""itemId"":""bad"",""itemType"":""kNews"",""type"":""tuwen"",""url"":""https://evil.test/a""},
                  {""itemId"":""gone"",""itemType"":""kNews"",""type"":""tuwen"",""dataValid"":false,""url"":""https://www.xuexi.cn/gone""}
                ]");
                var items = Rules.Catalogue(list);
                Check(items.Count == 2 && items.Count(i => i.Kind == "video") == 1, "typed catalogue and dedup");
                var dated = Rules.Catalogue(Json.Parse(@"[
                  {""itemId"":""100"",""itemType"":""kPureVideo"",""type"":""shipin"",""url"":""https://www.xuexi.cn/old"",""publishTime"":""2024-02-01 10:00:00""},
                  {""itemId"":""999"",""itemType"":""kPureVideo"",""type"":""shipin"",""url"":""https://www.xuexi.cn/new"",""publishTime"":""2026-10-23 10:18:38""}
                ]"));
                Check(dated[0].Id == "999" && dated[0].Published > dated[1].Published, "official text dates sort newest first, not by ID");
                Check(Rules.PublishedTime(1700000000) == Rules.PublishedTime(1700000000000L), "epoch seconds and milliseconds comparable");
                Check(Rules.PublishedTime("bad") == 0, "unknown date sorts last");
                var progress = Rules.TaskProgress(Json.Parse(@"{""ok"":true,""code"":200,""data"":{""taskProgress"":[
                  {""taskCode"":[""1"",""1002""],""currentScore"":2,""dayMaxScore"":12},
                  {""taskCode"":[""2"",""1003""],""currentScore"":12,""dayMaxScore"":12},
                  {""taskCode"":[""6""],""currentScore"":0,""dayMaxScore"":5}]}}"));
                Check(progress.Count == 3 && progress["video"].Done && !progress["article"].Done, "server progress");
                var split = Rules.TaskProgress(Json.Parse(@"{""ok"":true,""code"":200,""data"":{""taskProgress"":[
                    {""taskCode"":[""1""],""currentScore"":1,""dayMaxScore"":6},
                    {""taskCode"":[""1002""],""currentScore"":2,""dayMaxScore"":6}]}}"));
                Check(split["article"].Current == 3 && split["article"].Maximum == 12, "split count and duration progress");
                var loginProgress = Rules.TaskProgress(Json.Parse(@"{""ok"":true,""code"":200,""data"":{""taskProgress"":[
                    {""taskCode"":[""9""],""currentScore"":1,""dayMaxScore"":1}]}}"));
                Check(loginProgress["login"].Done, "login status comes from server");
                var preferences = Preferences.Load(path + ".settings");
                Check(preferences.AutoRun && preferences.Article && preferences.Mute && !preferences.AutoLogout, "settings defaults");
                preferences.Article = false; preferences.AutoLogout = true; preferences.Save(path + ".settings");
                preferences = Preferences.Load(path + ".settings");
                Check(!preferences.Article && preferences.AutoLogout, "settings roundtrip");
                File.WriteAllText(path + ".settings", "not-json");
                Check(Preferences.Load(path + ".settings").Article, "malformed settings fallback");
                bool blocked = false;
                try { Rules.TaskProgress(Json.Parse(@"{""ok"":true,""code"":200,""data"":{""needFaceRecognition"":true}}")); }
                catch (InvalidOperationException) { blocked = true; }
                Check(blocked, "pause identity challenge");
                Check(Json.Number(Json.Get(Json.Unwrap(Json.Parse(@"{""data_str"":""eyJ0b3RhbE51bSI6NX0=""}")), "totalNum")) == 5, "base64 result");
                Check(ExamSubmission.Accepted(Json.Parse(@"{""ok"":true,""code"":200,""data"":{""totalNum"":0}}")), "zero total submission accepted, not proof of credit");
                Check(ExamSubmission.Accepted(Json.Parse(@"{""ok"":true,""code"":200,""data_str"":""eyJ0b3RhbE51bSI6NX0=""}")), "base64 submission accepted");
                foreach (string invalid in new[] { "null", "true", "\"invalid\"", "-1", "101", "1.5" })
                    Check(!ExamSubmission.Accepted(Json.Parse("{\"ok\":true,\"code\":200,\"data\":{\"totalNum\":" + invalid + "}}")), "invalid submission total rejected");
                Check(!ExamSubmission.Accepted(Json.Parse(@"{""ok"":true,""code"":200,""data"":{}}")), "missing total rejected");
                Check(!ExamSubmission.Accepted(Json.Parse(@"{""ok"":false,""code"":200,""data"":{""totalNum"":5}}")), "rejected submission is not completion");
                using (var store = new History(path))
                {
                    var item = items.First();
                    var run = typeof(History).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic);
                    run.Invoke(store, new object[] {
                        "INSERT INTO visits(account,kind,itemId,viewTime,seconds) VALUES(?,?,?,?,?)",
                        new[] { "account-a", item.Kind, item.Id, "legacy", "90" }
                    });
                    Check(!store.Seen("account-a", item), "initial unseen");
                    store.RecordAttempt("account-a", item, 90, 0, 0, "no_credit");
                    Check(!store.Seen("account-a", item), "no-credit attempt remains retryable");
                    Check(store.NoCreditToday("account-a", item) && !store.NoCreditToday("account-b", item), "today no-credit preference is account scoped");
                    var other = new Item { Id = "other-video", Kind = "video" };
                    run.Invoke(store, new object[] {
                        "INSERT INTO attempts(account,kind,itemId,viewTime,outcome) VALUES(?,?,?,?,?)",
                        new[] { "account-a", other.Kind, other.Id, DateTimeOffset.Now.AddDays(-1).ToString("o"), "no_credit" }
                    });
                    Check(!store.NoCreditToday("account-a", other), "yesterday no-credit does not penalize today");
                    Check(new[] {item, other}.OrderBy(i => store.NoCreditToday("account-a", i) ? 1 : 0).First() == other,
                        "untested item is preferred over today's no-credit item");
                    bool rejected = false;
                    try { store.RecordConfirmed("account-a", item, 90, 0, 0); }
                    catch (ArgumentException) { rejected = true; }
                    Check(rejected && !store.Seen("account-a", item), "unchanged score cannot become confirmed");
                    store.RecordConfirmed("account-a", item, 90, 0, 1);
                    store.RecordConfirmed("account-a", item, 90, 0, 1);
                    Check(store.Seen("account-a", item), "stored history");
                    Check(!store.Seen("account-b", item), "account separation");
                    Check((bool)run.Invoke(store, new object[] {
                        "SELECT 1 FROM visits WHERE viewTime=?", new[] { "legacy" }
                    }), "legacy records preserved");
                    item.Id = "'; DROP TABLE visits; --";
                    store.RecordConfirmed("account-a", item, 90, 1, 2);
                    Check(store.Seen("account-a", item), "SQL bound values");
                }
                Check(WindowPlacement.Center(new Rectangle(100, 100, 620, 470), new Size(356, 421),
                    new Rectangle(0, 0, 1920, 1080)) == new Point(232, 124), "owner-centered QR placement");
                Check(WindowPlacement.Center(new Rectangle(-1800, 100, 620, 470), new Size(356, 421),
                    new Rectangle(-1920, 0, 1920, 1080)).X == -1668, "negative monitor coordinates");
                Check(WindowPlacement.Center(new Rectangle(1850, 1000, 620, 470), new Size(356, 421),
                    new Rectangle(0, 0, 1920, 1080)) == new Point(1564, 659), "QR work-area clamping");
                var reading = new ReadingSchedule();
                Check(!reading.Due(59.99) && reading.Due(60), "reading first checks at 60 seconds");
                reading.Checked(60);
                Check(!reading.Due(69.99) && reading.Due(70), "reading polls at 10-second intervals");
                Check(!reading.Expired(119.99) && reading.Expired(120), "reading stops bounded no-credit wait");
                reading.Checked(119);
                Check(reading.Due(120), "reading deadline does not drift with poll timing");
                var videoSchedule = new VideoSchedule();
                Check(!videoSchedule.Due(59.99) && videoSchedule.Due(60), "video begins credit checks at 60 played seconds");
                Check(!videoSchedule.Expired(60), "first credit check must not end playback");
                videoSchedule.Checked(60);
                Check(!videoSchedule.Due(64.99) && videoSchedule.Due(65), "video checks every five played seconds");
                Check(!videoSchedule.Expired(89.99) && videoSchedule.Expired(90), "video no-credit extension is bounded at 90 seconds");
                videoSchedule.Checked(89);
                Check(videoSchedule.Due(90), "video deadline does not drift with polling");
                Check(!VideoSchedule.Finished(60, 2441, false), "60 seconds is not natural video completion");
                Check(VideoSchedule.Finished(28, 30, true), "short completed video can confirm credit");
                Check(!VideoSchedule.Finished(28, 30, false), "short unfinished video cannot finish early");
                Check(!VideoSchedule.Finished(2, 30, true), "seek to end is not sufficient playback");
                Check(ScoreSummary.Read(Json.Parse(@"{""ok"":true,""code"":200,""data"":{""score"":1234.5}}"), "score") == 1234.5,
                    "total score supports numeric response");
                Check(ScoreSummary.Read(Json.Parse(@"{""ok"":true,""code"":200,""data"":{""totalScore"":0}}"), "totalScore") == 0,
                    "zero daily score is not missing");
                foreach (string raw in new[] { "null", "true", "\"12\"", "-1" })
                    Check(!ScoreSummary.Read(Json.Parse("{\"ok\":true,\"code\":200,\"data\":{\"score\":" + raw + "}}"), "score").HasValue,
                        "invalid score summary does not become zero");
                Check(ScoreSummary.Display(null) == "--" && ScoreSummary.Display(0) == "0", "unknown and zero scores are distinct");
                Check(CreditConfirmation.Check(0, 12, new Progress { Current = 1, Maximum = 12 }) != null,
                    "reading can finish on first confirmed gain");
                Check(CreditConfirmation.Check(0, 12, new Progress { Current = 0, Maximum = 12 }) == null,
                    "reading continues while unchanged");
                VerifyCreditConfirmation().GetAwaiter().GetResult();
                VerifyLoginVault(path + ".login");
                Console.WriteLine("PASS: rules, exam submission validation, adaptive reading, delayed score confirmation, cancellation, SQLite and QR placement");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".settings")) File.Delete(path + ".settings");
                LoginVault.Clear(path + ".login");
            }
        }

        private static void VerifyLoginVault(string path)
        {
            var records = new object[] {
                new { name = "token", value = "synthetic-secret-not-a-real-token", domain = ".xuexi.cn" },
                new { name = "token", value = "reject", domain = "xuexi.cn.evil.test" }
            };
            records = records.Select(c => Json.Parse(Json.Encode(c))).ToArray();
            LoginVault.Save(path, records);
            Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("synthetic-secret"), "vault encrypted at rest");
            var loaded = LoginVault.Load(path);
            Check(loaded.Length == 1 && Json.Text(Json.Get(loaded[0], "value")) == "synthetic-secret-not-a-real-token",
                "DPAPI roundtrip and official cookie isolation");
            LoginVault.Save(path, records);
            Check(LoginVault.Load(path).Length == 1, "atomic vault replacement");
            byte[] damaged = File.ReadAllBytes(path); damaged[damaged.Length / 2] ^= 1;
            File.WriteAllBytes(path, damaged);
            Check(LoginVault.Load(path).Length == 0, "tampered vault fails closed");
            LoginVault.Clear(path);
            Check(!File.Exists(path) && LoginVault.Load(path).Length == 0, "logout deletes vault");
            Check(LoginVault.OfficialDomain(".pc.xuexi.cn") && !LoginVault.OfficialDomain("evilxuexi.cn"), "cookie domain boundary");
        }

        private static async Task VerifyCreditConfirmation()
        {
            int calls = 0, delays = 0;
            Func<int, CancellationToken, Task> delay = (milliseconds, token) => {
                token.ThrowIfCancellationRequested(); delays++; return Task.CompletedTask;
            };
            var confirmed = await CreditConfirmation.Wait(0, 12, () => {
                calls++; return Task.FromResult(new Progress { Current = calls < 3 ? 0 : 1, Maximum = 12 });
            }, delay, CancellationToken.None);
            Check(confirmed.Current == 1 && calls == 3 && delays == 2, "delayed gain is confirmed");
            calls = 0;
            confirmed = await CreditConfirmation.Wait(0, 12, () => {
                calls++; return Task.FromResult(new Progress { Current = 0, Maximum = 12 });
            }, delay, CancellationToken.None);
            Check(confirmed == null && calls == 6, "unchanged score stops bounded polling");
            bool missing = false;
            try { await CreditConfirmation.Wait(0, 12, () => Task.FromResult<Progress>(null), delay, CancellationToken.None); }
            catch (InvalidOperationException) { missing = true; }
            Check(missing, "missing task is not completion");
            bool reset = false;
            try { await CreditConfirmation.Wait(2, 12, () => Task.FromResult(new Progress { Current = 0, Maximum = 12 }), delay, CancellationToken.None); }
            catch (InvalidOperationException) { reset = true; }
            Check(reset, "score reset stops confirmation");
            bool changed = false;
            try { await CreditConfirmation.Wait(0, 12, () => Task.FromResult(new Progress { Current = 1, Maximum = 6 }), delay, CancellationToken.None); }
            catch (InvalidOperationException) { changed = true; }
            Check(changed, "changed rule stops confirmation");
            using (var cancellation = new CancellationTokenSource())
            {
                calls = 0; bool canceled = false;
                try {
                    await CreditConfirmation.Wait(0, 12, () => {
                        calls++; cancellation.Cancel();
                        return Task.FromResult(new Progress { Current = 1, Maximum = 12 });
                    }, delay, cancellation.Token);
                }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && calls == 1, "cancel after response cannot commit completion");
            }
            using (var cancellation = new CancellationTokenSource())
            {
                calls = 0; bool canceled = false;
                try {
                    await CreditConfirmation.Wait(0, 12, () => {
                        calls++; return Task.FromResult(new Progress { Current = 0, Maximum = 12 });
                    }, (milliseconds, token) => {
                        cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask;
                    }, cancellation.Token);
                }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && calls == 1, "stop during delayed polling prevents another query");
            }
        }
    }
}
