'use strict';
(() => {
  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => [...root.querySelectorAll(s)];
  const icons = {
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 1v2m0 18v2M1 12h2m18 0h2M4.2 4.2l1.4 1.4m12.8 12.8 1.4 1.4m0-15.6-1.4 1.4M5.6 18.4l-1.4 1.4"/>',
    monitor: '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8m-6-4v4m4-4v4"/>',
    layout: '<rect x="2.5" y="3" width="19" height="18" rx="2"/><path d="M10 3v18m0-9h11"/>',
    keyboard: '<rect x="1.5" y="5" width="21" height="14" rx="3"/><path d="M5 9h1m3 0h1m3 0h1m3 0h1M5 12h1m3 0h1m3 0h1m3 0h1M7 15h10"/>',
    settings: '<path d="M12 2 9 3l-.5 2-2 .9-2-.5L2 9l1.5 1.5v3L2 15l2.5 3.6 2-.5 2 .9L9 21l3 1 3-1 .5-2 2-.9 2 .5L22 15l-1.5-1.5v-3L22 9l-2.5-3.6-2 .5-2-.9L15 3Z"/><circle cx="12" cy="12" r="3.5"/>',
    help: '<circle cx="12" cy="12" r="9.5"/><path d="M9.5 8.5a2.5 2.5 0 0 1 5 .5c0 2-2.5 2-2.5 4m0 3v.1"/>',
    link: '<path d="M9 7H7a5 5 0 0 0 0 10h2m6-10h2a5 5 0 0 1 0 10h-2M8 12h8"/>',
    refresh: '<path d="M20 4v6h-6M20 10a8 8 0 1 0-.5 7"/>'
  };
  $$('[data-icon]').forEach(e => { e.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true">' + icons[e.dataset.icon] + '</svg>'; });
  const host = window.chrome?.webview;
  const send = message => host?.postMessage(message);
  let state, currentPage = 'Brightness', requestedPage = 'Brightness', pageToken = 0, pageAnimations = [], toastTimer;
  const safeText = (element, value) => { if (element && element.textContent !== String(value ?? '')) element.textContent = value ?? ''; };
  const field = (name, value) => send({ action: 'field', name, value });
  function notify(message) {
    safeText($('#toast'), message); $('#toast').hidden = false;
    clearTimeout(toastTimer); toastTimer = setTimeout(() => { $('#toast').hidden = true; }, 5000);
  }
  function value(e, v) { if (document.activeElement !== e && !e.matches(':active')) e.value = v; }
  function range(e, v) { value(e, v); e.style.setProperty('--fill', (document.activeElement === e ? e.value : v) + '%'); }
  function animateNumber(e, target) {
    if (e._target === target) return;
    const from = Number(e._value ?? target); e._target = target;
    cancelAnimationFrame(e._frame);
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const start = performance.now();
    const frame = now => { const t = reduced ? 1 : Math.min(1, (now - start) / 190); e._value = from + (target - from) * (1 - Math.pow(1 - t, 3)); safeText(e, Math.round(e._value)); if (t < 1) e._frame = requestAnimationFrame(frame); };
    e._frame = requestAnimationFrame(frame);
  }
  const ns = 'http://www.w3.org/2000/svg';
  for (let i = 0; i <= 50; i++) {
    const theta = Math.PI + i / 50 * Math.PI;
    const tick = document.createElementNS(ns, 'line');
    tick.setAttribute('x1', 180 + Math.cos(theta) * 166); tick.setAttribute('y1', 180 + Math.sin(theta) * 166);
    tick.setAttribute('x2', 180 + Math.cos(theta) * (i % 5 === 0 ? 176 : 172)); tick.setAttribute('y2', 180 + Math.sin(theta) * (i % 5 === 0 ? 176 : 172));
    $('#gaugeTicks').append(tick);
  }
  $$('.gauge path').forEach(p => p.setAttribute('d', 'M30 180 A150 150 0 0 1 330 180'));
  function drawMaster(percent, enabled) {
    animateNumber($('#masterPercent'), percent); range($('#masterRange'), percent); value($('#masterNumber'), percent);
    $('#masterRange').disabled = $('#masterNumber').disabled = !enabled;
    $('#gaugeFill').style.strokeDasharray = percent + ' 100';
    const theta = Math.PI + percent / 100 * Math.PI;
    $('#gaugeThumb').setAttribute('cx', 180 + Math.cos(theta) * 150); $('#gaugeThumb').setAttribute('cy', 180 + Math.sin(theta) * 150);
    $$('#gaugeTicks line').forEach((line, i) => line.classList.toggle('lit', i <= percent / 2));
  }
  function bindRange(input, id) {
    input.addEventListener('input', () => {
      input.style.setProperty('--fill', input.value + '%');
      send({ action: 'brightness', id, value: Number(input.value) });
    });
    let remainder = 0;
    input.addEventListener('wheel', e => {
      if (input.disabled || e.ctrlKey || e.altKey || e.shiftKey || e.metaKey) return;
      e.preventDefault();
      const delta = e.deltaY * (e.deltaMode === 1 ? 40 : e.deltaMode === 2 ? 120 : 1);
      if (Math.sign(delta) !== Math.sign(remainder)) remainder = 0;
      remainder += delta;
      const ticks = Math.trunc(remainder / 100); remainder -= ticks * 100;
      if (ticks) send({ action: 'step', id, value: Math.max(-100, Math.min(100, -ticks * 2)) });
    }, { passive: false });
  }
  bindRange($('#masterRange'), 'master');
  $('#masterNumber').addEventListener('change', e => {
    const n = Number(e.target.value);
    if (!Number.isInteger(n) || n < 0 || n > 100 || e.target.value === '') { notify('밝기는 0~100 사이의 정수로 입력하세요.'); e.target.value = state.master.percent; return; }
    send({ action: 'brightness', id: 'master', value: n });
  });
  $('#masterNumber').addEventListener('keydown', e => { if (e.key === 'Enter') e.target.blur(); });
  const cards = new Map();
  function updateMonitors(monitors) {
    const container = $('#monitorCards');
    if (!monitors.length) { container.replaceChildren(Object.assign(document.createElement('article'), { className: 'card empty', textContent: '연결된 모니터가 없습니다.' })); cards.clear(); return; }
    $('.empty', container)?.remove();
    for (const [id, card] of cards) if (!monitors.some(m => m.id === id)) { card.remove(); cards.delete(id); }
    monitors.forEach((m, index) => {
      let card = cards.get(m.id);
      if (!card) {
        card = document.createElement('article'); card.className = 'card monitor-card';
        card.innerHTML = '<div class="monitor-heading"><div class="monitor-symbol"><div class="screen-icon"></div></div><div><h2 class="monitor-name"></h2><div class="monitor-model"></div></div><label class="monitor-linked" title="함께 조절 대상"><input type="checkbox" aria-label="함께 조절 대상"></label></div><div class="monitor-readout"><span class="ddc-label">DDC/CI</span><span class="monitor-percent"><span class="number"></span><small>%</small></span></div><div class="range-wrap"><input type="range" min="0" max="100" step="1"><div class="range-labels"><span>0</span><span>100</span></div></div><p class="monitor-error"></p>';
        $('input[type=checkbox]', card).addEventListener('change', e => send({ action: 'linked', id: m.id, value: e.target.checked }));
        bindRange($('input[type=range]', card), m.id); cards.set(m.id, card); container.append(card);
      }
      const number = m.primary ? 1 : monitors.filter(x => !x.primary).findIndex(x => x.id === m.id) + 2;
      const name = monitors.length === 2 ? (index === 0 ? '왼쪽 모니터' : '오른쪽 모니터') : '모니터 ' + number;
      safeText($('.monitor-name', card), name); safeText($('.screen-icon', card), number);
      $('.screen-icon', card).classList.toggle('portrait', m.height > m.width);
      safeText($('.monitor-model', card), m.name + ' · ' + (m.height > m.width ? '세로' : '가로'));
      $('input[type=checkbox]', card).checked = m.selected;
      $('input[type=checkbox]', card).setAttribute('aria-label', name + ' 함께 조절 대상');
      const slider = $('input[type=range]', card); slider.disabled = !m.supported; slider.setAttribute('aria-label', name + ' 밝기'); range(slider, m.percent);
      animateNumber($('.number', card), m.percent); safeText($('.monitor-error', card), m.status || (m.supported ? '' : 'DDC/CI 연결을 확인하세요.'));
      $('.monitor-error', card).hidden = !$('.monitor-error', card).textContent;
      safeText($('.ddc-label', card), m.supported ? 'DDC/CI' : '연결 확인');
    });
  }
  let displayKey = '';
  function updateDisplays(displays) {
    const key = JSON.stringify(displays);
    if (key === displayKey) return;
    displayKey = key;
    const map = $('#displayMap'), choices = $('#displayChoices'); map.replaceChildren(); choices.replaceChildren();
    if (!displays.length) { map.textContent = '연결된 모니터가 없습니다.'; return; }
    const left = Math.min(...displays.map(d => d.x)), top = Math.min(...displays.map(d => d.y));
    const width = Math.max(...displays.map(d => d.x + d.width)) - left, height = Math.max(...displays.map(d => d.y + d.height)) - top;
    const scale = Math.min((map.clientWidth - 100) / width, (map.clientHeight - 60) / height);
    displays.forEach(d => {
      const button = document.createElement('button'); button.className = d.selected ? 'selected' : ''; button.textContent = d.number; button.setAttribute('aria-label', '모니터 ' + d.number + (d.primary ? ' 기본' : '')); button.setAttribute('aria-pressed', d.selected);
      Object.assign(button.style, { left: (map.clientWidth - width * scale) / 2 + (d.x - left) * scale + 'px', top: (map.clientHeight - height * scale) / 2 + (d.y - top) * scale + 'px', width: Math.max(20, d.width * scale - 4) + 'px', height: Math.max(20, d.height * scale - 4) + 'px' });
      button.addEventListener('click', () => send({ action: 'display', id: d.id, value: !d.selected })); map.append(button);
      const choice = document.createElement('button'); choice.className = d.selected ? 'selected' : ''; choice.textContent = '모니터 ' + d.number; choice.setAttribute('aria-pressed', d.selected); choice.addEventListener('click', () => button.click()); choices.append(choice);
    });
  }
  new ResizeObserver(() => { if (state && !$('#Protection').hidden) { displayKey = ''; updateDisplays(state.displays); } }).observe($('#displayMap'));
  let ddcKey = '';
  function updateDdc(monitors) {
    const key = JSON.stringify(monitors.map(m => [m.id, m.capabilities, m.peakNits, m.fullFrameNits]));
    if (key === ddcKey) return; ddcKey = key;
    const root = $('#ddcDetails'); root.replaceChildren();
    monitors.forEach(m => { const detail = document.createElement('details'), title = document.createElement('summary'), p = document.createElement('p'); title.textContent = m.name + ' · ' + m.device; p.textContent = '피크 ' + (m.peakNits || '알 수 없음') + ' nit · 전체 화면 ' + (m.fullFrameNits || '알 수 없음') + ' nit'; detail.append(title, p); if (m.capabilities) { const pre = document.createElement('pre'); pre.textContent = m.capabilities; detail.append(pre); } root.append(detail); });
  }
  function applyState(next) {
    state = next; window.monitorShadeState = state;
    for (const [k, v] of Object.entries(state.palette)) if (k !== 'light') document.documentElement.style.setProperty('--' + k, v);
    document.documentElement.style.colorScheme = state.palette.light ? 'light' : 'dark';
    document.body.classList.toggle('solid', !state.glass); document.body.classList.toggle('windows-font', state.font === 'MalgunGothic'); document.body.classList.toggle('high-contrast', state.highContrast);
    safeText($('#themeTime'), state.time); safeText($('#version'), state.version + (state.preview ? ' · 미리보기' : ''));
    $$('[data-field]').forEach(e => {
      const setting = state.fields[e.id];
      if (setting === undefined) return;
      if (e.tagName === 'SELECT') {
        const key = JSON.stringify(setting.options);
        if (e.dataset.options !== key) { e.replaceChildren(...setting.options.map((s, i) => new Option(s, i))); e.dataset.options = key; }
        if (document.activeElement !== e) e.value = String(setting.value);
      } else if (e.type === 'checkbox' || e.type === 'radio') e.checked = setting;
      else value(e, setting);
    });
    $$('[data-message]').forEach(e => safeText(e, state.messages[e.dataset.message]));
    $$('[data-button]').forEach(e => { e.disabled = state.enabled[e.dataset.button] === false; });
    const maximize = $('[data-caption=maximize]'); maximize.setAttribute('aria-label', state.maximized ? '이전 크기로' : '최대화'); maximize.title = maximize.getAttribute('aria-label');
    const maximizeSvg = state.maximized ? '<path d="M5 3h8v8m-8-6h6v8H3V5Z"/>' : '<rect x="3.5" y="3.5" width="9" height="9" rx=".5"/>';
    if ($('svg', maximize).innerHTML !== maximizeSvg) $('svg', maximize).innerHTML = maximizeSvg;
    drawMaster(state.master.percent, state.master.enabled); updateMonitors(state.monitors);
    if (!$('#Protection').hidden) updateDisplays(state.displays);
    safeText($('#licenses'), state.licenses); updateDdc(state.monitors);
  }
  async function selectPage(page) {
    if (page === requestedPage || !document.getElementById(page)?.classList.contains('page')) return;
    requestedPage = page;
    const token = ++pageToken; pageAnimations.forEach(a => a.cancel()); pageAnimations = [];
    const previous = document.getElementById(currentPage), next = document.getElementById(page);
    $$('.nav').forEach(b => { const active = b.dataset.page === page; b.classList.toggle('active', active); active ? b.setAttribute('aria-current', 'page') : b.removeAttribute('aria-current'); });
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!reduced) { const fade = previous.animate([{ opacity: getComputedStyle(previous).opacity }, { opacity: 0 }], { duration: 100, easing: 'ease-out', fill: 'forwards' }); pageAnimations.push(fade); try { await fade.finished; } catch {} }
    if (token !== pageToken) return;
    $$('.page').forEach(p => { p.hidden = p !== next; p.classList.toggle('active', p === next); }); currentPage = page;
    $('#main').scrollTop = 0; pageAnimations.forEach(a => a.cancel()); pageAnimations = [];
    if (page === 'Protection' && state) { displayKey = ''; updateDisplays(state.displays); }
    if (!reduced) { const fade = next.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 190, easing: 'ease-out' }); pageAnimations.push(fade); try { await fade.finished; } catch {} }
    if (token === pageToken) { pageAnimations.forEach(a => a.cancel()); pageAnimations = []; send({ action: 'page', value: page }); }
  }
  window.monitorShadeNavigate = selectPage;
  $$('[data-page]').forEach(b => b.addEventListener('click', () => selectPage(b.dataset.page)));
  $$('[data-button]').forEach(b => b.addEventListener('click', () => send({ action: 'button', name: b.dataset.button })));
  $$('[data-caption]').forEach(b => b.addEventListener('click', () => send({ action: 'caption', value: b.dataset.caption })));
  // Native WebView2 app-region handles dragging/double-click/system menu without
  // asynchronous JavaScript mouse messages or a background helper window.
  $$('[data-field]').forEach(e => e.addEventListener('change', () => {
    if (e.tagName === 'SELECT') field(e.id, Number(e.value));
    else if (e.type === 'checkbox' || e.type === 'radio') field(e.id, e.checked);
    else field(e.id, e.value);
  }));
  window.addEventListener('error', e => { window.monitorShadeError = e.message; });
  if (host) { host.addEventListener('message', e => { if (e.data.state) applyState(e.data.state); else if (e.data.error) notify(e.data.error); }); send({ action: 'ready' }); }
})();
