(() => {
  'use strict';
  if (window.top !== window || !/(^|\.)xuexi\.cn$/i.test(location.hostname)) return;
  if (window.StudyLite) return;
  const state = {active: false, status: '等待每日答题'};
  let scrollEvents = 0;
  let mediaScrollAt = 0;
  document.addEventListener('scroll', () => { scrollEvents++; }, {passive: true});
  const visible = e => !!e && e.getClientRects().length > 0;
  const all = s => Array.from(document.querySelectorAll(s)).filter(visible);
  const stop = message => { state.active = false; state.status = message; };
  function tick() {
    if (!state.active) return;
    if (all('.nc-container,.nc_wrapper,iframe[src*="captcha"],.captcha').length) {
      state.status = '请在答题窗口手动完成验证'; return;
    }
    const stem = all('.q-body').length > 0;
    state.status = stem ? '请在答题窗口手动作答并提交' : '等待每日答题页面';
  }

  // ------------------------------------------------------------------
  // 每日答题自动作答：拦截取题响应（接口本身携带正确答案），按 DOM 结构作答并提交。
  // 不读取外部题库、不调用第三方搜索、不修改页面可见性，只驱动官方页面自身的控件。
  // ------------------------------------------------------------------
  let examPayload = null;   // {uniqueId, questions:[...]}
  let examStep = 0;         // 当前题目序号
  let examMode = 'wait';    // wait | answer | commit | submit | done | error | stopped
  let examStatus = '等待每日答题';
  let examSeenUrls = [];    // 去重的端点路径，最多 8 条
  let examDecodeError = null; // 最近一次解析失败的原因
  let examPending = null;   // 多选题逐项点击状态 {forStep, forSeq, targets, next, at}
  let examActionAt = 0;
  let examSubmissionObserved = false;
  let examSubmissionStarted = false;
  let examNextRequested = false;

  function examEndpoint(url, path) {
    try {
      const parsed = new URL(url, location.href);
      return parsed.protocol === 'https:' && parsed.hostname === 'pc-proxy-api.xuexi.cn'
        && !parsed.port && !parsed.username && !parsed.password && parsed.pathname === path;
    } catch (_) { return false; }
  }
  function examQuestionRequest(url) {
    return examEndpoint(url, '/api/exam/service/common/deduplicateRandomSearchV3')
      && new URL(url, location.href).searchParams.get('activityCode') === 'QUIZ_ALL';
  }
  const examSubmitRequest = url => examEndpoint(url, '/api/exam/service/practice/quizSubmitV3');

  function examDecode(envelope) {
    if (!envelope || typeof envelope !== 'object') { examDecodeError = '响应不是对象'; return false; }
    if (envelope.ok !== true || Number(envelope.code) !== 200) { examDecodeError = '平台未接受取题请求'; return false; }
    let inner = envelope.data_str;
    if (typeof inner === 'string') {
      if (inner.length > 2000000) { examDecodeError = '取题响应过大'; return false; }
      try { inner = JSON.parse(inner); }
      catch (_) {
        try { inner = JSON.parse(decodeURIComponent(escape(atob(inner)))); }
        catch (__) { examDecodeError = 'data_str 不是 JSON 也不是 base64'; return false; }
      }
    }
    if (!inner || !Array.isArray(inner.questions) || inner.questions.length === 0) { examDecodeError = 'questions 缺失或为空'; return false; }
    if (typeof inner.uniqueId !== 'string' || inner.uniqueId.length === 0) { examDecodeError = 'uniqueId 缺失'; return false; }
    for (const q of inner.questions) {
      if (!q || !Array.isArray(q.answers) || !q.answers.length || !Array.isArray(q.correct) || !q.correct.length) {
        examDecodeError = '题目缺少 answers/correct'; return false;
      }
    }
    if (examPayload && examPayload.uniqueId === inner.uniqueId) return true;
    examPayload = {uniqueId: inner.uniqueId, questions: inner.questions};
    examStep = 0;
    examPending = null;
    examActionAt = 0;
    examSubmissionObserved = false;
    examSubmissionStarted = false;
    examNextRequested = false;
    examMode = 'answer';
    examStatus = '已获取 ' + inner.questions.length + ' 道题，自动作答中';
    examDecodeError = null;
    return true;
  }

  function examRemember(url, ok) {
    try {
      let path = '';
      try { path = new URL(url, location.href).pathname; } catch (_) {}
      if (path && examSeenUrls.indexOf(path) < 0) {
        examSeenUrls.push(path);
        if (examSeenUrls.length > 8) examSeenUrls.shift();
      }
      if (!ok && examDecodeError == null) examDecodeError = '取题响应解析失败';
    } catch (_) {}
  }

  function examHookNetwork() {
    const open = XMLHttpRequest.prototype.open;
    const send = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (method, url) {
      try {
        this.__slExam = {method: String(method).toUpperCase(), url: String(url)};
        this.__slExamHooked = false;
      } catch (_) {}
      return open.apply(this, arguments);
    };
    XMLHttpRequest.prototype.send = function () {
      const meta = this.__slExam;
      try {
        if (meta && meta.method === 'POST' && examSubmitRequest(meta.url)) {
          examSubmissionStarted = true;
          this.addEventListener('load', () => { if (this.status === 200) examSubmissionObserved = true; }, {once: true});
        }
        if (meta && meta.method === 'GET' && examQuestionRequest(meta.url) && !this.__slExamHooked) {
          this.__slExamHooked = true;
          this.addEventListener('load', () => {
            try {
              if (this.status !== 200) return;
              let parsed = null;
              try { parsed = this.responseType === 'json' ? this.response : JSON.parse(this.responseText); } catch (_) {}
              if (parsed) { const ok = examDecode(parsed); examRemember(meta.url, ok); }
              else examRemember(meta.url, false);
            } catch (_) {}
          });
        }
      } catch (_) {}
      return send.apply(this, arguments);
    };
    const nativeFetch = window.fetch;
    if (typeof nativeFetch === 'function') {
      window.fetch = function (input, init) {
        const url = typeof input === 'string' ? input : (input && input.url) || '';
        const method = ((init && init.method) || (input && input.method) || 'GET');
        const promise = nativeFetch.apply(this, arguments);
        try {
          if (String(method).toUpperCase() === 'POST' && examSubmitRequest(url)) {
            examSubmissionStarted = true;
            promise.then(response => {
              if (response && response.ok) examSubmissionObserved = true;
              return response;
            }).catch(() => {});
          }
          if (String(method).toUpperCase() === 'GET' && examQuestionRequest(url)) {
            promise.then(response => {
              if (!response || !response.ok) return response;
              response.clone().text().then(text => {
                let parsed = null;
                try { parsed = JSON.parse(text); } catch (_) {}
                if (parsed) { const ok = examDecode(parsed); examRemember(url, ok); }
                else examRemember(url, false);
              }).catch(() => {});
              return response;
            }).catch(() => {});
          }
        } catch (_) {}
        return promise;
      };
    }
  }

  const examText = el => (el && el.textContent || '').replace(/\s+/g, '');
  const examClick = el => { try { el.click(); } catch (_) {} };
  const examOptions = () => all('.q-answers .q-answer');
  const examFillInputs = () => Array.from(document.querySelectorAll('input.blank')).filter(visible);
  const examFillSpans = () => Array.from(document.querySelectorAll('.q-answers .fill-answer')).filter(visible);
  const examCaptcha = () => all('.nc-container,.nc_wrapper,iframe[src*="captcha"],.captcha').length > 0;

  function examRenderedStep() {
    const pager = document.querySelector('.header-row .pager .big');
    const shown = Number(examText(pager));
    const total = examText(document.querySelector('.header-row .pager')).match(/^\d+\/(\d+)$/);
    if (!total || Number(total[1]) !== examPayload.questions.length) return null;
    if (!Number.isInteger(shown) || shown < 1 || !examPayload || shown > examPayload.questions.length) return null;
    return shown - 1;
  }

  function examAnswerCurrent() {
    const q = examPayload.questions[examStep];
    const answers = q.answers || [];
    const correct = q.correct || [];
    // 填空题：页面以 <input class="blank"> 渲染填空位，按题目顺序填入正确答案。
    const inputs = examFillInputs();
    if (inputs.length > 0) {
      if (inputs.length !== correct.length)
        return {mode: 'answer', status: '填空位数量与答案不一致（' + inputs.length + '/' + correct.length + '），已停止'};
      let filled = 0;
      const entries = answers.map(a => correct.find(c => String(c.answerId) === String(a.answerId)));
      if (entries.length !== inputs.length || entries.some(c => !c))
        return {mode: 'error', status: '填空答案与输入位置不匹配，已停止'};
      const values = entries.map(c => String(c.value == null ? '' : c.value));
      inputs.forEach((input, i) => {
        const value = values[i] || '';
        if ((input.value || '') !== value) {
          try {
            const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
            setter.call(input, value);
          } catch (_) { input.value = value; }
          input.dispatchEvent(new Event('input', {bubbles: true}));
          input.dispatchEvent(new Event('change', {bubbles: true}));
        }
        filled++;
      });
      const matched = inputs.every((input, i) => (input.value || '') === (values[i] || ''));
      if (!matched) return {mode: 'answer', status: '填空答案未能写入全部输入框'};
      examMode = 'commit';
      return {mode: 'commit', status: '填空题已填写 ' + filled + ' 个空，准备提交'};
    }
    // 填空题（选择词填空）：候选词以 .fill-answer 渲染，点击能组成正确答案的候选。
    const spans = examFillSpans();
    if (spans.length > 0) {
      const entry = correct[0];
      const target = entry ? String(entry.value).replace(/\s+/g, '') : '';
      let built = '';
      for (const span of spans) {
        const t = examText(span);
        if (!t || span.classList.contains('fill-answer-click')) continue;
        if (built.length < target.length && target.indexOf(t, built.length) === built.length) {
          examClick(span);
          built += t;
        }
      }
      if (built !== target) return {mode: 'answer', manual: true, status: '填空候选匹配不完整，请手动完成本题'};
      examMode = 'commit';
      return {mode: 'commit', status: '填空题候选已按序点选，准备提交'};
    }
    // 逐字填空（click-box）暂不支持自动输入，需人工完成本题。
    if (document.querySelector('.click-box'))
      return {mode: 'answer', manual: true, status: '遇到逐字填空题型，请在答题窗口手动完成本题'};
    // 选择题：按 correctAnswerSeq（字符串或数组，官方以 label 顺序拼接校验）或
    // correct 的 answerId 顺序，逐项点击（每 tick 至多处理一个选项），
    // 避免 React 异步渲染导致的重复点击（点击已选项=取消）与误判。
    const options = examOptions();
    if (options.length === 0) return {mode: 'answer', status: '未识别到选项或填空，等待渲染'};
    if (options.length !== answers.length) return {mode: 'answer', status: '等待选项完整渲染'};
    const matchSeq = label => {
      let i = answers.findIndex(a => String(a.label).trim() === label.trim());
      if (i < 0) i = answers.findIndex(a => String(a.content || '').trim() === label.trim());
      return i;
    };
    const matchById = c => {
      let i = answers.findIndex(a => a.answerId === c.answerId);
      if (i < 0) {
        const v = String(c.value == null ? '' : c.value).trim();
        if (v) {
          i = answers.findIndex(a => String(a.content || '').trim() === v);
          if (i < 0) i = answers.findIndex(a => String(a.label || '').trim() === v);
        }
      }
      return i;
    };
    let seq = null;
    if (typeof q.correctAnswerSeq === 'string' && q.correctAnswerSeq.length > 0)
      seq = q.correctAnswerSeq.split('');
    else if (Array.isArray(q.correctAnswerSeq) && q.correctAnswerSeq.length > 0)
      seq = q.correctAnswerSeq.map(String);
    let targets = [];
    if (seq) {
      targets = seq.map(matchSeq);
      if (targets.some(i => i < 0)) return {mode: 'answer', manual: true, status: '答案顺序与选项无法匹配，请手动完成本题'};
    } else {
      targets = correct.map(matchById);
      if (targets.some(i => i < 0)) return {mode: 'answer', manual: true, status: '答案与选项无法匹配，请手动完成本题'};
    }
    const unwanted = options.find((el, i) => el.classList.contains('chosen') && targets.indexOf(i) < 0);
    if (unwanted) { examClick(unwanted); examPending = null; return {mode: 'answer', status: '正在校正已选选项'}; }
    const seqKey = seq ? seq.join('') : String(correct.map(c => c.answerId).join(','));
    if (!examPending || examPending.forStep !== examStep || examPending.forSeq !== seqKey) {
      examPending = {forStep: examStep, forSeq: seqKey, targets: targets.slice(), next: 0, at: Date.now()};
    }
    while (examPending.next < examPending.targets.length) {
      const i = examPending.targets[examPending.next];
      const el = options[i];
      if (!el) return {mode: 'answer', status: '选项元素缺失'};
      if (el.classList.contains('chosen')) { examPending.next++; continue; }
      examClick(el);
      examPending.next++;
      return {mode: 'answer', status: '已点选第 ' + examPending.next + '/' + examPending.targets.length + ' 个选项'};
    }
    if (!examPending.targets.every(i => {
      const el = options[i];
      return el && el.classList.contains('chosen');
    })) {
      if (Date.now() - examPending.at > 8000)
        return {mode: 'answer', manual: true, status: '多选题选项点击未被页面接受，请在答题窗口手动完成本题'};
      return {mode: 'answer', status: '等待选项选中状态渲染'};
    }
    examMode = 'commit';
    examPending = null;
    return {mode: 'commit', status: '已选择答案，准备提交'};
  }

  function runExamAuto() {
    if (!state.active) { examMode = 'stopped'; return {mode: 'stopped', status: '已停止'}; }
    if (examCaptcha()) return {mode: 'captcha', status: '请在答题窗口手动完成验证'};
    if (examPayload == null) return {mode: 'wait', status: '等待每日答题题目数据'};
    // The host independently validates the response body and server credit.
    if (examSubmissionObserved) {
      examStep = examPayload.questions.length;
      return {mode: 'done', status: '官方答题提交请求已返回，等待平台积分确认'};
    }
    if (examMode === 'submit' || examSubmissionStarted) return {mode: 'submit', status: '等待官方答题提交响应'};
    const renderedStep = examRenderedStep();
    if (renderedStep === null) return {mode: 'answer', status: '等待可核对的答题页题号'};
    if (renderedStep !== null && renderedStep !== examStep) {
      examStep = renderedStep;
      examPending = null;
      examActionAt = 0;
      examNextRequested = false;
      examMode = 'answer';
      examStatus = '已同步到第 ' + (examStep + 1) + ' 题';
    }
    if (examStep >= examPayload.questions.length)
      return examSubmissionObserved
        ? {mode: 'done', status: '官方答题提交请求已返回，等待平台积分确认'}
        : {mode: 'submit', status: '等待官方答题提交响应'};
    try {
      const btn = document.querySelector('.action-row .next-btn');
      if (!btn) return {mode: 'answer', status: '等待答题页渲染'};
      const label = examText(btn);
      if (examMode === 'commit') {
        if (label !== '确定') { examMode = 'advance'; examActionAt = Date.now(); return {mode: 'advance', status: '页面已处理本题，等待题号更新'}; }
        if (btn.disabled === true) return {mode: 'commit', status: '提交按钮未就绪'};
        examClick(btn);
        examMode = 'advance';
        examActionAt = Date.now();
        return {mode: 'advance', status: '已提交本题，等待页面响应'};
      }
      if (examMode === 'advance' && label === '确定')
        return {mode: 'advance', status: '等待本题提交结果'};
      if (label === '确定') return examAnswerCurrent();
      if (label === '下一题') {
        if (examNextRequested) return {mode: 'advance', status: '已请求下一题，等待题号更新'};
        if (examMode === 'advance' && Date.now() - examActionAt < 2500)
          return {mode: 'advance', status: '等待页面自动跳转'};
        examClick(btn);
        examActionAt = Date.now();
        examNextRequested = true;
        examMode = 'advance';
        return {mode: 'advance', status: '已请求下一题，等待题号更新'};
      }
      if (label === '完成') {
        if (examMode === 'advance' && Date.now() - examActionAt < 2500)
          return {mode: 'advance', status: '等待官方末题提交'};
        if (!examSubmissionObserved) {
          examClick(btn);
          examActionAt = Date.now();
        }
        examMode = 'submit';
        return examSubmissionObserved
          ? {mode: 'done', status: '已观察到官方答题提交请求'}
          : {mode: 'submit', status: '已请求提交，等待官方响应'};
      }
      // 提交后按钮短暂停留在“确定”或在处理中，等待其变化。
      if (examMode === 'advance') return {mode: 'advance', status: '等待本题提交结果'};
      return {mode: 'answer', status: '按钮状态未知：' + label};
    } catch (_) {
      examMode = 'error';
      examStatus = '答题页结构异常，已停止';
      return {mode: 'error', status: '答题页结构异常，已停止'};
    }
  }

  function makeExamSnapshot() {
    let q = null;
    if (examPayload && examPayload.questions[examStep]) {
      const cur = examPayload.questions[examStep];
      try {
        q = {
          typeName: ['单选题', '多选题', '填空题', '判断题'].includes(cur.typeName) ? cur.typeName : '其他',
          answerInputType: Number(cur.answerInputType) || 0,
          questionDisplay: Number(cur.questionDisplay) || 0,
          answerCount: (cur.answers || []).length,
          correctCount: (cur.correct || []).length
        };
      } catch (_) { q = null; }
    }
    return {
      active: state.active,
      mode: examMode,
      step: examStep,
      count: examPayload ? examPayload.questions.length : 0,
      status: examStatus,
      q
    };
  }

  function makeExamDebug() {
    let dom = {err: true};
    try {
      dom = {
        qBodies: document.querySelectorAll('.q-body').length,
        qAnswers: document.querySelectorAll('.q-answers .q-answer').length,
        blanks: document.querySelectorAll('input.blank').length,
        clickBoxes: document.querySelectorAll('.click-box').length,
        actionRows: document.querySelectorAll('.action-row').length,
        nextBtns: document.querySelectorAll('.next-btn').length,
        captcha: document.querySelectorAll('.nc-container,.nc_wrapper,iframe[src*="captcha"],.captcha').length
      };
    } catch (_) {}
    let button = null;
    try { const b = document.querySelector('.action-row .next-btn'); button = b ? String(b.textContent) : null; } catch (_) {}
    return {
      captured: !!examPayload,
      payloadCount: examPayload ? examPayload.questions.length : 0,
      step: examStep,
      mode: examMode,
      status: examStatus,
      decodeError: examDecodeError,
      seenUrls: examSeenUrls.slice(),
      button,
      dom,
      href: location.pathname,
      submissionObserved: examSubmissionObserved
    };
  }

  examHookNetwork();

  window.StudyLite = {
    start() { state.active = true; state.status = '每日答题已启动'; return this.snapshot(); },
    stop() { stop('已停止'); examMode = 'stopped'; examPending = null; examStatus = '已停止'; return this.snapshot(); },
    snapshot() { return {active: state.active, status: state.status}; },
    tick() { try { tick(); } catch (_) { stop('页面结构变化，已停止'); } return this.snapshot(); },
    guarded() { return all('.nc-container,.nc_wrapper,iframe[src*="captcha"],.captcha').length > 0; },
    media(play) {
      const videos = all('video');
      if (play) for (const v of videos) if (v.paused && !v.ended) v.play().catch(() => {});
      return videos.map(v => ({source: v.currentSrc || '', time: v.currentTime,
        duration: Number.isFinite(v.duration) ? v.duration : 0, paused: v.paused, ended: v.ended,
        hidden: document.hidden === true, playbackRate: v.playbackRate,
        readyState: v.readyState, errorCode: v.error ? v.error.code : 0}));
    },
    videoPage(interact) {
      const videos = all('video');
      const player = videos[0] || all('iframe').find(frame => {
        try { return /(^|\.)xuexi\.cn$/i.test(new URL(frame.src, location.href).hostname); }
        catch (_) { return false; }
      });
      // Let the browser generate native scroll events; never dispatch synthetic events.
      if (interact && player && Date.now() - mediaScrollAt >= 5000) {
        const box = player.getBoundingClientRect();
        if (box.bottom <= 0 || box.top >= window.innerHeight)
          player.scrollIntoView({block: 'center', behavior: 'smooth'});
        else
          window.scrollBy({top: window.scrollY >= 70 ? -35 : 35, behavior: 'smooth'});
        mediaScrollAt = Date.now();
      }
      return {hidden: document.hidden === true, scrollEvents, playerFound: !!player};
    },
    article() {
      const body = document.body;
      const bad = /noContent\.html/i.test(location.pathname) ||
        /(内容不存在|内容已删除|内容已下架|访问异常|您无权限浏览该页面)/.test((body && body.innerText || '').slice(0, 400));
      const content = document.querySelector('.render-detail-article-content, .article-content, [itemprop="articleBody"], article');
      const ready = !!content && visible(content) && !bad && (content.innerText || '').replace(/\s/g, '').length > 100;
      if (ready) window.scrollBy({top: 70, behavior: 'smooth'});
      return {ready, bad, hidden: document.hidden === true, scrollEvents};
    },
    pauseMedia() { for (const v of all('video,audio')) v.pause(); },
    examAuto() {
      try {
        const r = runExamAuto();
        if (r.mode === 'done' || r.mode === 'error') examMode = r.mode;
        examStatus = r.status;
        const s = makeExamSnapshot();
        r.step = s.step; r.count = s.count; r.q = s.q;
        return r;
      } catch (_) { return {mode: 'error', status: '答题页结构异常，已停止'}; }
    },
    examSnapshot() { return makeExamSnapshot(); },
    examDebug() { return makeExamDebug(); }
  };
})();
