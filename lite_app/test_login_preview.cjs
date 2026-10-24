const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const script = fs.readFileSync('login-preview.js', 'utf8');
const png = 'data:image/png;base64,synthetic';
function image(extra = {}) {
  return {
    tagName: 'CANVAS', width: 240, height: 240, id: 'qrcode', className: '', alt: '',
    getBoundingClientRect: () => ({width: 240, height: 240}),
    getClientRects: () => [{}], getAttribute: () => '',
    closest: () => null, toDataURL: () => png, ...extra,
  };
}
function run(elements, {host = 'pc.xuexi.cn', text = '', captcha = false} = {}) {
  return vm.runInNewContext(script, {
    location: {protocol: 'https:', hostname: host},
    document: {
      body: {innerText: text},
      querySelectorAll: s => s === 'canvas,img' ? elements : captcha ? [image()] : [],
      createElement: () => ({getContext: () => ({drawImage() { throw Error('tainted'); }})}),
    },
  });
}
assert.equal(run([image()]).png, png);
assert.equal(run([image()], {host: 'xuexi.cn.evil.test'}), null);
assert.equal(run([image({getClientRects: () => []})]), null, 'ignore CSS-hidden fallback image');
assert.equal(run([image(), image()]), null, 'ambiguous images are not shown');
assert.equal(run([image()], {text: '二维码已过期'}).expired, true);
assert.equal(run([image()], {captcha: true}).verification, true);
assert.equal(run([image({tagName: 'IMG', complete: true, naturalWidth: 240, naturalHeight: 240,
  currentSrc: 'https://login.xuexi.cn/qr.png'})]).source, 'https://login.xuexi.cn/qr.png');
assert.equal(run([image({id: 'logo', width: 80, height: 80,
  getBoundingClientRect: () => ({width: 80, height: 80})})]), null);
console.log('PASS: QR extraction, official host boundary, expired/captcha, hidden fallback, ambiguity and cross-origin response fallback');
