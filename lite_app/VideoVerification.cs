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
        private async Task RunVideoProbe()
        {
            var core = web.CoreWebView2;
            int blockedLogs = 0;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e)
            {
                Uri uri;
                bool parsed = Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out uri);
                bool official = parsed && Rules.Platform(e.Request.Uri);
                bool asset = parsed && uri.Scheme == "https" && uri.Host == "g.alicdn.com"
                    && uri.Port == 443 && uri.UserInfo.Length == 0
                    && (e.ResourceContext == CoreWebView2WebResourceContext.Script
                        || e.ResourceContext == CoreWebView2WebResourceContext.Stylesheet
                        || e.ResourceContext == CoreWebView2WebResourceContext.Font
                        || e.ResourceContext == CoreWebView2WebResourceContext.Image);
                bool post = e.Request.Method != "GET" && e.Request.Method != "HEAD" && e.Request.Method != "OPTIONS";
                if (official && post && uri.Host == "iflow-api.xuexi.cn"
                    && (uri.AbsolutePath == "/logflow/api/v1/pclog" || uri.AbsolutePath == "/logflow_seer/api/v1/pclog"))
                    blockedLogs++;
                if (post || (!official && !asset))
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked in read-only probe", "");
            };
            core.Navigate("https://www.xuexi.cn/lgpage/detail/index.html?id=10001201830135450591&item_id=10001201830135450591");
            object video = null;
            for (int i = 0; i < 90; i++)
            {
                await Task.Delay(500);
                if (!navigationReady) continue;
                video = Json.Array(await Evaluate("window.StudyLite ? window.StudyLite.media(true) : null")).FirstOrDefault();
                if (video != null && Json.Number(Json.Get(video, "time")) > 2) break;
            }
            if (video == null || Json.Number(Json.Get(video, "duration")) <= 0)
                throw new InvalidOperationException("Public video did not become ready");
            int logsBefore = blockedLogs;
            double timeBefore = Json.Number(Json.Get(video, "time"));
            object page = null;
            for (int i = 0; i < 30; i++)
            {
                page = await Evaluate("window.StudyLite.videoPage(true)");
                await Evaluate("window.StudyLite.media(true)");
                await Task.Delay(500);
                if (blockedLogs > logsBefore && Json.Number(Json.Get(page, "scrollEvents")) > 0) break;
            }
            int initialLogs = blockedLogs;
            for (int i = 0; i < 34; i++)
            {
                await Task.Delay(1000);
                await Evaluate("window.StudyLite.media(true)");
            }
            page = await Evaluate("window.StudyLite.videoPage(false)");
            video = Json.Array(await Evaluate("window.StudyLite.media(false)")).FirstOrDefault();
            double timeAfter = Json.Number(Json.Get(video, "time"));
            bool success = logsBefore == 0 && initialLogs > logsBefore && blockedLogs > initialLogs
                && !Object.Equals(Json.Get(page, "hidden"), true)
                && Json.Number(Json.Get(page, "scrollEvents")) > 0 && timeAfter - timeBefore > 30;
            File.WriteAllText(Path.Combine(smokePath, "video-probe.json"), Json.Encode(new {
                ok = success, documentHidden = Json.Get(page, "hidden"),
                scrollEvents = Json.Get(page, "scrollEvents"), logRequestsBeforeInteraction = logsBefore,
                initialLogRequestsBlocked = initialLogs, officialLogRequestsBlocked = blockedLogs,
                playbackBefore = timeBefore, playbackAfter = timeAfter,
                credentialsUsed = false, postRequestsAllowed = false, scoringTested = false
            }), Encoding.UTF8);
            await Evaluate("window.StudyLite.pauseMedia()");
            if (!success) throw new InvalidOperationException("Public video interaction comparison failed");
        }
    }
}
