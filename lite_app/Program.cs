using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace StudyLite
{
    internal static class Program
    {
        internal static string RuntimeDirectory;
        internal static Icon AppIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            using (var icon = new Icon(stream)) return (Icon)icon.Clone();
        }
        internal static string Asset(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Contains("--self-test")) return Tests.Run();
                RuntimeDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StudyDeskLite", "runtime-1.0.3856.49");
                Directory.CreateDirectory(RuntimeDirectory);
                foreach (string name in new[] { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" })
                {
                    byte[] bytes;
                    using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    using (var memory = new MemoryStream()) { source.CopyTo(memory); bytes = memory.ToArray(); }
                    string destination = Path.Combine(RuntimeDirectory, name);
                    // Check content, not just presence, before loading an extracted dependency.
                    if (!File.Exists(destination) || !File.ReadAllBytes(destination).SequenceEqual(bytes))
                    {
                        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllBytes(temporary, bytes);
                        if (File.Exists(destination)) File.Delete(destination);
                        File.Move(temporary, destination);
                    }
                }
                SetDllDirectory(RuntimeDirectory);
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
                {
                    string name = new AssemblyName(e.Name).Name;
                    if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.WinForms") return null;
                    return Assembly.LoadFrom(Path.Combine(RuntimeDirectory, name + ".dll"));
                };
                return Open(args);
            }
            catch (Exception e)
            {
                string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StudyDeskLite", "startup-error.log");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log));
                    File.WriteAllText(log, DateTimeOffset.Now.ToString("o") + "\r\n" + e.ToString(), Encoding.UTF8);
                }
                catch { }
                if (!args.Any(a => a == "--smoke-test" || a == "--browser-test" || a == "--qr-test"
                    || a == "--login-probe" || a == "--article-probe" || a == "--video-probe" || a == "--self-test" || a == "--session-test"))
                    MessageBox.Show("启动失败：" + e.GetType().Name + "\r\n" + e.Message + "\r\n诊断日志：" + log,
                        "学习强国", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Open(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new MainForm(args)) Application.Run(form);
            return MainForm.ExitCode;
        }
    }

    internal sealed partial class MainForm : Form
    {
        internal static int ExitCode;
        private const string LoginUrl = "https://pc.xuexi.cn/points/login.html";
        private const string InfoUrl = "https://pc-api.xuexi.cn/open/api/user/info";
        private const string ScoreUrl = "https://pc-proxy-api.xuexi.cn/delegate/score/days/listScoreProgress?sence=score&deviceType=2";
        private const string TotalScoreUrl = "https://pc-proxy-api.xuexi.cn/delegate/score/get";
        private const string TodayScoreUrl = "https://pc-proxy-api.xuexi.cn/delegate/score/today/query";
        private const string ExamUrl = "https://pc.xuexi.cn/points/exam-practice.html";
        private readonly string dataRoot, sessionPath, smokePath;
        private readonly bool smallSmoke, browserTest, qrTest, loginProbe, articleProbe, videoProbe;
        private readonly bool sessionTest;
        private readonly string sessionTestStage;
        private readonly List<Stream> testStreams = new List<Stream>();
        private readonly WebView2 web = new WebView2();
        private readonly Label identity = new Label(), status = new Label();
        private readonly Label detail = new Label();
        private readonly CheckBox autoRun = new CheckBox();
        private readonly Button start = new Button(), stop = new Button(), login = new Button(), refresh = new Button();
        private readonly Dictionary<string, CheckBox> tasks = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, Label> scores = new Dictionary<string, Label>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly HttpClient http;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly HashSet<string> attempted = new HashSet<string>();
        private readonly List<CoreWebView2Frame> frames = new List<CoreWebView2Frame>();
        private Dictionary<string, Progress> progress = new Dictionary<string, Progress>();
        private List<Item> catalogue = new List<Item>();
        private History history;
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool initialized, closing, busy, loggedIn, running, armed = true, navigationReady, examResult;
        private bool actionPending;
        private string account = "", phase = "", videoSource = "", examProgressKey = "", lastExamDiagKey = "";
        private double examProgressAt = 0;
        private int generation, failures, examScoreResponses, examScoreHttp, videoNoCreditCount;
        private Item current;
        private double elapsed, watched, videoPosition = -1, tickAt, itemStarted, loginChecked, lastVideoAdvance;
        private double itemScore, itemMaximum;
        private ReadingSchedule readingSchedule = new ReadingSchedule();
        private VideoSchedule videoSchedule = new VideoSchedule();
        private int pageScrollEvents, heartbeatResponses, lastHeartbeatHttp;
        private bool pageHidden;
        private readonly Dictionary<string, int> attemptedCounts = new Dictionary<string, int>();

        internal MainForm(string[] args)
        {
            int smokeIndex = Array.IndexOf(args, "--smoke-test");
            browserTest = args.Contains("--browser-test");
            qrTest = args.Contains("--qr-test");
            loginProbe = args.Contains("--login-probe");
            articleProbe = args.Contains("--article-probe");
            videoProbe = args.Contains("--video-probe");
            sessionTest = args.Contains("--session-test");
            sessionTestStage = args.Contains("--seed") ? "seed" : args.Contains("--logout") ? "logout"
                : args.Contains("--after-logout") ? "after-logout" : "restore";
            if (browserTest) smokeIndex = Array.IndexOf(args, "--browser-test");
            if (qrTest) smokeIndex = Array.IndexOf(args, "--qr-test");
            if (loginProbe) smokeIndex = Array.IndexOf(args, "--login-probe");
            if (articleProbe) smokeIndex = Array.IndexOf(args, "--article-probe");
            if (videoProbe) smokeIndex = Array.IndexOf(args, "--video-probe");
            if (sessionTest) smokeIndex = Array.IndexOf(args, "--session-test");
            if (smokeIndex >= 0 && (smokeIndex + 1 >= args.Length || String.IsNullOrWhiteSpace(args[smokeIndex + 1])
                || args[smokeIndex + 1].StartsWith("--")))
                throw new ArgumentException("离线验证需要非空的输出目录。");
            smokePath = smokeIndex >= 0 ? Path.GetFullPath(args[smokeIndex + 1]) : null;
            smallSmoke = args.Contains("--small");
            dataRoot = smokePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudyDeskLite");
            sessionPath = smokePath == null || sessionTest ? Path.Combine(dataRoot, "browser-profile")
                : Path.Combine(dataRoot, "sessions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            history = new History(Path.Combine(dataRoot, "learning.sqlite3"));
            http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            http.Timeout = TimeSpan.FromSeconds(35);
            Text = "学习强国";
            Font = new Font("Microsoft YaHei UI", 9F);
            Size = smallSmoke ? new Size(580, 440) : new Size(620, 470);
            MinimumSize = new Size(580, 440);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Program.AppIcon();
            BackColor = Color.White;
            BuildControls();
            InitializeTray();
            armed = autoRun.Checked;
            timer.Interval = 1000;
            timer.Tick += Tick;
            Shown += async delegate { await Initialize(); };
            FormClosing += OnClosing;
        }

        private void BuildControls()
        {
            BuildDashboard();
        }
        private void ConfigureButton(Button button, string text, EventHandler handler)
        {
            button.Text = text; button.AutoSize = true; button.Height = 28;
            button.MinimumSize = new Size(68, 28);
            button.Margin = new Padding(0, 0, 7, 0);
            button.Click += handler;
        }
        private static string NameOf(string kind) { return kind == "login" ? "每日登录" : kind == "article" ? "阅读文章" : kind == "video" ? "观看视频" : "每日答题"; }
        private void Say(string value)
        {
            if (closing) return;
            status.Text = value;
            AddLog(value);
        }
        private void SetControls()
        {
            start.Enabled = initialized && !running;
            stop.Enabled = initialized;
            refresh.Enabled = initialized && loggedIn && !running;
            login.Enabled = initialized && !running;
            autoRun.Enabled = !running;
            autoLogout.Enabled = mute.Enabled = saveSettings.Enabled = !running;
            foreach (CheckBox task in tasks.Values) task.Enabled = !running;
        }
        private bool Valid(int stamp) { return !closing && stamp == generation && !cancellation.IsCancellationRequested; }
        private async Task Initialize()
        {
            try
            {
                web.CreationProperties = new CoreWebView2CreationProperties {
                    UserDataFolder = sessionPath, IsInPrivateModeEnabled = smokePath != null && !sessionTest,
                    AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
                };
                engineWindow.Location = Screen.FromControl(this).WorkingArea.Location;
                engineWindow.Show();
                IntPtr handle = web.Handle;
                await web.EnsureCoreWebView2Async(null);
                if (closing) return;
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsWebMessageEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.IsMuted = mute.Checked;
                core.DownloadStarting += delegate(object s, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                core.PermissionRequested += delegate(object s, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                core.ServerCertificateErrorDetected += delegate(object s, CoreWebView2ServerCertificateErrorDetectedEventArgs e) { e.Action = CoreWebView2ServerCertificateErrorAction.Cancel; };
                core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    if (smokePath == null && Rules.Navigation(e.Uri)) core.Navigate(e.Uri);
                };
                core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (!(smokePath != null && !loginProbe && !articleProbe && !videoProbe ? e.Uri == "about:blank" || e.Uri.StartsWith("data:")
                        || browserTest && e.Uri == BrowserVerification.PageUrl
                        || qrTest && e.Uri == QrVerification.PageUrl
                        : e.Uri == "about:blank" || Rules.Navigation(e.Uri)))
                    {
                        e.Cancel = true;
                        if (running) Halt("已拦截非官方页面跳转");
                        return;
                    }
                    navigationReady = false;
                };
                core.NavigationCompleted += async delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    navigationReady = e.IsSuccess;
                    if (!e.IsSuccess && running) { Halt("页面加载失败，请检查网络后重新开始"); return; }
                    if (running && phase == "exam" && Rules.Platform(core.Source))
                    {
                        try { await core.ExecuteScriptAsync("window.StudyLite && window.StudyLite.start()"); } catch { }
                    }
                };
                core.WebResourceResponseReceived += OnResponse;
                core.WebResourceResponseReceived += CaptureLoginImage;
                core.FrameCreated += delegate(object s, CoreWebView2FrameCreatedEventArgs e)
                {
                    frames.Add(e.Frame);
                    e.Frame.Destroyed += delegate { frames.Remove(e.Frame); };
                };
                await core.AddScriptToExecuteOnDocumentCreatedAsync(Program.Asset("browser.js"));
                initialized = true;
                SetControls();
                tickAt = clock.Elapsed.TotalSeconds;
                timer.Start();
                if (sessionTest)
                {
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += delegate(object s, CoreWebView2WebResourceRequestedEventArgs e) {
                        e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked in session test", "");
                    };
                    await VerifyLoginSession();
                    await ExitApplication();
                }
                else if (browserTest || qrTest)
                {
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += delegate(object s, CoreWebView2WebResourceRequestedEventArgs e)
                    {
                        string body = qrTest ? QrVerification.Response(e.Request.Uri) : BrowserVerification.Response(e.Request.Uri);
                        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body ?? ""));
                        testStreams.Add(stream);
                        e.Response = core.Environment.CreateWebResourceResponse(stream, body == null ? 403 : 200,
                            body == null ? "Blocked in test" : "OK", "Content-Type: "
                            + (e.Request.Uri == BrowserVerification.PageUrl || e.Request.Uri == QrVerification.PageUrl ? "text/html" : "application/json")
                            + "; charset=utf-8\r\nAccess-Control-Allow-Origin: https://pc.xuexi.cn\r\nCache-Control: no-store");
                    };
                    core.Navigate(qrTest ? QrVerification.PageUrl : BrowserVerification.PageUrl);
                    Say(qrTest ? "二维码窗口离线验证" : "自动答题离线验证");
                    for (int i = 0; i < 100 && !navigationReady; i++) await Task.Delay(100);
                    if (!navigationReady) throw new InvalidOperationException("Fixture navigation failed");
                    if (qrTest)
                    {
                        ShowLogin();
                        await QrVerification.Run(this, smokePath);
                    }
                    else
                    {
                        VerifyQuietExamWindow();
                        await BrowserVerification.Run(core, () => engineWindow.VisuallyHidden);
                        await VerifyExamResponses();
                    }
                    if (!closing) await FinishSmoke();
                }
                else if (videoProbe)
                {
                    await RunVideoProbe();
                    await FinishSmoke();
                }
                else if (articleProbe)
                {
                    int blockedLogs = 0;
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e)
                    {
                        Uri uri;
                        bool official = Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out uri) && Rules.Platform(e.Request.Uri);
                        bool post = e.Request.Method != "GET" && e.Request.Method != "HEAD" && e.Request.Method != "OPTIONS";
                        if (official && post && uri.Host == "iflow-api.xuexi.cn"
                            && (uri.AbsolutePath == "/logflow/api/v1/pclog" || uri.AbsolutePath == "/logflow_seer/api/v1/pclog"))
                            blockedLogs++;
                        if (!official || post)
                            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked in read-only probe", "");
                    };
                    core.Navigate("https://www.xuexi.cn/lgpage/detail/index.html?id=8122107777908086544&item_id=8122107777908086544");
                    object page = null;
                    for (int i = 0; i < 90; i++)
                    {
                        await Task.Delay(500);
                        if (!navigationReady) continue;
                        page = await Evaluate("window.StudyLite ? window.StudyLite.article() : null");
                        if (Object.Equals(Json.Get(page, "ready"), true)
                            && Json.Number(Json.Get(page, "scrollEvents")) > 0 && blockedLogs > 0) break;
                    }
                    bool success = Object.Equals(Json.Get(page, "ready"), true)
                        && !Object.Equals(Json.Get(page, "hidden"), true)
                        && Json.Number(Json.Get(page, "scrollEvents")) > 0 && blockedLogs > 0;
                    File.WriteAllText(Path.Combine(smokePath, "article-probe.json"), Json.Encode(new {
                        ok = success, documentHidden = Json.Get(page, "hidden"),
                        scrollEvents = Json.Number(Json.Get(page, "scrollEvents")),
                        officialLogRequestsBlocked = blockedLogs, credentialsUsed = false,
                        postRequestsAllowed = false, scoringTested = false
                    }), Encoding.UTF8);
                    if (!success) throw new InvalidOperationException("Official article rendering or interaction did not start");
                    await FinishSmoke();
                }
                else if (loginProbe)
                {
                    ShowLogin(); core.Navigate(LoginUrl);
                    for (int i = 0; i < 120 && qrLogin.Picture.Image == null; i++)
                    {
                        await Task.Delay(500);
                        await RefreshQrPreview(generation);
                    }
                    bool loaded = qrLogin.Picture.Image != null;
                    File.WriteAllText(Path.Combine(smokePath, "login-probe.json"), Json.Encode(new {
                        qrLoaded = loaded, browserVisible = !engineWindow.VisuallyHidden, frameCount = frames.Count,
                        credentialsUsed = false, qrSaved = false
                    }), Encoding.UTF8);
                    if (!loaded) throw new InvalidOperationException("Official QR preview did not load");
                    CloseLogin();
                    await FinishSmoke();
                }
                else if (smokePath == null) { await RestoreLogin(); }
                else
                {
                    core.NavigateToString("<!doctype html><meta charset='utf-8'><title>离线验证</title><body style='font:16px Segoe UI;background:#fff;color:#293038;padding:32px'><h2>学习精简版</h2><p>WebView2 离线验证</p><div style='height:180px;background:#e7f5eb;border-left:5px solid #287f46;padding:20px'>浏览器内核已启动</div></body>");
                    Say("离线验证，不访问网络");
                    await Task.Delay(3500);
                    if (!closing) await FinishSmoke();
                }
            }
            catch (Exception e)
            {
                ExitCode = 1;
                Say("启动或页面初始化失败：" + e.GetType().Name);
                if (smokePath != null)
                {
                    File.WriteAllText(Path.Combine(smokePath, "error.txt"), e.ToString());
                    Close();
                }
            }
        }

        private async Task<object> Get(string url, bool authenticated, CancellationToken token)
        {
            if (!Rules.Platform(url)) throw new InvalidOperationException("Unsupported URL");
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", web.CoreWebView2.Settings.UserAgent);
                request.Headers.Referrer = new Uri("https://pc.xuexi.cn/");
                request.Headers.TryAddWithoutValidation("Origin", "https://pc.xuexi.cn");
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                if (authenticated)
                {
                    var cookies = await web.CoreWebView2.CookieManager.GetCookiesAsync(url);
                    token.ThrowIfCancellationRequested();
                    string header = String.Join("; ", cookies.Where(c => c.Value.Length > 0)
                        .Select(c => c.Name + "=" + c.Value));
                    if (header.Length == 0) throw new InvalidOperationException("Missing session");
                    request.Headers.TryAddWithoutValidation("Cookie", header);
                }
                using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        throw new UnauthorizedAccessException();
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                        return Json.Parse(await ReadBounded(stream, authenticated ? 2000000 : 22000000, token));
                }
            }
        }
        private static async Task<string> ReadBounded(Stream stream, int maximum, CancellationToken token)
        {
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    if (output.Length + read > maximum) throw new FormatException("Response exceeds limit");
                    output.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
        private async Task CheckLogin(int stamp)
        {
            var cookies = await web.CoreWebView2.CookieManager.GetCookiesAsync(null);
            if (!Valid(stamp)) return;
            var scoped = cookies.Where(c => c.Domain.TrimStart('.') == "xuexi.cn" || c.Domain.EndsWith(".xuexi.cn", StringComparison.OrdinalIgnoreCase)).ToList();
            bool present = scoped.Any(c => c.Name == "__UID__" && c.Value.Length > 0)
                && scoped.Any(c => c.Name == "token" && c.Value.Length > 0);
            if (!present)
            {
                if (loggedIn) LoseLogin();
                return;
            }
            var result = await Get(InfoUrl, true, cancellation.Token);
            if (!Valid(stamp)) return;
            string uid = Json.Text(Json.Get(Json.Get(result, "data"), "uid"));
            if (!Json.Ok(result) || uid.Length == 0) throw new UnauthorizedAccessException();
            string key;
            using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(uid))).Replace("-", "").ToLowerInvariant();
            if (loggedIn && key != account)
            {
                Halt("账号已变化，已停止；请重新扫码");
                loggedIn = false; account = ""; identity.Text = "账号已变化"; UpdateScoreSummary(null, null); SetControls();
                return;
            }
            bool wasLoggedIn = loggedIn;
            account = key; loggedIn = true;
            loginRecoveryPending = false;
            CloseLogin();
            identity.Text = "已登录";
            SetControls();
            await SaveLoginSession();
            if (!Valid(stamp)) return;
            if (!wasLoggedIn)
            {
                await LoadProgress(stamp);
                if (Valid(stamp) && armed) await Begin();
                else if (Valid(stamp)) Say("已登录");
            }
        }
        private async Task LoadProgress(int stamp)
        {
            var result = await Get(ScoreUrl, true, cancellation.Token);
            if (!Valid(stamp)) return;
            progress = Rules.TaskProgress(result);
            double? today = ScoreSummary.Read(result, "totalScore");
            double? total = null;
            try
            {
                total = ScoreSummary.Read(await Get(TotalScoreUrl, true, cancellation.Token), "score");
                if (!Valid(stamp)) return;
                if (!today.HasValue)
                    today = ScoreSummary.Read(await Get(TodayScoreUrl, true, cancellation.Token), "score");
            }
            catch (UnauthorizedAccessException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch { /* Summary availability must not invalidate confirmed task progress. */ }
            if (!Valid(stamp)) return;
            UpdateScoreSummary(total, today);
            foreach (string kind in scores.Keys)
            {
                Progress value;
                scores[kind].Text = NameOf(kind) + "\r\n" + (progress.TryGetValue(kind, out value)
                    ? value.Current + " / " + value.Maximum + (value.Done ? "  已完成" : "")
                    : "暂未返回进度");
                scores[kind].ForeColor = value != null && value.Done ? Color.FromArgb(35, 116, 65) : Color.FromArgb(90, 96, 103);
            }
            summary.Text = "今日任务：" + progress.Count(p => p.Value.Done) + " / " + progress.Count + " 项已完成";
        }
        private async Task ReLogin()
        {
            if (!initialized || actionPending) return;
            Halt("等待重新扫码");
            loggedIn = false; account = ""; progress.Clear();
            UpdateScoreSummary(null, null);
            identity.Text = "等待扫码登录";
            foreach (var score in scores) score.Value.Text = NameOf(score.Key) + "\r\n等待登录";
            ClearLoginSession();
            armed = autoRun.Checked;
            ShowLogin();
            ResetQrPreview();
            web.CoreWebView2.Navigate(LoginUrl);
            SetControls();
            await Task.CompletedTask;
        }
        private async Task Begin()
        {
            if (running || !loggedIn || closing) return;
            if (!tasks.Values.Any(t => t.Checked)) { Say("请先在设置中选择任务"); return; }
            int stamp = ++generation;
            cancellation.Cancel(); cancellation.Dispose(); cancellation = new CancellationTokenSource();
            running = true; armed = false; failures = 0; videoNoCreditCount = 0;
            phase = ""; current = null; attempted.Clear(); attemptedCounts.Clear();
            SetControls();
            try
            {
                Say("正在读取平台任务进度");
                await LoadProgress(stamp);
                if (!Valid(stamp)) return;
                if (new[] { "article", "video" }.Any(Needed))
                {
                    Say("正在加载官方文章 / 视频列表");
                    object raw = await Get("https://www.xuexi.cn/lgdata/1hoa55co0nf.json", false, cancellation.Token);
                    if (!Valid(stamp)) return;
                    catalogue = Rules.Catalogue(raw);
                    if (catalogue.Count == 0) throw new FormatException("Empty catalogue");
                }
                await Next(stamp);
            }
            catch (OperationCanceledException) { if (Valid(stamp)) Halt("请求超时，请重试"); }
            catch (UnauthorizedAccessException) { if (Valid(stamp)) LoseLogin(); }
            catch (InvalidOperationException e) { if (Valid(stamp)) Halt(e.Message); }
            catch { if (Valid(stamp)) Halt("接口读取失败或格式变化，已停止；没有标记任务完成"); }
        }
        private bool Needed(string kind)
        {
            Progress value;
            return tasks[kind].Checked && progress.TryGetValue(kind, out value) && !value.Done;
        }
        private async Task Next(int stamp)
        {
            if (!Valid(stamp) || !running) return;
            current = null; navigationReady = false; elapsed = 0; watched = 0; videoPosition = -1; videoSource = "";
            readingSchedule = new ReadingSchedule();
            videoSchedule = new VideoSchedule();
            itemStarted = clock.Elapsed.TotalSeconds;
            lastVideoAdvance = itemStarted;
            foreach (string kind in new[] { "article", "video" })
            {
                if (!Needed(kind)) continue;
                int count; attemptedCounts.TryGetValue(kind, out count);
                if (count >= 12) { Halt("本轮已浏览 12 条，平台仍未完成；已停止，请检查计分规则"); return; }
                current = catalogue.Where(i => i.Kind == kind && !attempted.Contains(kind + ":" + i.Id) && !history.Seen(account, i))
                    .OrderBy(i => kind == "video" && history.NoCreditToday(account, i) ? 1 : 0).FirstOrDefault();
                if (current == null) { Halt("没有未学习的有效" + NameOf(kind) + "内容，已停止"); return; }
                phase = kind; attempted.Add(kind + ":" + current.Id); attemptedCounts[kind] = count + 1;
                itemScore = progress[kind].Current; itemMaximum = progress[kind].Maximum;
                pageScrollEvents = heartbeatResponses = lastHeartbeatHttp = 0; pageHidden = false;
                tickAt = clock.Elapsed.TotalSeconds;
                detail.Text = current.Title;
                Say(NameOf(kind) + " · 第 " + (count + 1) + " 条");
                web.CoreWebView2.Navigate(current.Url);
                return;
            }
            if (Needed("exam"))
            {
                phase = "exam"; examResult = false;
                examScoreResponses = examScoreHttp = 0;
                itemScore = progress["exam"].Current; itemMaximum = progress["exam"].Maximum;
                detail.Text = "每日答题";
                engineWindow.HideExam();
                examProgressKey = ""; lastExamDiagKey = ""; examProgressAt = clock.Elapsed.TotalSeconds;
                Say("每日答题自动作答中");
                web.CoreWebView2.Navigate(ExamUrl);
                return;
            }
            string[] missing = tasks.Where(t => t.Value.Checked && !progress.ContainsKey(t.Key)).Select(t => NameOf(t.Key)).ToArray();
            bool completed = missing.Length == 0;
            Halt(!completed ? "未返回任务进度：" + String.Join("、", missing) : "所选任务均已由平台确认完成");
            if (completed && autoLogout.Checked) Logout(true);
            await Task.CompletedTask;
        }
        private void Halt(string message)
        {
            running = false; armed = false; generation++;
            engineWindow.HideExam();
            cancellation.Cancel(); cancellation.Dispose(); cancellation = new CancellationTokenSource();
            current = null; phase = "";
            if (initialized && !closing)
            {
                web.CoreWebView2.Stop();
                try
                {
                    web.CoreWebView2.ExecuteScriptAsync("if(window.StudyLite){window.StudyLite.stop();window.StudyLite.pauseMedia();}");
                    foreach (var frame in frames.ToArray())
                        frame.ExecuteScriptAsync("if(/(^|\\.)xuexi\\.cn$/i.test(location.hostname)) for(const v of document.querySelectorAll('video,audio'))v.pause()");
                }
                catch { }
            }
            Say(message); SetControls();
        }
        private void LoseLogin()
        {
            Halt("登录校验失败，请重新扫码");
            loggedIn = false; account = ""; identity.Text = "未登录"; SetControls();
            ClearLoginSession();
            UpdateScoreSummary(null, null);
        }
        private async Task<object> Evaluate(string expression)
        {
            if (!Rules.Platform(web.CoreWebView2.Source)) return null;
            return Json.Parse(await web.CoreWebView2.ExecuteScriptAsync(expression));
        }
        private async Task<object[]> Media(int stamp)
        {
            var values = Json.Array(await Evaluate("window.StudyLite ? window.StudyLite.media(true) : null")).ToList();
            if (!Valid(stamp)) return new object[0];
            string probe = @"(() => {
                if(location.protocol!=='https:' || !/(^|\.)xuexi\.cn$/i.test(location.hostname))return [];
                return Array.from(document.querySelectorAll('video')).filter(v=>v.getClientRects().length).map(v=>{
                    if(v.paused&&!v.ended)v.play().catch(()=>{});
                    return {source:v.currentSrc||'',time:v.currentTime,duration:Number.isFinite(v.duration)?v.duration:0,
                        paused:v.paused,ended:v.ended,hidden:document.hidden===true,playbackRate:v.playbackRate,
                        readyState:v.readyState,errorCode:v.error?v.error.code:0};
                });
            })()";
            foreach (var frame in frames.ToArray())
            {
                if (!Valid(stamp)) return new object[0];
                try { values.AddRange(Json.Array(Json.Parse(await frame.ExecuteScriptAsync(probe)))); }
                catch { }
            }
            return values.ToArray();
        }
        private async void Tick(object sender, EventArgs args)
        {
            if (busy || actionPending || closing || !initialized || smokePath != null) return;
            busy = true;
            int stamp = generation;
            double now = clock.Elapsed.TotalSeconds, delta = Math.Max(0, Math.Min(3, now - tickAt));
            tickAt = now;
            try
            {
                if (!loggedIn && qrLogin != null && !qrLogin.IsDisposed)
                {
                    await RefreshQrPreview(stamp);
                    if (!Valid(stamp)) return;
                }
                if (now - loginChecked > (loggedIn ? 30 : 3))
                {
                    loginChecked = now;
                    await CheckLogin(stamp);
                    if (Valid(stamp)) PromptIfLoginMissing();
                    // Login may have started a new run with a new generation.
                    if (!Valid(stamp)) return;
                }
                if (!running || !Valid(stamp)) return;
                if (!navigationReady)
                {
                    if (now - itemStarted > 60) Halt("页面加载超时，已停止");
                    return;
                }
                if (web.CoreWebView2.Source.StartsWith(LoginUrl, StringComparison.OrdinalIgnoreCase))
                { LoseLogin(); return; }
                object guarded = await Evaluate("window.StudyLite ? window.StudyLite.guarded() : false");
                if (!Valid(stamp)) return;
                if (phase != "exam" && Object.Equals(guarded, true))
                { Halt("遇到验证，请手动处理后重新开始"); return; }
                if (phase == "exam")
                {
                    if (examResult)
                    {
                        foreach (int wait in new[] { 0, 2000, 4000, 6000, 8000, 10000 })
                        {
                            if (wait > 0) await Task.Delay(wait, cancellation.Token);
                            if (!Valid(stamp)) return;
                            await LoadProgress(stamp);
                            if (!Valid(stamp)) return;
                            Progress value;
                            if (!progress.TryGetValue("exam", out value))
                            { Halt("平台未返回每日答题进度，未记为完成"); return; }
                            CreditConfirmation.Check(itemScore, itemMaximum, value);
                            if (value.Done) break;
                        }
                        if (!Valid(stamp)) return;
                        WriteExamDiagnostic(Needed("exam") ? "submitted_not_complete" : "platform_confirmed");
                        if (Needed("exam")) { Halt("答题已提交，平台积分尚未达到上限；请稍后手动刷新确认"); return; }
                        await Next(stamp); return;
                    }
                    object result = await Evaluate("window.StudyLite ? window.StudyLite.examAuto() : null");
                    if (!Valid(stamp)) return;
                    if (result != null)
                    {
                        string mode = Json.Text(Json.Get(result, "mode"));
                        SyncExamWindow(mode, Object.Equals(Json.Get(result, "manual"), true));
                        Say(Json.Text(Json.Get(result, "status")));
                        if (mode == "error" || mode == "stopped")
                        { Halt(Json.Text(Json.Get(result, "status"))); return; }
                        if (mode == "wait" && now - itemStarted > 60)
                        { Halt("未获取到每日答题题目数据，已停止"); return; }
                        string key = mode + ":" + Json.Text(Json.Get(result, "step"));
                        if (key != examProgressKey) { examProgressKey = key; examProgressAt = now; }
                        else if (now - examProgressAt > 150)
                        { Halt("答题流程超过 150 秒无进展，已停止"); return; }
                        string diagKey = key + ":" + Json.Text(Json.Get(result, "status"));
                        if (diagKey != lastExamDiagKey)
                        { lastExamDiagKey = diagKey; WriteExamStructure(result); }
                    }
                    return;
                }
                if (current == null) return;
                if (now - itemStarted > 1800) { Halt("当前内容等待超时，已停止"); return; }
                if (phase == "article")
                {
                    object page = await Evaluate("window.StudyLite ? window.StudyLite.article() : null");
                    if (!Valid(stamp)) return;
                    pageScrollEvents = (int)Json.Number(Json.Get(page, "scrollEvents"));
                    pageHidden = Object.Equals(Json.Get(page, "hidden"), true);
                    if (pageHidden) { Halt("网页内核已暂停渲染，已停止；没有记录为完成"); return; }
                    if (Object.Equals(Json.Get(page, "bad"), true))
                    { await Skip(stamp, "文章已下架或不可访问"); return; }
                    if (!Object.Equals(Json.Get(page, "ready"), true))
                    {
                        if (now - itemStarted > 45) await Skip(stamp, "未找到可阅读正文");
                        return;
                    }
                    if (pageScrollEvents == 0)
                    {
                        if (now - itemStarted > 30) Halt("未检测到真实滚动事件，阅读统计可能未启动，已停止");
                        return;
                    }
                    elapsed += delta;
                    Say("文章浏览中 · " + (int)elapsed + " 秒"
                        + (elapsed < ReadingSchedule.InitialSeconds ? "（60 秒起核对积分）" : "（等待平台增分）"));
                    if (readingSchedule.Due(elapsed))
                    {
                        readingSchedule.Checked(elapsed);
                        await LoadProgress(stamp);
                        if (!Valid(stamp)) return;
                        Progress value;
                        progress.TryGetValue("article", out value);
                        Progress confirmed = CreditConfirmation.Check(itemScore, itemMaximum, value);
                        if (confirmed != null || readingSchedule.Expired(elapsed))
                            await CompleteItem(stamp, (int)elapsed, confirmed);
                    }
                }
                else if (phase == "video")
                {
                    object page = await Evaluate("window.StudyLite ? window.StudyLite.videoPage("
                        + (heartbeatResponses == 0 ? "true" : "false") + ") : null");
                    if (!Valid(stamp)) return;
                    if (page == null) { Halt("视频页诊断不可用，已停止"); return; }
                    pageScrollEvents = (int)Json.Number(Json.Get(page, "scrollEvents"));
                    pageHidden = Object.Equals(Json.Get(page, "hidden"), true);
                    if (pageHidden) { Halt("视频网页内核已暂停渲染，已停止；没有记录为完成"); return; }
                    object result = await Media(stamp);
                    if (!Valid(stamp)) return;
                    var media = Json.Array(result).Where(v => Json.Number(Json.Get(v, "duration")) > 0).ToArray();
                    if (media.Length != 1)
                    {
                        if (now - itemStarted > 45) await Skip(stamp, "未识别到单个可播放视频（可能为跨域播放器）");
                        return;
                    }
                    var video = media[0];
                    if (Object.Equals(Json.Get(video, "hidden"), true))
                    { pageHidden = true; Halt("播放器所在页面已暂停渲染，已停止"); return; }
                    if (Json.Number(Json.Get(video, "errorCode")) > 0)
                    { await Skip(stamp, "播放器报告加载或解码错误"); return; }
                    if (Json.Number(Json.Get(video, "playbackRate")) != 1)
                    { Halt("播放器不是正常播放速度，已停止；未确认有效观看"); return; }
                    string source = Json.Text(Json.Get(video, "source"));
                    double position = Json.Number(Json.Get(video, "time")), duration = Json.Number(Json.Get(video, "duration"));
                    if (videoSource != source) { watched = 0; videoPosition = -1; videoSource = source; videoSchedule = new VideoSchedule(); }
                    double moved = position - videoPosition;
                    if (videoPosition >= 0 && moved > 0 && moved <= delta + 2)
                    { watched += Math.Min(moved, delta + 0.3); lastVideoAdvance = now; }
                    videoPosition = position;
                    Say("播放中 · 已实际播放 " + (int)watched + " 秒"
                        + (watched < VideoSchedule.TargetSeconds ? "（60 秒起核对积分）" : "（继续播放并核对积分，最长 90 秒）"));
                    if (VideoSchedule.Finished(watched, duration, Object.Equals(Json.Get(video, "ended"), true)))
                        await FinishItem(stamp, (int)watched);
                    else if (videoSchedule.Due(watched))
                    {
                        videoSchedule.Checked(watched);
                        await LoadProgress(stamp);
                        if (!Valid(stamp)) return;
                        Progress value;
                        progress.TryGetValue("video", out value);
                        Progress confirmed = CreditConfirmation.Check(itemScore, itemMaximum, value);
                        WriteTaskDiagnostic(current, itemScore, value.Current, confirmed == null ? "video_check_unchanged" : "video_check_increased");
                        if (confirmed != null || videoSchedule.Expired(watched))
                        {
                            await PauseVideo(stamp);
                            if (!Valid(stamp)) return;
                            await CompleteItem(stamp, (int)watched, confirmed);
                        }
                    }
                    else if (now - lastVideoAdvance > 75) await Skip(stamp, "视频未开始播放或持续卡住");
                }
            }
            catch (OperationCanceledException) { if (Valid(stamp)) Halt("请求超时，已停止，请重试"); }
            catch (UnauthorizedAccessException) { if (Valid(stamp)) LoseLogin(); }
            catch (InvalidOperationException e) { if (Valid(stamp)) Halt(e.Message); }
            catch { if (Valid(stamp)) Halt("当前接口或页面暂不可用，已停止；未确认平台完成"); }
            finally { busy = false; }
        }
        private async Task Skip(int stamp, string reason)
        {
            failures++;
            if (failures >= 3) { Halt(reason + "；连续 3 条不可用，已停止"); return; }
            Say(reason + "，切换下一条");
            await Next(stamp);
        }
        private async Task FinishItem(int stamp, int seconds)
        {
            if (!Valid(stamp) || current == null) return;
            Item item = current;
            double before = itemScore, maximum = itemMaximum;
            CancellationToken token = cancellation.Token;
            failures = 0;
            Say("视频自然播放结束，正在等待平台积分确认");
            Progress confirmed = await CreditConfirmation.Wait(before, maximum, async delegate {
                await LoadProgress(stamp);
                token.ThrowIfCancellationRequested();
                Progress value;
                return progress.TryGetValue(item.Kind, out value) ? value : null;
            }, (milliseconds, ct) => Task.Delay(milliseconds, ct), token);
            if (!Valid(stamp)) return;
            await PauseVideo(stamp);
            if (!Valid(stamp)) return;
            await CompleteItem(stamp, Math.Max(seconds, (int)Math.Round(watched)), confirmed);
        }
        private async Task PauseVideo(int stamp)
        {
            await Evaluate("window.StudyLite && window.StudyLite.pauseMedia()");
            foreach (var frame in frames.ToArray())
            {
                try { await frame.ExecuteScriptAsync("if(/(^|\\.)xuexi\\.cn$/i.test(location.hostname)) for(const v of document.querySelectorAll('video,audio'))v.pause()"); }
                catch { }
                if (!Valid(stamp)) return;
            }
        }
        private async Task CompleteItem(int stamp, int seconds, Progress confirmed)
        {
            if (!Valid(stamp) || current == null) return;
            Item item = current;
            double before = itemScore;
            failures = 0;
            double after = confirmed == null ? before : confirmed.Current;
            string outcome = confirmed == null ? "no_credit" : "score_increased";
            history.RecordAttempt(account, item, seconds, before, after, outcome);
            WriteTaskDiagnostic(item, before, after, outcome);
            if (confirmed == null)
            {
                if (item.Kind == "video" && ++videoNoCreditCount < 3)
                {
                    Say("本条视频未增分，保留未完成记录，换下一条（连续 " + videoNoCreditCount + " / 3）");
                    await Next(stamp);
                    return;
                }
                Halt("平台积分未增加，已停止；未记为完成。官网日志响应 " + heartbeatResponses
                    + " 次，滚动事件 " + pageScrollEvents + " 次");
                return;
            }
            if (item.Kind == "video") videoNoCreditCount = 0;
            history.RecordConfirmed(account, item, seconds, before, after);
            Say("平台积分已增加：" + before + " → " + after);
            await Next(stamp);
        }
        private void WriteTaskDiagnostic(Item item, double before, double after, string outcome)
        {
            try
            {
                string path = Path.Combine(dataRoot, "task-diagnostics.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 1000000)
                    File.WriteAllText(path, "", Encoding.UTF8);
                File.AppendAllText(path, Json.Encode(new {
                    at = DateTimeOffset.Now.ToString("o"), kind = item.Kind, itemId = item.Id,
                    documentHidden = pageHidden, scrollEvents = pageScrollEvents,
                    officialLogResponses = heartbeatResponses, lastLogHttp = lastHeartbeatHttp,
                    localSeconds = item.Kind == "article" ? (int)elapsed : (int)watched,
                    scoreBefore = before, scoreAfter = after, outcome = outcome
                }) + Environment.NewLine, Encoding.UTF8);
            }
            catch { Say("诊断日志无法写入；平台状态未更改"); }
        }
        private async void OnResponse(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (running && current != null && (phase == "article" || phase == "video"))
            {
                Uri logUri;
                if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out logUri)
                    && logUri.Scheme == "https" && logUri.Host == "iflow-api.xuexi.cn"
                    && e.Request.Method == "POST"
                    && (logUri.AbsolutePath == "/logflow/api/v1/pclog"
                        || logUri.AbsolutePath == "/logflow_seer/api/v1/pclog"))
                {
                    heartbeatResponses++;
                    lastHeartbeatHttp = e.Response.StatusCode;
                }
            }
            if (!running || phase != "exam") return;
            Uri uri;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out uri) || uri.Scheme != "https"
                || uri.Host != "pc-proxy-api.xuexi.cn" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return;
            if (e.Request.Method == "GET" && uri.AbsolutePath == "/api/exam/service/detail/score")
            {
                examScoreResponses++; examScoreHttp = e.Response.StatusCode;
                WriteExamDiagnostic("official_score_response");
                return;
            }
            if (e.Request.Method != "POST" || uri.AbsolutePath != "/api/exam/service/practice/quizSubmitV3") return;
            int stamp = generation;
            WriteExamDiagnostic("submit_http_" + e.Response.StatusCode);
            if (e.Response.StatusCode != 200) { Halt("答题提交请求未成功，请在官网确认后重试"); return; }
            try
            {
                using (var stream = await e.Response.GetContentAsync())
                {
                    if (!Valid(stamp)) return;
                    WriteExamDiagnostic("submit_content_available");
                    object envelope = Json.Parse(await ReadBounded(stream, 2000000, cancellation.Token));
                    if (!Valid(stamp)) return;
                    if (!Json.Ok(envelope)) { Halt("平台未接受答题提交，已停止"); return; }
                    // Submission acceptance is separate from task-credit confirmation.
                    if (!ExamSubmission.Accepted(envelope))
                    { Halt("答题结果结构不匹配，已停止"); return; }
                    examResult = true;
                    engineWindow.HideExam();
                    Say("平台已返回答题结果，正在核对积分");
                }
            }
            catch { if (Valid(stamp)) Halt("无法确认平台答题结果，已停止"); }
        }
        private void WriteExamDiagnostic(string outcome)
        {
            try
            {
                string path = Path.Combine(dataRoot, "task-diagnostics.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 1000000)
                    File.WriteAllText(path, "", Encoding.UTF8);
                Progress value;
                File.AppendAllText(path, Json.Encode(new {
                    at = DateTimeOffset.Now.ToString("o"), kind = "exam", outcome = outcome,
                    scoreBefore = itemScore,
                    scoreAfter = progress.TryGetValue("exam", out value) ? (double?)value.Current : null,
                    officialScoreResponses = examScoreResponses, lastScoreHttp = examScoreHttp
                }) + Environment.NewLine, Encoding.UTF8);
            }
            catch { Say("答题诊断日志无法写入；平台状态未更改"); }
        }
        private void WriteExamStructure(object result)
        {
            try
            {
                string path = Path.Combine(dataRoot, "task-diagnostics.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 1000000)
                    File.WriteAllText(path, "", Encoding.UTF8);
                File.AppendAllText(path, Json.Encode(new {
                    at = DateTimeOffset.Now.ToString("o"), kind = "exam", outcome = "tick",
                    progressKey = examProgressKey,
                    step = Json.Number(Json.Get(result, "step")),
                    count = Json.Number(Json.Get(result, "count")),
                    mode = Json.Text(Json.Get(result, "mode"))
                }) + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
        private async Task VerifyExamResponses()
        {
            running = true; phase = "exam"; examResult = false;
            engineWindow.ShowExam(this);
            await web.CoreWebView2.ExecuteScriptAsync("fetch(" + Json.Encode(BrowserVerification.SubmitUrl)
                + ",{method:'POST'}).then(r=>r.text()).catch(()=>{})");
            for (int i = 0; i < 50 && !examResult; i++) await Task.Delay(100);
            if (!running || !examResult || !engineWindow.VisuallyHidden)
                throw new InvalidOperationException("Manual submission fixture failed: running=" + running
                    + ", result=" + examResult + ", hidden=" + engineWindow.VisuallyHidden + ", state=" + status.Text);
            await web.CoreWebView2.ExecuteScriptAsync("fetch(" + Json.Encode(BrowserVerification.ScoreUrl) + ").then(r=>r.text()).catch(()=>{})");
            for (int i = 0; i < 50 && examScoreResponses == 0; i++) await Task.Delay(100);
            if (examScoreResponses != 1 || examScoreHttp != 200 || progress.ContainsKey("exam"))
                throw new InvalidOperationException("Score response observation must not create task credit");
            examResult = false;
            await web.CoreWebView2.ExecuteScriptAsync("fetch(" + Json.Encode(BrowserVerification.SubmitUrl + "?fixture=malformed")
                + ",{method:'POST'}).then(r=>r.text()).catch(()=>{})");
            for (int i = 0; i < 50 && running; i++) await Task.Delay(100);
            if (running || examResult) throw new InvalidOperationException("Malformed submission must stop without credit");
        }
        private void SyncExamWindow(string mode, bool manual)
        {
            if (mode == "captcha" || manual)
            {
                if (!engineWindow.InteractiveExamWindow) engineWindow.ShowExam(this);
            }
            else engineWindow.HideExam();
        }
        private void VerifyQuietExamWindow()
        {
            SyncExamWindow("answer", false);
            if (!engineWindow.VisuallyHidden) throw new InvalidOperationException("Normal exam must stay visually hidden");
            SyncExamWindow("captcha", true);
            if (!engineWindow.InteractiveExamWindow) throw new InvalidOperationException("Captcha must allow manual intervention");
            SyncExamWindow("answer", false);
            if (!engineWindow.VisuallyHidden) throw new InvalidOperationException("Exam must return to background after captcha");
            SyncExamWindow("answer", true);
            if (!engineWindow.InteractiveExamWindow) throw new InvalidOperationException("Unsupported question must allow manual intervention");
            SyncExamWindow("submit", false);
            if (!engineWindow.VisuallyHidden) throw new InvalidOperationException("Submission must stay visually hidden");
        }
        private async Task FinishSmoke()
        {
            try
            {
                UpdateScoreSummary(1234567, 29);
                summary.Text = "今日任务：2 / 4 项已完成";
                foreach (string kind in scores.Keys)
                    scores[kind].Text = NameOf(kind) + "\r\n" + (kind == "login" ? "1 / 1  已完成" : kind == "article" ? "12 / 12  已完成" : "0 / 12");
                homePage.PerformLayout();
                // Draw only this application, never capture the user's desktop.
                using (var image = new Bitmap(Width, Height))
                {
                    DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
                    image.Save(Path.Combine(smokePath, "window.png"));
                }
                ShowPage(true);
                using (var image = new Bitmap(Width, Height))
                {
                    DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
                    image.Save(Path.Combine(smokePath, "settings.png"));
                }
                ShowPage(false);
                engineWindow.ShowExam(this);
                bool interactiveExam = engineWindow.InteractiveExamWindow;
                engineWindow.Close();
                bool closePreservesHost = !engineWindow.IsDisposed && engineWindow.VisuallyHidden;
                engineWindow.ShowExam(this);
                interactiveExam = interactiveExam && engineWindow.InteractiveExamWindow && closePreservesHost;
                if (browserTest)
                {
                    using (var image = new Bitmap(engineWindow.Width, engineWindow.Height))
                    {
                        engineWindow.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        image.Save(Path.Combine(smokePath, "manual-exam.png"));
                    }
                    using (var image = File.Create(Path.Combine(smokePath, "manual-exam-page.png")))
                        await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                }
                engineWindow.HideExam();
                bool hiddenAfterExam = engineWindow.VisuallyHidden;
                bool hidden = Object.Equals(Json.Parse(await web.CoreWebView2.ExecuteScriptAsync("document.hidden")), true);
                bool layout = interactiveExam && hiddenAfterExam && engineWindow.VisuallyHidden
                    && web.Visible && !hidden && web.Width > 400 && web.Height > 250;
                foreach (Control control in new Control[] {login, start, stop, refresh})
                    layout = layout && control.Right <= control.Parent.ClientSize.Width
                        && control.Bottom <= control.Parent.ClientSize.Height;
                foreach (Label label in scores.Values.Concat(new[] { scoreSummary }))
                    layout = layout && label.Height >= (label == scoreSummary ? 20 : 32)
                        && label.Bottom <= label.Parent.ClientSize.Height
                        && label.Right <= label.Parent.ClientSize.Width;
                if (!layout) throw new InvalidOperationException("Toolbar or browser layout overflow");
                bool trayVerified = await VerifyTray();
                if (!trayVerified) throw new InvalidOperationException("Tray lifecycle verification failed");
                File.WriteAllText(Path.Combine(smokePath, "result.json"), Json.Encode(new {
                    ok = true, engine = "WebView2", version = web.CoreWebView2.Environment.BrowserVersionString,
                    privateMode = web.CoreWebView2.Profile.IsInPrivateModeEnabled,
                    externalNetwork = loginProbe || articleProbe || videoProbe, browserTests = browserTest, qrTests = qrTest,
                    browserVisible = !engineWindow.VisuallyHidden, engineDocumentHidden = hidden,
                    manualExamWindowToggle = interactiveExam && hiddenAfterExam,
                    quietExamVerified = browserTest,
                    trayLifecycleVerified = trayVerified,
                    scoreSummaryVisible = scoreSummary.Text.Contains("1234567") && scoreSummary.Text.Contains("29"),
                    width = Width, height = Height
                }), Encoding.UTF8);
            }
            catch (Exception e) { ExitCode = 1; File.WriteAllText(Path.Combine(smokePath, "error.txt"), e.ToString()); }
            await ExitApplication();
        }
        private void OnClosing(object sender, FormClosingEventArgs args)
        {
            if (InterceptClose(args)) return;
            if (closing) return;
            closing = true; running = false; generation++; cancellation.Cancel(); timer.Stop();
            DisposeTray();
            CloseLogin();
            try { if (smokePath != null && !sessionTest && initialized) web.CoreWebView2.CookieManager.DeleteAllCookies(); } catch { }
            web.Dispose(); engineWindow.Dispose(); http.Dispose(); history.Dispose(); cancellation.Dispose();
            foreach (var stream in testStreams) stream.Dispose();
            // Verification profiles are disposable; the app-owned production profile is retained.
            try { if (smokePath != null && !sessionTest && Directory.Exists(sessionPath)) Directory.Delete(sessionPath, true); } catch { }
        }
    }
}
