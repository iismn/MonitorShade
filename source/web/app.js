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
    refresh: '<path d="M20 4v6h-6M20 10a8 8 0 1 0-.5 7"/>',
    search: '<circle cx="10.5" cy="10.5" r="6.5"/><path d="m15.5 15.5 5 5"/>'
  };
  $$('[data-icon]').forEach(e => { e.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true">' + icons[e.dataset.icon] + '</svg>'; });
  const host = window.chrome?.webview;
  const send = message => host?.postMessage(message);
  let state, currentPage = 'Brightness', requestedPage = 'Brightness', pageToken = 0, pageAnimations = [], toastTimer;
  const safeText = (element, value) => { if (element && element.textContent !== String(value ?? '')) element.textContent = value ?? ''; };
  let language = 'KO', catalog = {}, fragments;
  const t = text => {
    text = String(text ?? '');
    if (language !== 'EN') return text;
    return Object.hasOwn(catalog, text) ? catalog[text] : text.replace(fragments, match => catalog[match]);
  };
  const localizedNodes = [], localizedAttributes = [];
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) if (/[가-힣]/.test(walker.currentNode.nodeValue)) localizedNodes.push([walker.currentNode, walker.currentNode.nodeValue]);
  $$('[aria-label],[placeholder],[title]').forEach(e => ['aria-label','placeholder','title'].forEach(a => {
    const text = e.getAttribute(a); if (text && /[가-힣]/.test(text)) localizedAttributes.push([e,a,text]);
  }));
  function localize(next) {
    const changed = language !== next.language || !fragments;
    language = next.language || 'KO';
    if (!fragments) {
      catalog = next.translations || {};
      const escape = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      fragments = new RegExp(Object.keys(catalog).filter(k=>k.length>1 && /[가-힣]/.test(k)).sort((a,b)=>b.length-a.length).map(escape).join('|'), 'g');
    }
    if (!changed) return;
    document.documentElement.lang = language === 'EN' ? 'en' : 'ko';
    localizedNodes.forEach(([node,text])=>{ node.nodeValue = t(text); });
    localizedAttributes.forEach(([e,a,text])=>e.setAttribute(a,t(text)));
    displayKey = ''; ddcKey = ''; updateSearch();
  }
  const field = (name, value) => send({ action: 'field', name, value });
  function notify(message) {
    safeText($('#toast'), t(message)); $('#toast').hidden = false;
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
    // A frame timestamp can predate a host message received during that frame.
    // Clamp both ends so a 0 → 100 change never produces a negative dot glyph.
    const frame = now => { const t = reduced ? 1 : Math.max(0, Math.min(1, (now - start) / 190)); e._value = from + (target - from) * (1 - Math.pow(1 - t, 3)); safeText(e, Math.round(e._value)); if (e.id === 'masterPercent') drawDotNumber(Math.round(e._value)); if (t < 1) e._frame = requestAnimationFrame(frame); };
    e._frame = requestAnimationFrame(frame);
  }
  const ns = 'http://www.w3.org/2000/svg';
  // Original 5×7 dot glyphs: no remote font or image, sharp at every Windows DPI.
  const dotGlyphs = {
    '0':['01110','10001','10001','10001','10001','10001','01110'],
    '1':['00100','01100','00100','00100','00100','00100','01110'],
    '2':['01110','10001','00001','00010','00100','01000','11111'],
    '3':['11110','00001','00001','01110','00001','00001','11110'],
    '4':['00010','00110','01010','10010','11111','00010','00010'],
    '5':['11111','10000','10000','11110','00001','00001','11110'],
    '6':['01110','10000','10000','11110','10001','10001','01110'],
    '7':['11111','00001','00010','00100','01000','01000','01000'],
    '8':['01110','10001','10001','01110','10001','10001','01110'],
    '9':['01110','10001','10001','01111','00001','00001','01110'],
    '%':['11001','11001','00010','00100','01000','10011','10011']
  };
  let lastDotValue;
  function drawDotNumber(percent) {
    if (lastDotValue === percent) return;
    lastDotValue = percent;
    const label = String(percent) + '%', svg = $('#dotBrightness'), group = document.createDocumentFragment();
    label.split('').forEach((digit, n) => dotGlyphs[digit].forEach((row, y) => [...row].forEach((pixel, x) => {
      if (pixel !== '1') return;
      const dot = document.createElementNS(ns, 'circle');
      dot.setAttribute('cx', 3 + n * 30 + x * 5); dot.setAttribute('cy', 3 + y * 5); dot.setAttribute('r', '1'); group.append(dot);
    })));
    svg.setAttribute('viewBox', '0 0 ' + (label.length * 30 - 4) + ' 36'); svg.replaceChildren(group);
    svg.dataset.value = String(percent);
  }
  function drawMaster(percent, enabled) {
    animateNumber($('#masterPercent'), percent); range($('#masterRange'), percent); value($('#masterNumber'), percent);
    $('#masterRange').disabled = $('#masterNumber').disabled = !enabled;
    $('#dotBrightness').classList.toggle('unavailable', !enabled);
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
    if (!monitors.length) { container.replaceChildren(Object.assign(document.createElement('article'), { className: 'card empty', textContent: t('연결된 모니터가 없습니다.') })); cards.clear(); return; }
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
      safeText($('.monitor-name', card), t(name)); safeText($('.screen-icon', card), number);
      $('.screen-icon', card).classList.toggle('portrait', m.height > m.width);
      safeText($('.monitor-model', card), m.name + ' · ' + t(m.height > m.width ? '세로' : '가로'));
      $('.monitor-linked', card).title = t('함께 조절 대상');
      $('input[type=checkbox]', card).checked = m.selected;
      $('input[type=checkbox]', card).setAttribute('aria-label', t(name) + ' · ' + t('함께 조절 대상'));
      const slider = $('input[type=range]', card); slider.disabled = !m.supported; slider.setAttribute('aria-label', t(name) + ' · ' + t('밝기')); range(slider, m.percent);
      animateNumber($('.number', card), m.percent); safeText($('.monitor-error', card), t(m.status || (m.supported ? '' : 'DDC/CI 연결을 확인하세요.')));
      $('.monitor-error', card).hidden = !$('.monitor-error', card).textContent;
      safeText($('.ddc-label', card), m.supported ? 'DDC/CI' : t('연결 확인'));
    });
  }
  let displayKey = '';
  function updateDisplays(displays) {
    const key = JSON.stringify(displays);
    if (key === displayKey) return;
    displayKey = key;
    const map = $('#displayMap'), choices = $('#displayChoices'); map.replaceChildren(); choices.replaceChildren();
    if (!displays.length) { map.textContent = t('연결된 모니터가 없습니다.'); return; }
    const left = Math.min(...displays.map(d => d.x)), top = Math.min(...displays.map(d => d.y));
    const width = Math.max(...displays.map(d => d.x + d.width)) - left, height = Math.max(...displays.map(d => d.y + d.height)) - top;
    const scale = Math.min((map.clientWidth - 100) / width, (map.clientHeight - 60) / height);
    displays.forEach(d => {
      const button = document.createElement('button'); button.className = d.selected ? 'selected' : ''; button.textContent = d.number; button.setAttribute('aria-label', t('모니터') + ' ' + d.number + (d.primary ? t(' 기본') : '')); button.setAttribute('aria-pressed', d.selected);
      Object.assign(button.style, { left: (map.clientWidth - width * scale) / 2 + (d.x - left) * scale + 'px', top: (map.clientHeight - height * scale) / 2 + (d.y - top) * scale + 'px', width: Math.max(20, d.width * scale - 4) + 'px', height: Math.max(20, d.height * scale - 4) + 'px' });
      button.addEventListener('click', () => send({ action: 'display', id: d.id, value: !d.selected })); map.append(button);
      const choice = document.createElement('button'); choice.className = d.selected ? 'selected' : ''; choice.textContent = t('모니터') + ' ' + d.number; choice.setAttribute('aria-pressed', d.selected); choice.addEventListener('click', () => button.click()); choices.append(choice);
    });
  }
  new ResizeObserver(() => { if (state && !$('#Protection').hidden) { displayKey = ''; updateDisplays(state.displays); } }).observe($('#displayMap'));
  let ddcKey = '';
  function updateDdc(monitors) {
    const key = JSON.stringify(monitors.map(m => [m.id, m.capabilities, m.peakNits, m.fullFrameNits]));
    if (key === ddcKey) return; ddcKey = key;
    const root = $('#ddcDetails'); root.replaceChildren();
    monitors.forEach(m => { const detail = document.createElement('details'), title = document.createElement('summary'), p = document.createElement('p'); title.textContent = m.name + ' · ' + m.device; p.textContent = t('피크 ') + (m.peakNits || t('알 수 없음')) + t(' nit · 전체 화면 ') + (m.fullFrameNits || t('알 수 없음')) + ' nit'; detail.append(title, p); if (m.capabilities) { const pre = document.createElement('pre'); pre.textContent = m.capabilities; detail.append(pre); } root.append(detail); });
  }
  const layoutActions = { Left:'왼쪽', Up:'위쪽', Right:'오른쪽', Down:'아래쪽', Center:'중앙 배치', Scale:'크기 순환' };
  const modifierName = mask => [[2,'Ctrl'],[1,'Alt'],[4,'Shift'],[8,'Win']].filter(([bit])=>mask & bit).map(([,name])=>name).join(' + ');
  const keyName = key => ({8:'Backspace',33:'Page Up',34:'Page Down',35:'End',36:'Home',37:'←',38:'↑',39:'→',40:'↓',45:'Insert',46:'Delete'}[key] || (key>=112 ? 'F'+(key-111) : String.fromCharCode(key)));
  const keyOptions = [8,33,34,35,36,37,38,39,40,45,46,...Array.from({length:10},(_,i)=>48+i),...Array.from({length:26},(_,i)=>65+i),...Array.from({length:12},(_,i)=>112+i)];
  const shortcutLabel = binding => modifierName(binding.modifiers) + ' + ' + keyName(binding.key);
  let layoutEditorLanguage;
  function updateLayoutShortcuts(next) {
    if (!next.layoutShortcuts) return;
    const root = $('#layoutShortcutRows');
    if (layoutEditorLanguage !== language) {
      layoutEditorLanguage = language; root.replaceChildren();
      Object.entries(layoutActions).forEach(([action,title]) => {
        const row=document.createElement('div'); row.className='layout-shortcut-row'; row.dataset.action=action;
        const label=document.createElement('span');label.textContent=t(title);
        const modifiers=document.createElement('select'), key=document.createElement('select');
        modifiers.id='layoutModifiers'+action;key.id='layoutKey'+action;
        modifiers.setAttribute('aria-label', t(title)+' · '+t('보조키 선택'));key.setAttribute('aria-label',t(title)+' · '+t('키 선택'));
        modifiers.append(...[8,2,1,6,3,5,9,10,12,7,11,13,14,15].map(v=>new Option(modifierName(v),v)));
        key.append(...keyOptions.map(v=>new Option(keyName(v),v)));
        const change=()=>send({action:'layoutShortcut',name:action,modifiers:Number(modifiers.value),key:Number(key.value)});
        modifiers.addEventListener('change',change);key.addEventListener('change',change);
        row.append(label,modifiers,key);root.append(row);
      });
    }
    next.layoutShortcuts.forEach(b=>{value($('#layoutModifiers'+b.action),b.modifiers);value($('#layoutKey'+b.action),b.key);});
    const applied=next.appliedShortcuts;
    if (applied) {
      const directions=applied.filter(b=>['Left','Up','Right','Down'].includes(b.action));
      safeText($('#directionKeys'), directions.every(b=>b.modifiers===8 && b.key==={Left:37,Up:38,Right:39,Down:40}[b.action]) ? 'Win + ↑ ↓ ← →' : directions.map(shortcutLabel).join(' · '));
      safeText($('#scaleKeys'),shortcutLabel(applied.find(b=>b.action==='Scale')));
      safeText($('#centerKeys'),shortcutLabel(applied.find(b=>b.action==='Center'))+' · 16:9');
    }
  }
  $('#resetLayoutShortcuts').addEventListener('click',()=>send({action:'resetLayoutShortcuts'}));
  function applyState(next) {
    state = next; window.monitorShadeState = state; localize(state); updateLayoutShortcuts(state);
    for (const [k, v] of Object.entries(state.palette)) if (k !== 'light') document.documentElement.style.setProperty('--' + k, v);
    document.documentElement.style.colorScheme = state.palette.light ? 'light' : 'dark';
    document.body.classList.toggle('light', state.palette.light);
    document.body.classList.toggle('solid', !state.glass); document.body.classList.toggle('windows-font', state.font === 'MalgunGothic'); document.body.classList.toggle('high-contrast', state.highContrast);
    safeText($('#themeTime'), t(state.time)); safeText($('#version'), state.version + (state.preview ? t(' · 미리보기') : ''));
    $$('[data-field]').forEach(e => {
      const setting = state.fields[e.id];
      if (setting === undefined) return;
      if (e.tagName === 'SELECT') {
        const key = language + JSON.stringify(setting.options);
        const replaced = e.dataset.options !== key;
        if (replaced) { e.replaceChildren(...setting.options.map((s, i) => new Option(t(s), i))); e.dataset.options = key; }
        if (replaced || document.activeElement !== e) e.value = String(setting.value);
      } else if (e.type === 'checkbox' || e.type === 'radio') e.checked = setting;
      else value(e, setting);
    });
    $$('[data-message]').forEach(e => safeText(e, t(state.messages[e.dataset.message])));
    $$('[data-button]').forEach(e => { e.disabled = state.enabled[e.dataset.button] === false; });
    const maximize = $('[data-caption=maximize]'); maximize.setAttribute('aria-label', t(state.maximized ? '이전 크기로' : '최대화')); maximize.title = maximize.getAttribute('aria-label');
    const maximizeSvg = state.maximized ? '<path d="M5 3h8v8m-8-6h6v8H3V5Z"/>' : '<rect x="3.5" y="3.5" width="9" height="9" rx=".5"/>';
    if ($('svg', maximize).innerHTML !== maximizeSvg) $('svg', maximize).innerHTML = maximizeSvg;
    drawMaster(state.master.percent, state.master.enabled); updateMonitors(state.monitors);
    updateDisplays(state.displays);
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
  const searchIndex = [
    ['Brightness','밝기 조절','DDC CI 모니터 밝기 brightness','masterRange'],
    ['Brightness','밝기 동기화','함께 조절 동시 sync','BrightnessSync'],
    ['Protection','감시할 모니터','화면 보호 화면보호기 검은 화면 OLED 대상','displayChoices'],
    ['Protection','대기 시간','초 분 시간 타이머 delay','Amount'],
    ['Protection','움직이는 배경화면','Lively 배경 무시','IgnoreWallpaper'],
    ['Protection','강제 검은 화면','F9 F10 가리기','ManualEnabled'],
    ['Layout','창 배치','윈도우 방향키 위 아래 왼쪽 오른쪽 windows','LayoutTitle'],
    ['Layout','창 크기 조절','Win Backspace 백스페이스 크기 확장 50 70 100','resizeFeature'],
    ['Shortcuts','창 배치 단축키','크기 조절 키 조합 변경 modifiers keys bindings layout shortcuts','layoutShortcutEditor'],
    ['General','언어','language English Korean KO EN 한국어 영어','Language'],
    ['Layout','중앙 창 너비','Win Delete Del 16:9 중앙 비율','CenterPercent'],
    ['Shortcuts','밝기 단축키','키보드 Alt F2 F3 보조키','BrightnessModifier'],
    ['Shortcuts','밝기 조절 간격','증가 감소 단계 step','BrightnessStep'],
    ['General','시간별 색상','테마 아침 낮 저녁 밤 디자인 theme','AppearanceMode'],
    ['General','한글 글꼴','Pretendard 프리텐다드 맑은 고딕 폰트 font','KoreanFont'],
    ['General','창 뒤 배경 블러','투명 blur Acrylic 유리','UseBackdrop'],
    ['General','로그인 시 자동 실행','시작프로그램 트레이 startup','AutoStart'],
    ['Help','도움말','문제 진단 사용법 라이선스','HelpTitle']
  ];
  const pageNames = {Brightness:'밝기',Protection:'화면 보호',Layout:'창 배치',Shortcuts:'단축키',General:'일반',Help:'도움말'};
  const searchInput = $('#settingsSearch'), results = $('#searchResults');
  const normalizeSearch = text => text.toLocaleLowerCase().replace(/[\s+·/:-]/g, '');
  function clearSearch() { searchInput.value = ''; updateSearch(); }
  function updateSearch() {
    const query = normalizeSearch(searchInput.value.trim());
    results.replaceChildren(); results.hidden = !query; $('#navigation').hidden = !!query; $('#clearSearch').hidden = !searchInput.value;
    if (!query) { safeText($('#searchAnnouncement'), ''); return; }
    const matches = searchIndex.filter(item => normalizeSearch(item.slice(1,3).join(' ') + t(item[1]) + t(pageNames[item[0]])).includes(query));
    safeText($('#searchAnnouncement'), language === 'EN' ? matches.length + ' results' : '검색 결과 ' + matches.length + '개');
    if (!matches.length) { const empty = document.createElement('p'); empty.className = 'search-empty'; empty.textContent = t('검색 결과가 없습니다.'); results.append(empty); }
    matches.forEach(([page, title, keywords, id]) => {
      const button = document.createElement('button'), name = document.createElement('span'), section = document.createElement('small');
      button.className = 'search-result'; name.textContent = t(title); section.textContent = t(pageNames[page]); button.append(name,section);
      button.addEventListener('click', async () => {
        clearSearch(); await selectPage(page);
        const target = document.getElementById(id); if (!target) return;
        if (!target.matches('input,select,button,[tabindex]')) target.tabIndex = -1;
        target.scrollIntoView({block:'center',behavior:'instant'}); target.focus({preventScroll:true});
        target.classList.remove('search-highlight'); void target.offsetWidth; target.classList.add('search-highlight');
      }); results.append(button);
    });
  }
  searchInput.addEventListener('input', updateSearch);
  $('#clearSearch').addEventListener('click', () => { clearSearch(); searchInput.focus(); });
  searchInput.addEventListener('keydown', e => {
    if (e.key === 'Escape') { clearSearch(); e.preventDefault(); }
    if (e.key === 'ArrowDown') { $('button',results)?.focus(); e.preventDefault(); }
    if (e.key === 'Enter' && !e.isComposing) { $('button',results)?.click(); e.preventDefault(); }
  });
  results.addEventListener('keydown', e => {
    const buttons = $$('button',results), i = buttons.indexOf(document.activeElement);
    if (e.key === 'Escape') { clearSearch(); searchInput.focus(); e.preventDefault(); }
    if (e.key === 'ArrowDown') { buttons[Math.min(buttons.length-1,i+1)]?.focus(); e.preventDefault(); }
    if (e.key === 'ArrowUp') { (i > 0 ? buttons[i-1] : searchInput).focus(); e.preventDefault(); }
  });
  document.addEventListener('keydown', e => { if(e.ctrlKey && !e.altKey && e.key.toLowerCase()==='f'){searchInput.focus();searchInput.select();e.preventDefault();} });
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
