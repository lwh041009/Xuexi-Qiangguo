(() => {
  if (location.protocol !== 'https:' ||
      !/(^|\.)(xuexi\.cn|dingtalk\.com)$/i.test(location.hostname)) return null;
  const visible = e => !!e && e.getClientRects().length > 0;
  if (Array.from(document.querySelectorAll('.nc-container,.nc_wrapper,.captcha,iframe[src*="captcha"]')).some(visible))
    return {verification: true};
  const text = (document.body && document.body.innerText || '').slice(0, 6000);
  if (/(二维码已过期|二维码已失效|二维码失效)/.test(text)) return {expired: true};
  let candidates = Array.from(document.querySelectorAll('canvas,img')).filter(e => {
    if (!visible(e)) return false;
    const r = e.getBoundingClientRect();
    const size = Math.max(r.width, e.width || e.naturalWidth || 0);
    const height = Math.max(r.height, e.height || e.naturalHeight || 0);
    return size >= 100 && size <= 1000 && height >= 100 && height <= 1000 &&
      Math.abs(size / height - 1) < 0.14 && (e.tagName !== 'IMG' || e.complete);
  });
  const hint = e => /qrcode|qr-code|qr_code|login-code|login_code|二维码/i.test(
    [e.id, e.className, e.alt, e.getAttribute('src'), e.parentElement && e.parentElement.className].join(' ')) ||
    !!e.closest('.qrcode-box,.qrcode,.qr-code,#qrcode,#qglogin,#ddlogin');
  const hinted = candidates.filter(hint);
  if (hinted.length) candidates = hinted;
  if (candidates.length !== 1) return null;
  const e = candidates[0];
  try {
    if (e.tagName === 'CANVAS') return {png: e.toDataURL('image/png')};
    const canvas = document.createElement('canvas');
    canvas.width = e.naturalWidth;
    canvas.height = e.naturalHeight;
    if (canvas.width < 100 || canvas.height < 100) return null;
    canvas.getContext('2d').drawImage(e, 0, 0);
    return {png: canvas.toDataURL('image/png')};
  } catch (_) {
    // A cross-origin image can be taken from its already-received browser response.
    return e.tagName === 'IMG' ? {source: e.currentSrc || e.src} : null;
  }
})();
