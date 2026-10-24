using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace StudyLite
{
    // All requests in this test are fulfilled in memory or blocked, never forwarded.
    internal static class BrowserVerification
    {
        internal const string PageUrl = "https://pc.xuexi.cn/__study_lite_offline_test__";
        internal const string SubmitUrl = "https://pc-proxy-api.xuexi.cn/api/exam/service/practice/quizSubmitV3";
        internal const string ScoreUrl = "https://pc-proxy-api.xuexi.cn/api/exam/service/detail/score";
        internal const string QuestionUrl = "https://pc-proxy-api.xuexi.cn/api/exam/service/common/deduplicateRandomSearchV3?activityCode=QUIZ_ALL&forced=0&limit=5";

        // 模拟官方取题响应：data_str 直接是可展开的 JSON 字符串，题目自带 correct 答案。
        internal static string QuestionPayload()
        {
            return @"{""ok"":true,""code"":200,""data_str"":{""uniqueId"":""fixture-quiz-001"",""nextIndex"":5,""questions"":["
                + SingleChoice() + "," + MultiChoice() + "," + FillQuestion() + "," + JudgeQuestion() + "," + LastChoice() + "]}}";
        }
        private static string SingleChoice()
        {
            return @"{""questionId"":101,""body"":""离线单选题：甲是____？"",""typeName"":""单选题"",""answerInputType"":1,""questionDisplay"":1,"
                + @"""answers"":[{""answerId"":1011,""label"":""A"",""content"":""甲""},{""answerId"":1012,""label"":""B"",""content"":""乙"",""pictureUrl"":""""},"
                + @"{""answerId"":1013,""label"":""C"",""content"":""丙""},{""answerId"":1014,""label"":""D"",""content"":""丁""}],"
                + @"""correct"":[{""answerId"":1012,""value"":""B""}]}";
        }
        private static string MultiChoice()
        {
            return @"{""questionId"":102,""body"":""离线多选题：应选____？"",""typeName"":""多选题"",""answerInputType"":1,""questionDisplay"":3,"
                + @"""answers"":[{""answerId"":1021,""label"":""A"",""content"":""甲""},{""answerId"":1022,""label"":""B"",""content"":""乙""},"
                + @"{""answerId"":1023,""label"":""C"",""content"":""丙""},{""answerId"":1024,""label"":""D"",""content"":""丁""}],"
                + @"""correct"":[{""answerId"":1021,""value"":""A""},{""answerId"":1023,""value"":""C""}]}";
        }
        private static string FillQuestion()
        {
            return @"{""questionId"":103,""body"":""离线填空题：我国的执政党是（）"",""typeName"":""填空题"",""answerInputType"":2,""questionDisplay"":4,"
                + @"""answers"":[{""answerId"":1031,""label"":"""",""content"":""中国共产党""}],"
                + @"""correct"":[{""answerId"":1031,""value"":""中国共产党""}]}";
        }
        private static string JudgeQuestion()
        {
            return @"{""questionId"":104,""body"":""离线判断题：地球是圆的。"",""typeName"":""判断题"",""answerInputType"":1,""questionDisplay"":2,"
                + @"""answers"":[{""answerId"":1041,""label"":""A"",""content"":""正确""},{""answerId"":1042,""label"":""B"",""content"":""错误""}],"
                + @"""correct"":[{""answerId"":1041,""value"":""正确""}]}";
        }
        private static string LastChoice()
        {
            return @"{""questionId"":105,""body"":""离线末题：2+2=____？"",""typeName"":""单选题"",""answerInputType"":1,""questionDisplay"":1,"
                + @"""answers"":[{""answerId"":1051,""label"":""A"",""content"":""3""},{""answerId"":1052,""label"":""B"",""content"":""4""}],"
                + @"""correct"":[{""answerId"":1052,""value"":""4""}]}";
        }
        internal static string Response(string url)
        {
            if (url == PageUrl) return FixturePage();
            if (url == QuestionUrl) return QuestionPayload();
            if (url == SubmitUrl) return @"{""ok"":true,""code"":200,""data"":{""totalNum"":0}}";
            if (url == SubmitUrl + "?fixture=malformed") return @"{""ok"":true,""code"":200,""data"":{""totalNum"":""invalid""}}";
            if (url == ScoreUrl) return @"{""ok"":true,""code"":200,""data"":{}}";
            return null;
        }

        // 模拟官方答题页：渲染题目、处理选项/填空/按钮状态流转，最终点击“完成”后向
        // quizSubmitV3 提交并记录作答结果。所有交互均由真实 DOM 事件驱动。
        // 使用占位符插入测试 URL，避免在 C# 字符串中混用引号拼接。
        private static string FixturePage()
        {
            return (@"<!doctype html><meta charset='utf-8'>
                <title>本地模拟每日答题</title>
                <style>body{font:16px 'Microsoft YaHei UI';padding:25px;color:#293038}
                article{height:5000px}
                .q-answer{padding:12px;border:1px solid #ddd;margin:10px 0;cursor:pointer;display:block}
                .q-answer.chosen{border-color:#19874b;background:#e7f5eb}
                input.blank{border:1px solid #bbb;padding:6px;font-size:16px;width:220px}
                button{padding:8px 24px}.action-row{margin-top:18px}
                button:disabled{opacity:.5}</style>
                <h3>本地模拟每日答题</h3>
                <div class='header-row'><div class='pager'><span class='big'>1</span>/5</div></div>
                <div id='q-card'>
                  <div class='q-header'><span>单选题</span></div>
                  <div class='q-body'>加载中…</div>
                  <div class='q-answers'></div>
                  <div class='action-row'><button class='next-btn' disabled>确定</button></div>
                </div>
                <script>
                (function(){
                  var QUESTIONS=null, STEP=0, committed=false, nextTimer=null;
                  var card=document.getElementById('q-card');
                  var body=card.querySelector('.q-body');
                  var area=card.querySelector('.q-answers');
                  var btn=card.querySelector('.next-btn');
                  window.__examFixture={submitted:false,correct:0,total:0,perQuestion:[],submitClicks:0};
                  function submit(){
                    if(window.__examFixture.submitted)return;
                    window.__examFixture.submitted=true;
                    window.__examFixture.submitClicks++;
                    window.__examFixture.total=QUESTIONS.length;
                    btn.disabled=true;
                    fetch('__SUBMIT_URL__',{method:'POST'}).then(function(r){return r.text();}).then(function(){
                      card.style.display='none';
                    }).catch(function(){});
                  }
                  function next(){
                    clearTimeout(nextTimer);
                    if(STEP<QUESTIONS.length-1){STEP++;refresh();}
                  }
                  function refresh(){
                    var q=QUESTIONS[STEP];
                    card.querySelector('.q-header').textContent=q.typeName||'单选题';
                    body.textContent=q.body;
                    document.querySelector('.pager .big').textContent=STEP+1;
                    area.innerHTML='';
                    btn.disabled=true; btn.textContent='确定'; committed=false;
                    var correctIds=q.correct.map(function(c){return c.answerId;});
                    if(q.questionDisplay===4){
                      var i=document.createElement('input');
                      i.className='blank'; i.type='text'; i.maxLength=51;
                      i.addEventListener('input',function(){
                        btn.disabled=(i.value||'').length===0;
                      });
                      area.appendChild(i);
                      window.__examFixture.perQuestion.push({type:'fill',answerId:q.correct[0].answerId});
                      return;
                    }
                    q.answers.forEach(function(a,idx){
                      var d=document.createElement('div');
                      d.className='q-answer choosable';
                      d.textContent=(a.label?a.label+'.':'')+a.content;
                      d.addEventListener('click',function(){
                        if(committed)return;
                        if(d.classList.contains('chosen'))d.classList.remove('chosen');
                        else d.classList.add('chosen');
                        var chosen=Array.prototype.map.call(area.querySelectorAll('.q-answer.chosen'),
                          function(e){return q.answers[Array.prototype.indexOf.call(area.children,e)].answerId;});
                        btn.disabled=chosen.length===0;
                      });
                      area.appendChild(d);
                    });
                    window.__examFixture.perQuestion.push({type:'choice',correct:correctIds});
                  }
                  btn.addEventListener('click',function(){
                    if(btn.disabled)return;
                    if(btn.textContent==='确定'){
                      committed=true;
                      var q=QUESTIONS[STEP];
                      if(q.questionDisplay===4){
                        var val=area.querySelector('input.blank').value;
                        var ok=(val||'')===q.correct[0].value;
                        if(ok)window.__examFixture.correct++;
                        window.__examFixture.perQuestion[STEP].filled=val;
                        window.__examFixture.perQuestion[STEP].ok=ok;
                      }else{
                        var chosen=Array.prototype.map.call(area.querySelectorAll('.q-answer.chosen'),
                          function(e){return q.answers[Array.prototype.indexOf.call(area.children,e)].answerId;});
                        var correctIds=q.correct.map(function(c){return c.answerId;});
                        var ok=correctIds.length===chosen.length && correctIds.every(function(id){return chosen.indexOf(id)>=0;});
                        if(ok)window.__examFixture.correct++;
                        window.__examFixture.perQuestion[STEP].chosen=chosen;
                        window.__examFixture.perQuestion[STEP].ok=ok;
                      }
                      btn.textContent=STEP < QUESTIONS.length-1 ? '下一题' : '完成';
                      if(ok){
                        if(STEP===QUESTIONS.length-1)submit();
                        else nextTimer=setTimeout(next,1000);
                      }
                    }else if(btn.textContent==='下一题'){
                      next();
                    }else if(btn.textContent==='完成'){
                      submit();
                    }
                  });
                  fetch('__QUESTION_URL__').then(function(r){return r.json();}).then(function(envelope){
                    var inner=typeof envelope.data_str==='string'?JSON.parse(envelope.data_str):envelope.data_str;
                    QUESTIONS=inner.questions; refresh();
                  }).catch(function(){});
                })();
                </script>
                <article class='article-content'>" + new string('x', 300) + "</article><video id='fixture-video' muted preload='auto' "
                + "style='display:none;width:320px;height:180px' src='data:audio/wav;base64,"
                + VideoWave() + "'></video>")
                .Replace("__SUBMIT_URL__", SubmitUrl)
                .Replace("__QUESTION_URL__", QuestionUrl);
        }
        private static async Task<object> Eval(CoreWebView2 core, string script)
        {
            return Json.Parse(await core.ExecuteScriptAsync(script));
        }
        private static string VideoWave()
        {
            using (var memory = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(memory))
            {
                int length = 8000 * 2 * 5;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(length + 36);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(length);
                writer.Write(new byte[length]);
                return Convert.ToBase64String(memory.ToArray());
            }
        }
        internal static async Task Run(CoreWebView2 core, Func<bool> visuallyHidden)
        {
            if (!visuallyHidden()) throw new InvalidOperationException("Exam test must begin without a visible browser window");
            if (!Object.Equals(await Eval(core, "document.hidden"), false))
                throw new InvalidOperationException("Transparent host must retain native rendering");
            if (!Object.Equals(await Eval(core, "Object.prototype.hasOwnProperty.call(document,'hidden')"), false))
                throw new InvalidOperationException("Document visibility must remain native");
            await core.ExecuteScriptAsync(@"window.fixtureStarted=false;window.fixtureTrusted=false;
                document.addEventListener('scroll',function(e){fixtureStarted=true;fixtureTrusted=e.isTrusted;},{once:true});");
            object article = null;
            for (int i = 0; i < 20; i++)
            {
                article = await Eval(core, "window.StudyLite.article()");
                await Task.Delay(150);
                if (Object.Equals(await Eval(core, "window.fixtureStarted"), true)) break;
            }
            article = await Eval(core, "window.StudyLite.article()");
            if (!Object.Equals(Json.Get(article, "ready"), true)
                || Json.Number(Json.Get(article, "scrollEvents")) <= 0
                || !Object.Equals(await Eval(core, "window.fixtureTrusted"), true)
                || Json.Number(await Eval(core, "window.scrollY")) <= 0)
                throw new InvalidOperationException("Article smooth scroll must emit real native events");
            await core.ExecuteScriptAsync("document.querySelector('article').remove();window.scrollTo(0,0);window.StudyLite.start()");

            // ---- 自动答题：等待取题响应被内置浏览器捕获，然后逐题自动作答并提交 ----
            object snap = null;
            for (int i = 0; i < 50; i++)
            {
                snap = await Eval(core, "window.StudyLite.examSnapshot()");
                if (Json.Number(Json.Get(snap, "count")) == 5) break;
                await Task.Delay(200);
            }
            if (snap == null || Json.Number(Json.Get(snap, "count")) != 5)
                throw new InvalidOperationException("Exam question payload was not captured from the built-in browser");
            bool done = false;
            for (int i = 0; i < 150 && !done; i++)
            {
                if (!visuallyHidden()) throw new InvalidOperationException("Automatic exam unexpectedly opened a browser window");
                object step = await Eval(core, "window.StudyLite.examAuto()");
                string mode = Json.Text(Json.Get(step, "mode"));
                if (mode == "error" || mode == "stopped")
                    throw new InvalidOperationException("Auto exam stopped: " + Json.Text(Json.Get(step, "status")));
                done = mode == "done";
                if (!done) await Task.Delay(1300);
            }
            if (!done)
                throw new InvalidOperationException("Auto exam did not finish within the offline window: " + Json.Text(Json.Get(await Eval(core, "window.StudyLite.examSnapshot()"), "status")));
            if (!visuallyHidden()) throw new InvalidOperationException("Completed exam must remain visually hidden");
            object fixture = await Eval(core, "window.__examFixture");
            if (!Object.Equals(Json.Get(fixture, "submitted"), true)
                || Json.Number(Json.Get(fixture, "submitClicks")) != 1
                || Json.Number(Json.Get(fixture, "correct")) != 5
                || Json.Number(Json.Get(fixture, "total")) != 5)
                throw new InvalidOperationException("Auto exam did not answer every question correctly: "
                    + Json.Encode(fixture));
            snap = await Eval(core, "window.StudyLite.examSnapshot()");
            if (Json.Number(Json.Get(snap, "step")) != 5 || Json.Number(Json.Get(snap, "count")) != 5)
                throw new InvalidOperationException("Auto exam did not advance through all questions");
            await core.ExecuteScriptAsync("window.StudyLite.stop()");

            // ---- 视频与页面交互验证（与 2.3 一致） ----
            await core.ExecuteScriptAsync(@"document.body.style.minHeight='2500px';
                const v=document.getElementById('fixture-video');v.style.display='block';
                v.style.position='absolute';v.style.top='100px';window.scrollTo(0,0);");
            await Task.Delay(400);
            await core.ExecuteScriptAsync(@"window.videoTrustedScroll=false;
                document.addEventListener('scroll',function(e){videoTrustedScroll=videoTrustedScroll||e.isTrusted;});
                window.StudyLite.videoPage(true);window.StudyLite.media(true);");
            await Task.Delay(1300);
            object videoState = await Eval(core, @"({page:StudyLite.videoPage(false),
                media:StudyLite.media(false),trusted:window.videoTrustedScroll})");
            var video = System.Linq.Enumerable.FirstOrDefault(Json.Array(Json.Get(videoState, "media")));
            if (video == null || Json.Number(Json.Get(video, "time")) <= 0
                || Json.Number(Json.Get(video, "duration")) <= 0
                || Json.Number(Json.Get(video, "playbackRate")) != 1
                || !Object.Equals(Json.Get(videoState, "trusted"), true)
                || Object.Equals(Json.Get(Json.Get(videoState, "page"), "hidden"), true))
                throw new InvalidOperationException("Video playback or native page interaction failed");
            await core.ExecuteScriptAsync("window.StudyLite.pauseMedia()");
            if (!Object.Equals(await Eval(core, "document.getElementById('fixture-video').paused"), true))
                throw new InvalidOperationException("Video stop must pause native media");
        }
    }
}
