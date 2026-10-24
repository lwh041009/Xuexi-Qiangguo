using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace StudyLite
{
    internal static class Json
    {
        public static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 24000000, RecursionLimit = 64 };
        }
        public static object Parse(string value) { return Serializer().DeserializeObject(value); }
        public static string Encode(object value) { return Serializer().Serialize(value); }
        public static Dictionary<string, object> Map(object value)
        {
            return value as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
        public static object Get(object value, string key)
        {
            object result;
            return Map(value).TryGetValue(key, out result) ? result : null;
        }
        public static string Text(object value) { return Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""; }
        public static double Number(object value)
        {
            double number;
            return double.TryParse(Text(value), NumberStyles.Any, CultureInfo.InvariantCulture, out number)
                && !double.IsInfinity(number) && !double.IsNaN(number) ? number : 0;
        }
        public static IEnumerable<object> Array(object value)
        {
            return value is object[] ? (object[])value : new object[0];
        }
        public static bool Ok(object value)
        {
            return Object.Equals(Get(value, "ok"), true) && Number(Get(value, "code")) == 200;
        }
        public static object Unwrap(object value)
        {
            object encoded = Get(value, "data_str");
            if (encoded is string)
            {
                string s = (string)encoded;
                if (s.Length > 2000000) throw new FormatException("Response too large");
                try { return Parse(s); }
                catch (ArgumentException) { return Parse(Encoding.UTF8.GetString(Convert.FromBase64String(s))); }
            }
            return Get(value, "data");
        }
    }

    internal static class ExamSubmission
    {
        internal static bool Accepted(object envelope)
        {
            if (!Json.Ok(envelope)) return false;
            object value = Json.Get(Json.Unwrap(envelope), "totalNum");
            if (value == null || value is bool || value is string) return false;
            double total;
            return double.TryParse(Json.Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out total)
                && !double.IsNaN(total) && !double.IsInfinity(total)
                && total >= 0 && total <= 100 && total == Math.Truncate(total);
        }
    }

    internal sealed class Item
    {
        public string Id, Kind, Title, Url;
        public double Published;
    }

    internal sealed class Progress
    {
        public string Kind;
        public double Current, Maximum;
        public bool Done { get { return Maximum > 0 && Current >= Maximum; } }
    }

    internal static class CreditConfirmation
    {
        internal static Progress Check(double before, double maximum, Progress value)
        {
            if (value == null) throw new InvalidOperationException("平台未返回当前任务进度，已停止");
            if (value.Maximum != maximum || value.Current < before)
                throw new InvalidOperationException("平台任务日期或规则已变化，已停止，请刷新进度");
            return value.Current > before ? value : null;
        }

        internal static async Task<Progress> Wait(double before, double maximum,
            Func<Task<Progress>> query, Func<int, CancellationToken, Task> delay, CancellationToken token)
        {
            foreach (int wait in new[] { 0, 2000, 4000, 6000, 8000, 10000 })
            {
                token.ThrowIfCancellationRequested();
                if (wait > 0) await delay(wait, token);
                token.ThrowIfCancellationRequested();
                Progress value = await query();
                token.ThrowIfCancellationRequested();
                Progress confirmed = Check(before, maximum, value);
                if (confirmed != null) return confirmed;
            }
            return null;
        }
    }

    internal sealed class ReadingSchedule
    {
        internal const int InitialSeconds = 60, PollSeconds = 10, MaximumSeconds = 120;
        private double nextCheck = InitialSeconds;
        internal bool Due(double seconds) { return seconds >= nextCheck || Expired(seconds); }
        internal void Checked(double seconds) { nextCheck = seconds + PollSeconds; }
        internal bool Expired(double seconds) { return seconds >= MaximumSeconds; }
    }

    internal sealed class VideoSchedule
    {
        internal const int TargetSeconds = 60, MaximumSeconds = 90, PollSeconds = 5;
        private double nextCheck = TargetSeconds;
        internal static bool Finished(double watched, double duration, bool ended)
        {
            return ended && duration > 0 && watched >= duration * 0.9;
        }
        internal bool Due(double watched) { return watched >= nextCheck || Expired(watched); }
        internal void Checked(double watched) { nextCheck = watched + PollSeconds; }
        internal bool Expired(double watched) { return watched >= MaximumSeconds; }
    }

    internal static class ScoreSummary
    {
        internal static double? Read(object envelope, string field)
        {
            if (!Json.Ok(envelope)) return null;
            object raw = Json.Get(Json.Get(envelope, "data"), field);
            if (!(raw is int || raw is long || raw is decimal || raw is double || raw is float)) return null;
            double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? (double?)null : value;
        }
        internal static string Display(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "--";
        }
    }

    internal static class WindowPlacement
    {
        internal static Point Center(Rectangle owner, Size size, Rectangle workArea)
        {
            int x = owner.Left + (owner.Width - size.Width) / 2;
            int y = owner.Top + (owner.Height - size.Height) / 2;
            return new Point(Math.Max(workArea.Left, Math.Min(x, Math.Max(workArea.Left, workArea.Right - size.Width))),
                Math.Max(workArea.Top, Math.Min(y, Math.Max(workArea.Top, workArea.Bottom - size.Height))));
        }
    }

    internal sealed class Preferences
    {
        public bool Article = true, Video = true, Exam = true, AutoRun = true, AutoLogout = false, Mute = true;
        public bool TaskEnabled(string kind) { return kind == "article" ? Article : kind == "video" ? Video : Exam; }
        private static bool Read(object data, string key, bool fallback)
        {
            object value = Json.Get(data, key); return value is bool ? (bool)value : fallback;
        }
        public static Preferences Load(string path)
        {
            var result = new Preferences();
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 50000) return result;
                object data = Json.Parse(File.ReadAllText(path));
                result.Article = Read(data, "article", true); result.Video = Read(data, "video", true);
                result.Exam = Read(data, "exam", true); result.AutoRun = Read(data, "autoRun", true);
                result.AutoLogout = Read(data, "autoLogout", false); result.Mute = Read(data, "mute", true);
            }
            catch { }
            return result;
        }
        public void Save(string path)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, Json.Encode(new { article = Article, video = Video, exam = Exam,
                autoRun = AutoRun, autoLogout = AutoLogout, mute = Mute }), Encoding.UTF8);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    internal static class Rules
    {
        internal static double PublishedTime(object value)
        {
            double numeric = Json.Number(value);
            if (numeric > 0) return numeric < 100000000000 ? numeric * 1000 : numeric;
            DateTimeOffset date;
            return DateTimeOffset.TryParse(Json.Text(value), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out date)
                ? (date.ToUniversalTime() - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalMilliseconds : 0;
        }
        public static bool Platform(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == "https"
                && u.UserInfo.Length == 0 && u.Port == 443
                && (u.Host == "xuexi.cn" || u.Host.EndsWith(".xuexi.cn", StringComparison.OrdinalIgnoreCase));
        }
        public static bool Navigation(string url)
        {
            Uri u;
            return Platform(url) || (Uri.TryCreate(url, UriKind.Absolute, out u)
                && u.Scheme == "https" && u.Port == 443 && u.UserInfo.Length == 0
                && (u.Host == "dingtalk.com" || u.Host.EndsWith(".dingtalk.com", StringComparison.OrdinalIgnoreCase)));
        }
        public static List<Item> Catalogue(object value)
        {
            var items = new Dictionary<string, Item>();
            var rows = Json.Array(value).ToArray();
            if (rows.Length > 25000) throw new FormatException("Catalogue too large");
            foreach (object row in rows)
            {
                string type = Json.Text(Json.Get(row, "itemType"));
                string format = Json.Text(Json.Get(row, "type"));
                string kind = (type == "kNews" || type == "kReading") && format == "tuwen" ? "article"
                    : (type == "kPureVideo" || type == "kLongVideo") && format == "shipin" ? "video" : "";
                string id = Json.Text(Json.Get(row, "itemId"));
                string url = Json.Text(Json.Get(row, "url"));
                string title = Json.Text(Json.Get(row, "title"));
                if (kind == "" || id.Length == 0 || id.Length > 512 || !Platform(url)
                    || Object.Equals(Json.Get(row, "dataValid"), false)) continue;
                var item = new Item { Id = id, Kind = kind, Url = url,
                    Title = title.Substring(0, Math.Min(500, title.Length)),
                    Published = PublishedTime(Json.Get(row, "publishTime")) };
                items[kind + ":" + id] = item;
            }
            return items.Values.OrderByDescending(i => i.Published).ThenBy(i => i.Id).ToList();
        }
        public static Dictionary<string, Progress> TaskProgress(object envelope)
        {
            if (!Json.Ok(envelope)) throw new FormatException("Progress not successful");
            var data = Json.Get(envelope, "data");
            if (Object.Equals(Json.Get(data, "inBlackList"), true)
                || Object.Equals(Json.Get(data, "needFaceRecognition"), true))
                throw new InvalidOperationException("平台要求身份验证，请在官方页面处理");
            var result = new Dictionary<string, Progress>();
            foreach (object task in Json.Array(Json.Get(data, "taskProgress")))
            {
                var codes = Json.Array(Json.Get(task, "taskCode")).Select(Json.Text).ToArray();
                string title = Json.Text(Json.Get(task, "title"));
                string kind = codes.Contains("1") || codes.Contains("1002") || title.Contains("选读文章") ? "article"
                    : codes.Contains("2") || codes.Contains("1003") || title.Contains("视听学习") ? "video"
                    : codes.Contains("6") || title.Contains("每日答题") ? "exam"
                    : codes.Contains("9") || title == "登录" ? "login" : "";
                if (kind == "") continue;
                double max = Json.Number(Json.Get(task, "dayMaxScore"));
                double current = Json.Number(Json.Get(task, "currentScore"));
                if (max <= 0 || max > 1000 || current < 0) continue;
                Progress existing;
                if (result.TryGetValue(kind, out existing)) { existing.Current += current; existing.Maximum += max; }
                else result[kind] = new Progress { Kind = kind, Current = current, Maximum = max };
            }
            if (result.Count == 0) throw new FormatException("Unknown progress schema");
            return result;
        }
    }

    // Windows supplies SQLite; prepared statements keep all content out of SQL syntax.
    internal sealed class History : IDisposable
    {
        private IntPtr db;
        private static readonly IntPtr Transient = new IntPtr(-1);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open(byte[] name, out IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int count, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] text, int count, IntPtr destroy);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);
        private static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value + "\0"); }
        public History(string path)
        {
            if (sqlite3_open(Utf8(path), out db) != 0) { Dispose(); throw new InvalidOperationException("无法打开学习日志"); }
            Run("CREATE TABLE IF NOT EXISTS visits(account TEXT,kind TEXT,itemId TEXT,viewTime TEXT,seconds TEXT,PRIMARY KEY(account,kind,itemId))");
            // Legacy visits were only timed locally; never migrate them into credited history.
            Run("CREATE TABLE IF NOT EXISTS credited_visits(account TEXT,kind TEXT,itemId TEXT,viewTime TEXT,seconds TEXT,scoreBefore TEXT,scoreAfter TEXT,PRIMARY KEY(account,kind,itemId))");
            Run("CREATE TABLE IF NOT EXISTS attempts(account TEXT,kind TEXT,itemId TEXT,viewTime TEXT,seconds TEXT,scoreBefore TEXT,scoreAfter TEXT,outcome TEXT)");
        }
        private bool Run(string sql, params string[] values)
        {
            IntPtr statement;
            if (sqlite3_prepare_v2(db, Utf8(sql), -1, out statement, IntPtr.Zero) != 0)
                throw new InvalidOperationException("SQLite prepare failed");
            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    byte[] bytes = Utf8(values[i]);
                    if (sqlite3_bind_text(statement, i + 1, bytes, bytes.Length - 1, Transient) != 0)
                        throw new InvalidOperationException("SQLite bind failed");
                }
                int result = sqlite3_step(statement);
                if (result != 100 && result != 101) throw new InvalidOperationException("SQLite step failed");
                return result == 100;
            }
            finally { sqlite3_finalize(statement); }
        }
        public bool Seen(string account, Item item)
        {
            return Run("SELECT 1 FROM credited_visits WHERE account=? AND kind=? AND itemId=?", account, item.Kind, item.Id);
        }
        public bool NoCreditToday(string account, Item item)
        {
            return Run("SELECT 1 FROM attempts WHERE account=? AND kind=? AND itemId=? AND outcome=? AND substr(viewTime,1,10)=?",
                account, item.Kind, item.Id, "no_credit", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        public void RecordAttempt(string account, Item item, int seconds, double before, double after, string outcome)
        {
            Run("INSERT INTO attempts(account,kind,itemId,viewTime,seconds,scoreBefore,scoreAfter,outcome) VALUES(?,?,?,?,?,?,?,?)",
                account, item.Kind, item.Id, DateTimeOffset.Now.ToString("o"), seconds.ToString(CultureInfo.InvariantCulture),
                before.ToString(CultureInfo.InvariantCulture), after.ToString(CultureInfo.InvariantCulture), outcome);
        }
        public void RecordConfirmed(string account, Item item, int seconds, double before, double after)
        {
            if (before < 0 || after <= before || Double.IsNaN(before) || Double.IsNaN(after)
                || Double.IsInfinity(before) || Double.IsInfinity(after))
                throw new ArgumentException("Confirmed history requires a server score increase");
            Run("INSERT OR IGNORE INTO credited_visits(account,kind,itemId,viewTime,seconds,scoreBefore,scoreAfter) VALUES(?,?,?,?,?,?,?)",
                account, item.Kind, item.Id, DateTimeOffset.Now.ToString("o"), seconds.ToString(CultureInfo.InvariantCulture),
                before.ToString(CultureInfo.InvariantCulture), after.ToString(CultureInfo.InvariantCulture));
        }
        public void Dispose() { if (db != IntPtr.Zero) { sqlite3_close(db); db = IntPtr.Zero; } }
    }
}
