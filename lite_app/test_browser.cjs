const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const script = fs.readFileSync('browser.js', 'utf8');

function fixture() {
  let now = 10000, buttonText = '确定', clicks = [], inputEvents = [], page = 0, total = 0;
  let options = [], inputs = [], videos = [], frames = [], scrollCalls = [], listeners = [], guarded = false;
  const element = (text, extra = {}) => {
    const classes = new Set();
    const el = {
      innerText: text, textContent: text, disabled: false, readOnly: false,
      getClientRects: () => [{}], getAttribute: () => null,
      classList: {
        contains: c => classes.has(c),
        add: c => classes.add(c),
        remove: c => classes.delete(c),
        toggle: c => classes.has(c) ? (classes.delete(c), false) : (classes.add(c), true),
      },
      click() {
        clicks.push(this.innerText);
        if (this._selectable) this.classList.toggle('chosen');
      },
      ...extra,
    };
    return el;
  };
  const stem = element('测试题目？'), button = element(buttonText, {_selectable: false});
  const document = {
    addEventListener(name, fn) { listeners[name] = fn; },
    querySelector: s => {
      if (s === '.q-body') return stem;
      if (s === '.action-row .next-btn') return button;
      if (s === '.click-box') return null;
      if (s === '.header-row .pager .big') return element(String(page + 1));
      if (s === '.header-row .pager') return element((page + 1) + '/' + total);
      return null;
    },
    querySelectorAll: s => {
      if (s === '.q-body') return [stem];
      if (s === '.action-row .next-btn, .next-btn') return [button];
      if (s === '.q-answers .q-answer') return options;
      if (s === 'input.blank') return inputs;
      if (s === '.q-answers .fill-answer') return [];
      if (s === '.nc-container,.nc_wrapper,iframe[src*="captcha"],.captcha') return guarded ? [element('')] : [];
      if (s === 'video' || s === 'video,audio') return videos;
      if (s === 'iframe') return frames;
      return [];
    },
  };
  function XHR() { this.handlers = {}; this.status = 200; this.responseText = ''; }
  XHR.prototype.open = function (method, url) { this.__slExam = {method: String(method).toUpperCase(), url: String(url)}; };
  XHR.prototype.addEventListener = function (name, fn) { this.handlers[name] = fn; };
  XHR.prototype.send = function () { if (this.handlers.load) this.handlers.load(); };
  class Input {
    constructor() {
      this.currentValue = '';
      this.addEventListener = () => {};
    }
    dispatchEvent(e) { inputEvents.push(e.type); }
  }
  Object.defineProperty(Input.prototype, 'value', {
    get() { return this.currentValue; },
    set(v) { this.currentValue = v; },
  });
  const location = {hostname: 'pc.xuexi.cn', href: 'https://pc.xuexi.cn/points/exam-practice.html'};
  const context = {
    document, location, XMLHttpRequest: XHR, TextDecoder, Uint8Array, URL,
    HTMLInputElement: Input, Event: class { constructor(type) { this.type = type; } },
    Date: {now: () => now}, atob: t => Buffer.from(t, 'base64').toString('binary'),
    fetch: async () => ({ok: false}), console,
    scrollY: 0, innerHeight: 800,
    scrollBy(value) { scrollCalls.push(value); context.scrollY += value.top; listeners.scroll(); },
  };
  context.window = context; context.top = context;
  vm.runInNewContext(script, context);
  const api = context.StudyLite;
  const questionUrl = 'https://pc-proxy-api.xuexi.cn/api/exam/service/common/deduplicateRandomSearchV3?activityCode=QUIZ_ALL&forced=0&limit=5';
  return {
    api, clicks, inputEvents, scrollCalls, document,
    tick() { now += 2000; return api.tick(); },
    guarded(value) { guarded = value; },
    options(texts) { options = texts.map(t => element(t, {_selectable: true})); },
    stem(text) { stem.innerText = text; stem.textContent = text; },
    page(index) { page = index; },
    advance(ms = 2000) { now += ms; },
    button(text) { button.innerText = text; button.textContent = text; },
    inputs(count) {
      inputs = Array.from({length: count}, () => Object.assign(new Input(), {getClientRects: () => [{}]}));
      return inputs;
    },
    payload(questions, uniqueId = 'fixture-u1', url = questionUrl, accepted = true) {
      total = questions.length;
      const xhr = new XHR();
      xhr.responseText = JSON.stringify({ok: accepted, code: accepted ? 200 : 403,
        data_str: JSON.stringify({uniqueId, nextIndex: questions.length, questions})});
      xhr.open('GET', url);
      xhr.send();
    },
    submission() {
      const xhr = new XHR();
      xhr.open('POST', 'https://pc-proxy-api.xuexi.cn/api/exam/service/practice/quizSubmitV3');
      xhr.send();
    },
    video(box = {top: 200, bottom: 500}) {
      const video = Object.assign(element(''), {
        currentSrc: 'https://video.xuexi.cn/synthetic.mp4', currentTime: 3, duration: 120,
        paused: true, ended: false, playbackRate: 1, readyState: 4, error: null, playCalls: 0,
        play() { this.playCalls++; this.paused = false; return Promise.resolve(); },
        pause() { this.paused = true; },
        getBoundingClientRect: () => box,
        scrollIntoView(value) { scrollCalls.push(value); listeners.scroll(); },
      });
      videos = [video]; return video;
    },
    frame(src) {
      const frame = Object.assign(element(''), {src, getBoundingClientRect: () => ({top: 200, bottom: 500})});
      frames = [frame];
    },
  };
}

const q = (id, answers, correct, extra = {}) => ({
  questionId: id, body: '题目' + id, typeName: '单选题', answerInputType: 1, questionDisplay: 1,
  answers, correct, ...extra,
});
const ans = (answerId, label, content) => ({answerId, label, content});

{
  const f = fixture(), video = f.video();
  const before = f.api.videoPage(false);
  assert.equal(before.scrollEvents, 0, 'observing does not scroll');
  f.api.media(true);
  assert.equal(video.playCalls, 1, 'play calls real media API');
  assert.equal(f.scrollCalls.length, 0, 'media.play alone does not initiate page interaction');
  f.api.videoPage(true);
  assert.equal(f.scrollCalls.length, 1, 'video initialization scrolls actual page');
  assert.equal(f.api.videoPage(true).scrollEvents, 1, 'video exposes real observed scroll count');
  assert.equal(f.scrollCalls.length, 1, 'scroll requests throttled');
  f.tick(); f.tick(); f.tick();
  f.api.videoPage(false);
  assert.equal(f.scrollCalls.length, 1, 'stop interacting after log response');
  f.document.hidden = true;
  assert.equal(f.api.videoPage(false).hidden, true, 'video visibility is not spoofed');
  assert.equal(f.api.media(false)[0].hidden, true);
  f.api.pauseMedia(); assert.equal(video.paused, true);
}
{
  const f = fixture(); f.video({top: 1200, bottom: 1500});
  f.api.videoPage(true);
  assert.equal(f.scrollCalls[0].block, 'center', 'offscreen player is brought into view');
}
{
  const f = fixture(); f.frame('https://video.xuexi.cn/player');
  assert.equal(f.api.videoPage(true).playerFound, true, 'official iframe gets parent interaction');
  assert.equal(f.scrollCalls.length, 1);
}
{
  const f = fixture(); f.frame('https://xuexi.cn.evil.test/player');
  assert.equal(f.api.videoPage(true).playerFound, false, 'external iframe is not a playback target');
  assert.equal(f.scrollCalls.length, 0);
}

{
  // 自动答题：单选 → 多选（按 correctAnswerSeq 字符串顺序）→ 填空 → 判断题 → 末题提交
  const f = fixture();
  f.payload([
    q(1, [ans(11,'A','甲'), ans(12,'B','乙'), ans(13,'C','丙'), ans(14,'D','丁')], [{answerId: 12, value: 'B'}]),
    q(2, [ans(21,'A','甲'), ans(22,'B','乙'), ans(23,'C','丙'), ans(24,'D','丁')], [{answerId: 21, value: 'A'}, {answerId: 23, value: 'C'}], {typeName: '多选题', questionDisplay: 3, correctAnswerSeq: 'AC'}),
    q(3, [ans(31,'','中国共产党')], [{answerId: 31, value: '中国共产党'}], {typeName: '填空题', answerInputType: 2, questionDisplay: 4}),
    q(4, [ans(41,'A','正确'), ans(42,'B','错误')], [{answerId: 41, value: '正确'}], {typeName: '判断题', questionDisplay: 2}),
    q(5, [ans(51,'A','3'), ans(52,'B','4')], [{answerId: 52, value: '4'}]),
  ]);
  f.api.start();

  // Q1 单选：点选→确认→提交
  f.options(['A.甲', 'B.乙', 'C.丙', 'D.丁']); f.inputs(0); f.button('确定');
  let r = f.api.examAuto();
  assert.equal(r.mode, 'answer', 'single choice first click');
  assert.deepEqual(f.clicks.slice(-1), ['B.乙'], 'correct single option clicked');
  r = f.api.examAuto();
  assert.equal(r.mode, 'commit', 'single choice selected then commit');
  r = f.api.examAuto();
  assert.equal(r.mode, 'advance', 'commit tick presses 确定');
  assert.deepEqual(f.clicks.slice(-2), ['B.乙', '确定']);
  f.button('下一题'); r = f.api.examAuto();
  assert.equal(r.step, 0, 'clicking next alone does not assume the page advanced');
  f.page(1); f.button('确定'); r = f.api.examAuto();
  assert.equal(r.mode, 'answer', 'advance tick moves to question 2');
  assert.equal(f.api.examSnapshot().step, 1, 'step advances after 下一题');

  // Q2 多选：按 correctAnswerSeq 字符串顺序逐项点击 A、C
  f.options(['A.甲', 'B.乙', 'C.丙', 'D.丁']); f.inputs(0); f.button('确定');
  r = f.api.examAuto();
  assert.equal(r.mode, 'answer', 'multi choice first option click');
  assert.deepEqual(f.clicks.slice(-1), ['A.甲'], 'first multi option in sequence');
  r = f.api.examAuto();
  assert.equal(r.mode, 'answer', 'multi choice second option click');
  assert.deepEqual(f.clicks.slice(-2), ['A.甲', 'C.丙'], 'multi options clicked in correctAnswerSeq order');
  r = f.api.examAuto();
  assert.equal(r.mode, 'commit', 'multi choice selected then commit');
  f.api.examAuto();
  f.button('下一题'); f.api.examAuto(); f.page(2);
  f.inputs(1); f.button('确定');
  assert.equal(f.api.examAuto().step, 2, 'question 3 reached');

  // Q3 填空：写入正确答案并触发 input/change
  f.inputs(1); f.button('确定');
  r = f.api.examAuto();
  assert.equal(r.mode, 'advance', 'fill question is submitted after rendering is confirmed');
  assert.deepEqual(f.inputEvents.slice(-2), ['input', 'change'], 'fill input dispatches native input events');
  assert.equal(f.api.examSnapshot().step, 2);
  f.button('下一题'); f.api.examAuto(); f.page(3);

  // Q4 判断题：点“正确”
  f.options(['A.正确', 'B.错误']); f.inputs(0); f.button('确定');
  r = f.api.examAuto();
  assert.equal(r.step, 3, 'question 4 reached');
  assert.equal(r.mode, 'answer', 'judge question first click');
  r = f.api.examAuto();
  assert.equal(r.mode, 'commit');
  assert.deepEqual(f.clicks.slice(-1), ['A.正确'], 'judge question picks correct option');
  f.api.examAuto();
  f.button('下一题'); f.api.examAuto(); f.page(4);

  // Q5 末题：选择后提交“完成”
  f.options(['A.3', 'B.4']); f.inputs(0); f.button('确定');
  r = f.api.examAuto();
  assert.equal(r.step, 4, 'question 5 reached');
  assert.equal(r.mode, 'answer', 'last question first click');
  r = f.api.examAuto();
  assert.equal(r.mode, 'commit');
  assert.deepEqual(f.clicks.slice(-1), ['B.4'], 'last question option clicked');
  f.api.examAuto();
  f.advance(4000); f.button('完成'); r = f.api.examAuto();
  assert.equal(r.mode, 'submit', 'last question presses 完成');
  assert.deepEqual(f.clicks.slice(-2), ['确定', '完成']);
  assert.equal(f.api.examAuto().mode, 'submit', 'clicking complete alone is not submission acceptance');
  assert.equal(f.api.examSnapshot().step, 4, 'final page index is preserved until acceptance');
  f.submission();
  assert.equal(f.api.examAuto().mode, 'done', 'official submission response is observed');
  assert.equal(f.api.examSnapshot().step, 5, 'submission response completes the question sequence');
  f.api.stop();
  assert.equal(f.api.examAuto().mode, 'stopped', 'stop halts auto answering');
}

{
  const questions = [q(1, [ans(11,'A','甲')], [{answerId: 11, value: 'A'}])];
  const f = fixture(); f.api.start();
  f.payload(questions, 'bad-host', 'https://example.test/api/exam/service/common/deduplicateRandomSearchV3?activityCode=QUIZ_ALL');
  assert.equal(f.api.examAuto().mode, 'wait', 'external hosts cannot supply questions');
  f.payload(questions, 'wrong-activity', 'https://pc-proxy-api.xuexi.cn/api/exam/service/common/deduplicateRandomSearchV3?activityCode=SPECIAL');
  assert.equal(f.api.examAuto().mode, 'wait', 'other activities cannot replace the daily quiz');
  f.payload(questions, 'rejected', undefined, false);
  assert.equal(f.api.examAuto().mode, 'wait', 'rejected envelopes cannot supply questions');
}

{
  const f = fixture();
  const questions = [
    q(1, [ans(11,'A','甲')], [{answerId: 11, value: 'A'}]),
    q(2, [ans(21,'A','乙')], [{answerId: 21, value: 'A'}]),
  ];
  f.payload(questions); f.api.start(); f.page(1); f.options(['A.乙']);
  f.api.examAuto(); f.payload(questions);
  assert.equal(f.api.examSnapshot().step, 1, 'duplicate payload does not reset progress');
  f.api.examAuto(); f.api.examAuto(); f.button('下一题'); f.advance(4000);
  f.api.examAuto(); const count = f.clicks.length;
  f.advance(4000); f.api.examAuto();
  assert.equal(f.clicks.length, count, 'waiting for a rendered next page does not double-click');
}

{
  const f = fixture();
  f.payload([
    q(1, [ans(11,'A','甲'), ans(12,'B','乙')], [{answerId: 12, value: 'B'}]),
    q(2, [ans(21,'A','丙'), ans(22,'B','丁')], [{answerId: 21, value: 'A'}]),
  ]);
  f.options(['A.甲', 'B.乙']); f.api.start();
  f.api.examAuto(); f.api.examAuto(); f.api.examAuto();
  // The official page advances after one second, before the next host tick.
  f.page(1); f.options(['A.丙', 'B.丁']); f.button('确定');
  const result = f.api.examAuto();
  assert.equal(result.step, 1, 'official auto-next synchronizes the question index');
  assert.equal(result.mode, 'answer', 'auto-next exits the previous advance state');
  assert.deepEqual(f.clicks.slice(-1), ['A.丙'], 'new page gets its own answer');
  assert.equal(f.clicks.includes('下一题'), false, 'auto-next needs no extra next click');
  const serialized = JSON.stringify(f.api.examSnapshot());
  for (const secret of ['correctVals', 'correctIds', 'correctAnswerSeq', 'answerLabels', 'uniqueId'])
    assert.equal(serialized.includes(secret), false, 'snapshots exclude ' + secret);
}

{
  const f = fixture();
  const inputs = f.inputs(2);
  f.payload([q(1, [ans(11,'','first'), ans(12,'','second')],
    [{answerId: 12, value: 'second'}, {answerId: 11, value: 'first'}],
    {typeName: '填空题', answerInputType: 2, questionDisplay: 4})]);
  f.api.start(); f.api.examAuto();
  assert.deepEqual(inputs.map(i => i.value), ['first', 'second'], 'blank order follows answerId, not correct array order');
}

{
  // 未获取题目数据：等待状态
  const f = fixture();
  f.api.start();
  assert.equal(f.api.examAuto().mode, 'wait', 'waits for question payload before acting');
  assert.equal(f.clicks.length, 0, 'no interaction before payload');
  f.api.stop();
}

{
  // 验证码出现时暂停自动作答，等待用户手动处理
  const f = fixture();
  f.options(['A.甲', 'B.乙']);
  f.payload([q(1, [ans(11,'A','甲'), ans(12,'B','乙')], [{answerId: 12, value: 'B'}])]);
  f.api.start();
  assert.equal(f.api.examAuto().mode, 'answer', 'captcha block first picks option');
  assert.equal(f.api.examAuto().mode, 'commit', 'captcha block then commits');
  f.guarded(true);
  assert.equal(f.api.examAuto().mode, 'captcha', 'captcha pauses auto answering');
  assert.equal(f.api.examAuto().status, '请在答题窗口手动完成验证');
  f.guarded(false);
  assert.equal(f.api.examAuto().mode, 'advance', 'auto answering resumes after captcha');
}

{
  // 填空题：正确答案完整写入输入框后进入提交
  const f = fixture();
  f.inputs(1);
  f.payload([q(3, [ans(31,'','中国共产党')], [{answerId: 31, value: '中国共产党'}], {typeName: '填空题', answerInputType: 2, questionDisplay: 4})]);
  f.api.start();
  const r = f.api.examAuto();
  assert.equal(r.mode, 'commit', 'fill writes the correct value and commits');
  assert.deepEqual(f.inputEvents.slice(-2), ['input', 'change']);
}


{
  // 回退：correct.answerId 与选项 answerId 不一致时，按答案文字匹配选项
  const f = fixture();
  f.options(['A.甲', 'B.乙', 'C.丙']);
  f.payload([q(1, [ans(91,'A','甲'), ans(92,'B','乙'), ans(93,'C','丙')], [{answerId: 999, value: '乙'}])]);
  f.api.start();
  let r = f.api.examAuto();
  assert.equal(r.mode, 'answer', 'fallback first click');
  assert.deepEqual(f.clicks.slice(-1), ['B.乙'], 'fallback matches by value text');
  r = f.api.examAuto();
  assert.equal(r.mode, 'commit', 'fallback then commits');
  f.api.stop();
}
{
  const f = fixture();
  f.options(['A.甲', 'B.乙']);
  f.payload([q(1, [ans(11,'A','甲'), ans(12,'B','乙')], [{answerId: 999, value: 'unmatched'}])]);
  f.api.start();
  const result = f.api.examAuto();
  assert.equal(result.manual, true, 'unmatched question explicitly requests an intervention window');
  assert.equal(f.clicks.length, 0, 'manual fallback does not blindly click options');
}
console.log('PASS: real-media API, video-page scroll initialization, visibility, stop and auto daily-exam answering');
