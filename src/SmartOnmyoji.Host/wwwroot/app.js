'use strict';

const $ = (id) => document.getElementById(id);

async function api(path, opts) {
  const res = await fetch(path, opts);
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) throw new Error(data?.error || res.statusText);
  return data;
}

function toast(msg, kind) {
  const t = $('toast');
  t.textContent = msg;
  t.className = 'toast ' + (kind || 'info');
  setTimeout(() => t.classList.add('hidden'), 3200);
}

// ---------- 目标集 ----------
async function loadTargets() {
  const sets = await api('/api/targets');
  const sel = $('targetSelect');
  const prev = sel.value;
  sel.innerHTML = '';
  for (const s of sets) {
    const o = document.createElement('option');
    o.value = s.name;
    o.textContent = `${s.name}（${s.imageCount} 图 · ${s.source}）`;
    sel.appendChild(o);
  }
  if (prev && sets.some((s) => s.name === prev)) sel.value = prev;
  if (sel.value) await onTargetChange();
}

async function onTargetChange() {
  const name = $('targetSelect').value;
  if (!name) return;
  const detail = await api(`/api/targets/${encodeURIComponent(name)}`);
  $('targetMeta').textContent = `来源 ${detail.source} · ${detail.images.length} 图`
    + (detail.warnings.length ? ` · ${detail.warnings.length} 条告警` : '');

  const thumbs = $('thumbs');
  thumbs.innerHTML = '';
  for (const img of detail.images) {
    const cell = document.createElement('div');
    cell.className = 'thumb flag-' + img.flag.toLowerCase();
    cell.title = `${img.file}\nflag=${img.flag} 优先级=${img.priority}${img.hasClick ? ' 偏移点击' : ''}`;
    const im = document.createElement('img');
    im.loading = 'lazy';
    im.src = `/api/targets/${encodeURIComponent(name)}/images/${encodeURIComponent(img.file)}`;
    const cap = document.createElement('span');
    cap.textContent = img.flag === 'Normal' ? img.name : `${img.name} · ${flagLabel(img.flag)}`;
    cell.appendChild(im);
    cell.appendChild(cap);
    thumbs.appendChild(cell);
  }
  for (const w of detail.warnings) appendLog('warning', '[目标集] ' + w);
}

function flagLabel(f) {
  return { RoundStart: '开局', Once: '单次', Skip: '跳过', Stop: '终止' }[f] || f;
}

// ---------- 窗口 ----------
let selectedHandles = new Set();
let allWindows = [];

async function loadWindows() {
  allWindows = await api('/api/windows');
  renderWindows();
}

function renderWindows() {
  const box = $('windowList');
  const f = ($('windowFilter').value || '').trim().toLowerCase();
  const list = f ? allWindows.filter((w) => (w.title || '').toLowerCase().includes(f)) : allWindows;
  box.innerHTML = '';
  for (const w of list) {
    const row = document.createElement('label');
    row.className = 'win-row';
    const cb = document.createElement('input');
    cb.type = 'checkbox';
    cb.value = w.handle;
    cb.checked = selectedHandles.has(w.handle);
    cb.addEventListener('change', () => {
      if (cb.checked) selectedHandles.add(w.handle);
      else selectedHandles.delete(w.handle);
    });
    const info = document.createElement('span');
    info.className = 'win-info';
    info.innerHTML = `<b>${escapeHtml(w.title || '(无标题)')}</b>`
      + `<small>${w.handle} · pid ${w.processId} · ${w.width}×${w.height}</small>`;
    row.appendChild(cb);
    row.appendChild(info);
    box.appendChild(row);
  }
  if (!list.length)
    box.innerHTML = `<div class="empty">${allWindows.length ? '无匹配窗口(改筛选词或清空)' : '未枚举到窗口'}</div>`;
}

async function pickWindow() {
  toast('3 秒内切到游戏窗口…', 'info');
  const btn = $('pickWindow');
  btn.disabled = true;
  try {
    const w = await api('/api/windows/pick', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ seconds: 3 }),
    });
    if (!w) { toast('未取到前台窗口', 'error'); return; }
    selectedHandles.add(w.handle);
    await loadWindows();
    toast(`已选:${w.title}`, 'ok');
  } catch (e) {
    toast('点选失败:' + e.message, 'error');
  } finally {
    btn.disabled = false;
  }
}

// ---------- 参数 ----------
// 加载已保存的运行参数(缺失/损坏时后端回落默认)。启动时调用,记住上次设置。
async function loadOptions() {
  const o = await api('/api/options');
  setMode(o.mode);
  $('rounds').value = o.rounds;
  $('durationMinutes').value = Math.round(o.durationMinutes);
  $('intervalMin').value = o.intervalMin;
  $('intervalMax').value = o.intervalMax;
  $('matchThreshold').value = o.matchThreshold;
  $('priority').value = o.priority || '';

  const a = o.antiDetection;
  $('antiEnabled').checked = a.enabled;
  $('clickDeviation').value = a.clickDeviation;
  $('rwEnabled').checked = a.randomWait.enabled;
  $('rwProbability').value = a.randomWait.probability;
  $('rwMin').value = a.randomWait.minSeconds;
  $('rwMax').value = a.randomWait.maxSeconds;
  $('rwGap').value = a.randomWait.minGapSeconds;
  $('fcEnabled').checked = a.frequencyCap.enabled;
  $('fcWindow').value = a.frequencyCap.windowMinutes;
  $('fcMax').value = a.frequencyCap.maxRounds;
  $('fcExtra').value = a.frequencyCap.extraWaitSeconds;
  $('repeatStop').checked = a.enableRepeatStop;
  $('repeatN').value = a.repeatSameTargetStop;
}

function setMode(mode) {
  document.querySelector(`input[name=mode][value=${mode}]`).checked = true;
  toggleMode();
}

// 改动防抖写回 config.json(静默;失败不打扰)。程序化 setValue 不触发 input/change,故加载时不会误存。
let saveOptionsTimer = null;
function scheduleSaveOptions() {
  clearTimeout(saveOptionsTimer);
  saveOptionsTimer = setTimeout(() => {
    api('/api/options', {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(collectRequest().options),
    }).catch(() => { /* 只读目录等,静默 */ });
  }, 600);
}

function toggleMode() {
  const mode = document.querySelector('input[name=mode]:checked').value;
  $('roundsField').classList.toggle('hidden', mode !== 'ByRounds');
  $('durationField').classList.toggle('hidden', mode !== 'ByMinutes');
}

function collectRequest() {
  const mode = document.querySelector('input[name=mode]:checked').value;
  const num = (id) => parseFloat($(id).value);
  return {
    windowHandles: [...selectedHandles],
    targetSet: $('targetSelect').value,
    options: {
      mode,
      rounds: parseInt($('rounds').value, 10),
      durationMinutes: num('durationMinutes'),
      intervalMin: num('intervalMin'),
      intervalMax: num('intervalMax'),
      matchThreshold: num('matchThreshold'),
      priority: $('priority').value || null,
      antiDetection: {
        enabled: $('antiEnabled').checked,
        clickDeviation: parseInt($('clickDeviation').value, 10),
        randomWait: {
          enabled: $('rwEnabled').checked,
          probability: num('rwProbability'),
          minSeconds: parseInt($('rwMin').value, 10),
          maxSeconds: parseInt($('rwMax').value, 10),
          minGapSeconds: parseInt($('rwGap').value, 10),
        },
        frequencyCap: {
          enabled: $('fcEnabled').checked,
          windowMinutes: parseInt($('fcWindow').value, 10),
          maxRounds: parseInt($('fcMax').value, 10),
          extraWaitSeconds: parseInt($('fcExtra').value, 10),
        },
        enableRepeatStop: $('repeatStop').checked,
        repeatSameTargetStop: parseInt($('repeatN').value, 10),
      },
    },
  };
}

async function start() {
  if (!selectedHandles.size) { toast('请先选择至少一个游戏窗口', 'error'); return; }
  if (!$('targetSelect').value) { toast('请选择目标集', 'error'); return; }
  try {
    await api('/api/engine/start', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(collectRequest()),
    });
    toast('引擎已启动', 'ok');
    // 新一次运行重置点击统计(点击分析只统计本次运行)
    clickLog.length = 0;
    // 记下本次运行选中窗口的客户区尺寸,散点图坐标轴按真实窗口分辨率画(而非点击点的 min/max)。
    // 多窗口(双开)分辨率不同时取最大值兜底,避免任一窗口的点被裁掉。
    const chosen = allWindows.filter((w) => selectedHandles.has(w.handle));
    runClientSize = chosen.length
      ? { width: Math.max(...chosen.map((w) => w.width)), height: Math.max(...chosen.map((w) => w.height)) }
      : null;
    renderAnalysis();
  } catch (e) {
    toast('启动失败:' + e.message, 'error');
  }
}

async function stop() {
  try { await api('/api/engine/stop', { method: 'POST' }); }
  catch (e) { toast('停止失败:' + e.message, 'error'); }
}

// ---------- 事件流 ----------
function connectEvents() {
  const es = new EventSource('/api/events');
  es.onmessage = (e) => handleEvent(JSON.parse(e.data));
  es.onerror = () => { /* EventSource 自动重连 */ };
}

function applyState(s) {
  const pill = $('statePill');
  pill.textContent = s.running ? '运行中' : '空闲';
  pill.className = 'pill ' + (s.running ? 'running' : 'idle');
  $('startBtn').disabled = s.running;
  $('stopBtn').disabled = !s.running;
  $('roundVal').textContent = s.round;
  setProgress(s.progress);
}

function setProgress(p) {
  $('progressBar').style.width = Math.max(0, Math.min(100, p)) + '%';
  $('progressVal').textContent = p + '%';
}

function handleEvent(evt) {
  switch (evt.type) {
    case 'state': applyState(evt); break;
    case 'round': $('roundVal').textContent = evt.round; appendLog('round', `第 ${evt.round} 轮开始`); break;
    case 'matched': appendLog('matched', `匹配 ${evt.name} @(${evt.x},${evt.y}) 分数 ${evt.score.toFixed(2)}`, evt.thumb, evt.mark); break;
    case 'clicked':
      appendLog('clicked', `点击 ${evt.name} → ${evt.points.map((p) => `(${p.x},${p.y})`).join(', ')}`);
      recordClicks(evt.name, evt.points);
      break;
    case 'waiting': appendLog('waiting', `等待 ${evt.seconds.toFixed(1)}s（${evt.reason}）`); break;
    case 'progress': setProgress(evt.percent); break;
    case 'stopped': appendLog('stopped', `停止:${evt.reason}`); break;
    case 'error': appendLog('error', `错误:${evt.message}`); break;
    case 'log': appendLog((evt.level || 'info').toLowerCase(), evt.text); break;
    default: appendLog('info', JSON.stringify(evt));
  }
}

function appendLog(kind, text, thumb, mark) {
  const log = $('log');
  const line = document.createElement('div');
  line.className = 'line ' + kind;
  const time = new Date().toLocaleTimeString('zh-CN', { hour12: false });
  line.innerHTML = `<span class="t">${time}</span><span class="k">${kind}</span><span class="m">${escapeHtml(text)}</span>`;
  if (thumb) {
    // 缩略图包一层相对定位容器,以便在其上叠加红点标出实际点击落点
    const wrap = document.createElement('span');
    wrap.className = 'log-thumb-wrap';
    const im = document.createElement('img');
    im.className = 'log-thumb';
    im.src = thumb;
    wrap.appendChild(im);
    if (mark) {
      // mark 是缩略图内归一化坐标(0~1),百分比定位,随缩放自适应
      const dot = document.createElement('span');
      dot.className = 'click-dot';
      dot.style.left = (mark.x * 100).toFixed(1) + '%';
      dot.style.top = (mark.y * 100).toFixed(1) + '%';
      dot.title = '点击位置';
      wrap.appendChild(dot);
    }
    line.querySelector('.m').appendChild(wrap);
  }
  log.appendChild(line);
  while (log.childElementCount > 800) log.removeChild(log.firstChild);
  if ($('autoScroll').checked) log.scrollTop = log.scrollHeight;
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// ---------- 点击位置分析(本次运行,数据来自事件流,不落文件) ----------
const clickLog = []; // { x, y, name, t } 客户区像素坐标 + 目标名 + 时间戳(ms)
let runClientSize = null; // { width, height } 本次运行窗口客户区尺寸,散点图坐标轴范围用它(而非点击点 min/max)
let analyzeOpen = false;
let analyzeRaf = 0;

// 目标名 → 稳定颜色(散点图与柱状图共用,互为图例);超出调色板则循环
const PALETTE = ['#7c3aed', '#0ea5e9', '#16a34a', '#d97706', '#dc2626', '#db2777', '#0891b2', '#65a30d'];
const colorCache = new Map();
function colorFor(name) {
  if (!colorCache.has(name)) colorCache.set(name, PALETTE[colorCache.size % PALETTE.length]);
  return colorCache.get(name);
}

function recordClicks(name, points) {
  if (!points || !points.length) return;
  const now = Date.now();
  for (const p of points) clickLog.push({ x: p.x, y: p.y, name, t: now });
  if (analyzeOpen) scheduleAnalysisRender();
}

function openAnalyze() {
  analyzeOpen = true;
  $('analyzeModal').classList.remove('hidden');
  renderAnalysis();
}
function closeAnalyze() {
  analyzeOpen = false;
  $('analyzeModal').classList.add('hidden');
}
function scheduleAnalysisRender() {
  if (analyzeRaf) return;
  analyzeRaf = requestAnimationFrame(() => { analyzeRaf = 0; renderAnalysis(); });
}

function renderAnalysis() {
  if (!analyzeOpen) return;
  const total = clickLog.length;
  const names = [...new Set(clickLog.map((c) => c.name))];
  $('analyzeSummary').textContent = total
    ? `共 ${total} 次点击 · ${names.length} 个目标`
    : '本次运行暂无点击';
  drawScatter($('chartScatter'), clickLog);
  drawTargetBars($('chartTargets'), clickLog);
  drawTimeBars($('chartTime'), clickLog);
}

// canvas 高清初始化(按 devicePixelRatio 缩放,返回 CSS 像素坐标系的 ctx)
function setupCanvas(canvas) {
  const dpr = window.devicePixelRatio || 1;
  const rect = canvas.getBoundingClientRect();
  const w = Math.max(1, Math.round(rect.width));
  const h = Math.max(1, Math.round(rect.height));
  canvas.width = Math.round(w * dpr);
  canvas.height = Math.round(h * dpr);
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  return { ctx, w, h };
}

function themeColors() {
  const s = getComputedStyle(document.body);
  const g = (k, d) => (s.getPropertyValue(k).trim() || d);
  return {
    text: g('--text', '#1f2430'),
    muted: g('--muted', '#6b7280'),
    border: g('--border', '#e2e5ea'),
    accent: g('--accent', '#7c3aed'),
  };
}

function drawEmpty(ctx, w, h, color) {
  ctx.fillStyle = color;
  ctx.font = '12px sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText('本次运行暂无点击数据', w / 2, h / 2);
}

function fitLabel(ctx, text, maxW) {
  let s = String(text);
  if (ctx.measureText(s).width <= maxW - 4) return s;
  while (s.length > 1 && ctx.measureText(s + '…').width > maxW - 4) s = s.slice(0, -1);
  return s + '…';
}

// 点击散点图:坐标轴范围用本次运行窗口的真实客户区分辨率(而非点击点 min/max),
// 这样才能看出点击落在整个窗口的哪个位置,不会因点集中一角就把范围收窄、丢失其余空间的上下文;
// 按窗口宽高比 contain 适配(letterbox),避免拉伸失真。canvas 的 y 天然向下,与屏幕坐标一致(顶部=小 y)。
function drawScatter(canvas, pts) {
  const { ctx, w, h } = setupCanvas(canvas);
  const tc = themeColors();
  if (!pts.length) { drawEmpty(ctx, w, h, tc.muted); return; }

  const pad = 14;
  const availW = w - 2 * pad, availH = h - 2 * pad;

  let boundsW, boundsH, originX = 0, originY = 0, labelResolution = true;
  if (runClientSize && runClientSize.width > 0 && runClientSize.height > 0) {
    boundsW = runClientSize.width;
    boundsH = runClientSize.height;
  } else {
    // 兜底(理论上不会发生,一次运行必选窗口):按数据范围,保证最小跨度避免单簇被过度放大。
    labelResolution = false;
    const xs = pts.map((p) => p.x), ys = pts.map((p) => p.y);
    let minX = Math.min(...xs), maxX = Math.max(...xs);
    let minY = Math.min(...ys), maxY = Math.max(...ys);
    const spanMin = 80;
    if (maxX - minX < spanMin) { const c = (minX + maxX) / 2; minX = c - spanMin / 2; maxX = c + spanMin / 2; }
    if (maxY - minY < spanMin) { const c = (minY + maxY) / 2; minY = c - spanMin / 2; maxY = c + spanMin / 2; }
    originX = minX; originY = minY;
    boundsW = maxX - minX; boundsH = maxY - minY;
  }

  const scale = Math.min(availW / boundsW, availH / boundsH);
  const plotW = boundsW * scale, plotH = boundsH * scale;
  const offX = pad + (availW - plotW) / 2, offY = pad + (availH - plotH) / 2;
  const sx = (x) => offX + (x - originX) * scale;
  const sy = (y) => offY + (y - originY) * scale;

  ctx.strokeStyle = tc.border;
  ctx.lineWidth = 1;
  ctx.strokeRect(offX, offY, plotW, plotH);

  ctx.globalAlpha = 0.75;
  for (const p of pts) {
    ctx.beginPath();
    ctx.arc(sx(p.x), sy(p.y), 2.5, 0, Math.PI * 2);
    ctx.fillStyle = colorFor(p.name);
    ctx.fill();
  }
  ctx.globalAlpha = 1;

  ctx.fillStyle = tc.muted;
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'left';
  ctx.textBaseline = 'top';
  const label = labelResolution
    ? `窗口客户区 ${Math.round(boundsW)}×${Math.round(boundsH)}`
    : `x:${Math.round(originX)}~${Math.round(originX + boundsW)}  y:${Math.round(originY)}~${Math.round(originY + boundsH)}(未知窗口分辨率,按点击范围显示)`;
  ctx.fillText(label, pad + 2, 2);
}

// 通用竖直柱状图
function drawBars(canvas, labels, values, colorFn) {
  const { ctx, w, h } = setupCanvas(canvas);
  const tc = themeColors();
  if (!values.length || values.every((v) => v === 0)) { drawEmpty(ctx, w, h, tc.muted); return; }

  const padL = 28, padR = 10, padT = 14, padB = 34;
  const plotW = w - padL - padR, plotH = h - padT - padB;
  const maxV = Math.max(...values);

  ctx.strokeStyle = tc.border;
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(padL, padT);
  ctx.lineTo(padL, padT + plotH);
  ctx.lineTo(padL + plotW, padT + plotH);
  ctx.stroke();

  ctx.fillStyle = tc.muted;
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'right';
  ctx.textBaseline = 'middle';
  ctx.fillText(String(maxV), padL - 4, padT + 4);
  ctx.fillText('0', padL - 4, padT + plotH);

  const n = values.length;
  const bw = plotW / n;
  const barW = Math.min(bw * 0.7, 46);
  for (let i = 0; i < n; i++) {
    const bh = maxV ? values[i] / maxV * plotH : 0;
    const cx = padL + bw * (i + 0.5);
    ctx.fillStyle = colorFn ? colorFn(labels[i], i) : tc.accent;
    ctx.fillRect(cx - barW / 2, padT + plotH - bh, barW, bh);
    if (values[i] > 0) {
      ctx.fillStyle = tc.text;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'bottom';
      ctx.fillText(String(values[i]), cx, padT + plotH - bh - 2);
    }
    ctx.fillStyle = tc.muted;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.fillText(fitLabel(ctx, labels[i], bw), cx, padT + plotH + 5);
  }
}

function drawTargetBars(canvas, pts) {
  const counts = new Map();
  for (const p of pts) counts.set(p.name, (counts.get(p.name) || 0) + 1);
  const labels = [...counts.keys()];
  const values = labels.map((k) => counts.get(k));
  drawBars(canvas, labels, values, (name) => colorFor(name));
}

function drawTimeBars(canvas, pts) {
  if (!pts.length) {
    const { ctx, w, h } = setupCanvas(canvas);
    drawEmpty(ctx, w, h, themeColors().muted);
    return;
  }
  const ts = pts.map((p) => p.t);
  const minT = Math.min(...ts), maxT = Math.max(...ts);
  const span = Math.max(1, maxT - minT);
  const bins = Math.min(12, Math.max(4, Math.ceil(span / 30000))); // ~30s/桶,限 4~12 桶
  const binMs = span / bins;
  const values = new Array(bins).fill(0);
  for (const t of ts) {
    let idx = Math.floor((t - minT) / binMs);
    if (idx >= bins) idx = bins - 1;
    values[idx]++;
  }
  const labels = values.map((_, i) => fmtElapsed(i * binMs)); // 相对本次运行首次点击的用时
  drawBars(canvas, labels, values, null);
}

function fmtElapsed(ms) {
  const s = Math.round(ms / 1000);
  const m = Math.floor(s / 60);
  return m > 0 ? `${m}m${String(s % 60).padStart(2, '0')}` : `${s}s`;
}

// ---------- 视图切换 ----------
function bindTabs() {
  document.querySelectorAll('.tab').forEach((t) =>
    t.addEventListener('click', () => switchView(t.dataset.view)));
}

function switchView(view) {
  document.querySelectorAll('.tab').forEach((t) => t.classList.toggle('active', t.dataset.view === view));
  $('view-run').classList.toggle('hidden', view !== 'run');
  $('view-manage').classList.toggle('hidden', view !== 'manage');
  if (view === 'manage') mgInit().catch((e) => toast(e.message, 'error'));
}

// ---------- 绑定 ----------
function bind() {
  $('refreshTargets').addEventListener('click', () => loadTargets().catch((e) => toast(e.message, 'error')));
  $('targetSelect').addEventListener('change', () => onTargetChange().catch((e) => toast(e.message, 'error')));
  $('refreshWindows').addEventListener('click', () => loadWindows().catch((e) => toast(e.message, 'error')));
  $('windowFilter').addEventListener('input', renderWindows);
  $('pickWindow').addEventListener('click', pickWindow);
  $('startBtn').addEventListener('click', start);
  $('stopBtn').addEventListener('click', stop);
  $('clearLog').addEventListener('click', () => ($('log').innerHTML = ''));
  $('analyzeBtn').addEventListener('click', openAnalyze);
  $('analyzeClose').addEventListener('click', closeAnalyze);
  $('analyzeClear').addEventListener('click', () => { clickLog.length = 0; renderAnalysis(); });
  $('analyzeModal').addEventListener('click', (e) => { if (e.target === $('analyzeModal')) closeAnalyze(); });
  window.addEventListener('resize', () => { if (analyzeOpen) scheduleAnalysisRender(); });
  document.querySelectorAll('input[name=mode]').forEach((r) => r.addEventListener('change', toggleMode));
  $('antiHead').addEventListener('click', () => {
    $('antiBody').classList.toggle('collapsed');
    $('antiHead').classList.toggle('collapsed');
  });
  // 运行参数改动 → 防抖写回 config.json(事件委托到控制卡片,涵盖全部参数/防检测输入)。
  const controlCard = document.querySelector('.control-card');
  if (controlCard) {
    controlCard.addEventListener('input', scheduleSaveOptions);
    controlCard.addEventListener('change', scheduleSaveOptions);
  }
}

async function init() {
  bind();
  bindTabs();
  connectEvents();
  try {
    await loadOptions();
    await loadTargets();
    await loadWindows();
  } catch (e) {
    toast('初始化失败:' + e.message, 'error');
  }
}

// ==================== 目标管理 ====================
const mg = {
  loaded: false,
  bound: false,
  name: null,
  model: null,        // target.json 形状 { name, defaults?, images:[{file,priority,flag,click?,matcher?,threshold?}] }
  capUrl: null,
  natW: 0, natH: 0,
  mode: 'crop',       // 'crop' | 'point'
  pointRow: -1,
  sel: null,          // {x,y,w,h} 自然像素
  dragging: false,
  dragStart: null,    // 显示像素
  disp: null,         // 拖拽中的显示矩形
};

const clampf = (v, a, b) => Math.max(a, Math.min(b, v));
const round4 = (v) => Math.round(v * 10000) / 10000;
const flagLabelFull = (f) => ({ Normal: '普通', RoundStart: '开局', Once: '单次', Skip: '跳过', Stop: '终止' }[f] || f);

async function mgInit() {
  if (!mg.loaded) {
    mg.loaded = true;
    bindManage();
    await Promise.all([mgLoadSets(), mgLoadWindows()]);
  }
}

function bindManage() {
  if (mg.bound) return;
  mg.bound = true;
  $('createSet').addEventListener('click', createSet);
  $('mgRefresh').addEventListener('click', () => mgLoadSets().catch((e) => toast(e.message, 'error')));
  $('mgTargetSelect').addEventListener('change', () => mgSelectSet().catch((e) => toast(e.message, 'error')));
  $('mgOpenFolder').addEventListener('click', () => mgOpenFolder().catch((e) => toast(e.message, 'error')));
  $('mgWinRefresh').addEventListener('click', () => mgLoadWindows().catch((e) => toast(e.message, 'error')));
  $('mgWindowFilter').addEventListener('input', renderMgWindows);
  $('mgCapture').addEventListener('click', () => mgCapture().catch((e) => toast(e.message, 'error')));
  $('modeCrop').addEventListener('click', () => setCropMode());
  $('saveCrop').addEventListener('click', () => saveCrop().catch((e) => toast(e.message, 'error')));
  $('saveTargetJson').addEventListener('click', () => saveTargetJson().catch((e) => toast(e.message, 'error')));

  const stage = $('captureStage');
  stage.addEventListener('mousedown', onStageDown);
  window.addEventListener('mousemove', onStageMove);
  window.addEventListener('mouseup', onStageUp);
  stage.addEventListener('click', onStageClick);
}

async function mgLoadSets() {
  const sets = await api('/api/targets');
  const sel = $('mgTargetSelect');
  const prev = sel.value;
  sel.innerHTML = '<option value="">（选择目标集）</option>';
  for (const s of sets) {
    const o = document.createElement('option');
    o.value = s.name;
    o.textContent = `${s.name}（${s.imageCount} 图）`;
    sel.appendChild(o);
  }
  if (prev && sets.some((s) => s.name === prev)) { sel.value = prev; }
}

let mgAllWindows = [];

async function mgLoadWindows() {
  mgAllWindows = await api('/api/windows');
  renderMgWindows();
}

function renderMgWindows() {
  const sel = $('mgWindowSelect');
  const prev = sel.value;
  const f = ($('mgWindowFilter').value || '').trim().toLowerCase();
  const list = f ? mgAllWindows.filter((w) => (w.title || '').toLowerCase().includes(f)) : mgAllWindows;
  sel.innerHTML = '';
  for (const w of list) {
    const o = document.createElement('option');
    o.value = w.handle;
    o.textContent = `${w.title || '(无标题)'} · ${w.width}×${w.height}`;
    sel.appendChild(o);
  }
  if (prev && list.some((w) => w.handle === prev)) sel.value = prev;
}

async function mgSelectSet() {
  const name = $('mgTargetSelect').value;
  mg.name = name || null;
  setCropMode();
  $('mgOpenFolder').disabled = !name;
  if (!name) { mg.model = null; $('saveTargetJson').disabled = true; renderMgImages(); return; }
  mg.model = await api(`/api/targets/${encodeURIComponent(name)}/edit`);
  $('saveTargetJson').disabled = false;
  renderMgImages();
}

async function mgOpenFolder() {
  if (!mg.name) return;
  await api(`/api/targets/${encodeURIComponent(mg.name)}/open-folder`, { method: 'POST' });
}

async function createSet() {
  const name = $('newSetName').value.trim();
  if (!name) { toast('请输入玩法名', 'error'); return; }
  try {
    await api('/api/targets/create', {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name }),
    });
    toast('已创建目标集 ' + name, 'ok');
    $('newSetName').value = '';
    await mgLoadSets();
    $('mgTargetSelect').value = name;
    await mgSelectSet();
    await loadTargets();
  } catch (e) { toast('创建失败:' + e.message, 'error'); }
}

async function mgCapture() {
  const handle = $('mgWindowSelect').value;
  if (!handle) { toast('请选择窗口', 'error'); return; }
  const res = await fetch('/api/capture', {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ windowHandle: handle }),
  });
  if (!res.ok) {
    const t = await res.json().catch(() => ({}));
    throw new Error(t.error || res.statusText);
  }
  const blob = await res.blob();
  if (mg.capUrl) URL.revokeObjectURL(mg.capUrl);
  mg.capUrl = URL.createObjectURL(blob);
  const img = $('captureImg');
  img.onload = () => {
    mg.natW = img.naturalWidth;
    mg.natH = img.naturalHeight;
    $('captureStage').classList.remove('empty');
  };
  img.src = mg.capUrl;
  clearSelection();
}

// ---- 框选模板 ----
function imgRect() { return $('captureImg').getBoundingClientRect(); }

function onStageDown(e) {
  if (!mg.natW || mg.mode !== 'crop') return;
  const rect = imgRect();
  mg.dragging = true;
  mg.dragStart = { x: clampf(e.clientX - rect.left, 0, rect.width), y: clampf(e.clientY - rect.top, 0, rect.height) };
  const sr = $('selRect');
  sr.classList.remove('hidden');
  setSelRect(mg.dragStart.x, mg.dragStart.y, 0, 0);
  e.preventDefault();
}

function onStageMove(e) {
  if (!mg.dragging) return;
  const rect = imgRect();
  const x = clampf(e.clientX - rect.left, 0, rect.width);
  const y = clampf(e.clientY - rect.top, 0, rect.height);
  const left = Math.min(x, mg.dragStart.x), top = Math.min(y, mg.dragStart.y);
  const w = Math.abs(x - mg.dragStart.x), h = Math.abs(y - mg.dragStart.y);
  mg.disp = { left, top, w, h };
  setSelRect(left, top, w, h);
}

function onStageUp() {
  if (!mg.dragging) return;
  mg.dragging = false;
  const rect = imgRect();
  const d = mg.disp;
  if (!d || d.w < 5 || d.h < 5) { clearSelection(); return; }
  const rx = mg.natW / rect.width, ry = mg.natH / rect.height;
  mg.sel = { x: Math.round(d.left * rx), y: Math.round(d.top * ry), w: Math.round(d.w * rx), h: Math.round(d.h * ry) };
  $('selInfo').textContent = `选区 ${mg.sel.w}×${mg.sel.h} @(${mg.sel.x},${mg.sel.y})`;
  $('saveCrop').disabled = false;
}

function setSelRect(left, top, w, h) {
  const sr = $('selRect');
  sr.style.left = left + 'px'; sr.style.top = top + 'px';
  sr.style.width = w + 'px'; sr.style.height = h + 'px';
}

function clearSelection() {
  mg.sel = null; mg.disp = null;
  $('selRect').classList.add('hidden');
  $('selInfo').textContent = '';
  $('saveCrop').disabled = true;
}

async function saveCrop() {
  if (!mg.sel || !mg.name) { toast('请先框选选区', 'error'); return; }
  let fname = $('newImgName').value.trim();
  if (!fname) { toast('请输入模板文件名', 'error'); return; }
  if (!/\.(png|jpe?g)$/i.test(fname)) fname += '.png';

  const img = $('captureImg');
  const c = document.createElement('canvas');
  c.width = mg.sel.w; c.height = mg.sel.h;
  c.getContext('2d').drawImage(img, mg.sel.x, mg.sel.y, mg.sel.w, mg.sel.h, 0, 0, mg.sel.w, mg.sel.h);
  const dataUrl = c.toDataURL('image/png');

  const r = await api(`/api/targets/${encodeURIComponent(mg.name)}/images`, {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ file: fname, dataUrl }),
  });
  toast('已保存模板 ' + r.file, 'ok');
  $('newImgName').value = '';
  if (!mg.model.images.some((i) => i.file === r.file))
    mg.model.images.push({ file: r.file, priority: mg.model.images.length + 1, flag: 'Normal' });
  renderMgImages();
  clearSelection();
  await mgLoadSets();
  await loadTargets();
}

// ---- 偏移点击点选 ----
function enterPointMode(idx) {
  mg.mode = 'point';
  mg.pointRow = idx;
  $('modeCrop').classList.remove('active');
  clearSelection();
  renderPoints();
  renderMgImages();
  toast('点选模式:在截图上单击添加偏移点击点', 'info');
}

function setCropMode() {
  mg.mode = 'crop';
  mg.pointRow = -1;
  $('modeCrop').classList.add('active');
  renderPoints();
  if (mg.model) renderMgImages();
}

function onStageClick(e) {
  if (mg.mode !== 'point' || mg.pointRow < 0 || !mg.natW || !mg.model) return;
  if (mg.dragging) return;
  const rect = imgRect();
  const nx = clampf((e.clientX - rect.left) / rect.width, 0, 1);
  const ny = clampf((e.clientY - rect.top) / rect.height, 0, 1);
  const img = mg.model.images[mg.pointRow];
  if (!img.click) img.click = { clickPos: [] };
  img.click.clickPos.push([round4(nx), round4(ny)]);
  renderPoints();
  renderMgImages();
}

function renderPoints() {
  const layer = $('pointLayer');
  layer.innerHTML = '';
  if (mg.mode !== 'point' || mg.pointRow < 0 || !mg.model) return;
  const pts = mg.model.images[mg.pointRow].click?.clickPos || [];
  for (const p of pts) {
    const dot = document.createElement('div');
    dot.className = 'pt';
    dot.style.left = (p[0] * 100) + '%';
    dot.style.top = (p[1] * 100) + '%';
    layer.appendChild(dot);
  }
}

// 匹配优先级 = 图片在列表中的先后顺序(从上到下、自动设置),不再手填;拖动列表项调整顺序即改优先级。
function recomputePriorities() {
  if (!mg.model) return;
  mg.model.images.forEach((img, i) => { img.priority = i + 1; });
}

function moveImage(from, to) {
  if (from === to) return;
  const arr = mg.model.images;
  const [item] = arr.splice(from, 1);
  arr.splice(to, 0, item);
  renderMgImages();
}

let mgDragFrom = -1;

function renderMgImages() {
  const box = $('mgImages');
  const head = $('mgImagesHead');
  if (!mg.model) {
    box.innerHTML = '<div class="empty">先在左侧选择要编辑的目标集</div>';
    head.classList.add('hidden');
    return;
  }
  head.classList.remove('hidden');
  recomputePriorities();
  $('mgImagesTitle').textContent = `图片(${mg.model.images.length})· ${mg.name}`;
  box.innerHTML = '';
  mg.model.images.forEach((img, idx) => {
    const row = document.createElement('div');
    row.className = 'mg-row' + (mg.mode === 'point' && mg.pointRow === idx ? ' pointing' : '');

    const handle = document.createElement('span');
    handle.className = 'mg-handle';
    handle.textContent = '⠿';
    handle.title = '拖动调整优先级顺序';
    handle.addEventListener('mousedown', () => { row.draggable = true; });

    const thumb = document.createElement('img');
    thumb.className = 'mg-thumb';
    thumb.loading = 'lazy';
    thumb.src = `/api/targets/${encodeURIComponent(mg.name)}/images/${encodeURIComponent(img.file)}`;

    const nameCol = document.createElement('div');
    nameCol.className = 'mg-name';
    nameCol.textContent = img.file;

    const flagSel = document.createElement('select');
    flagSel.className = 'mg-flag';
    for (const f of ['Normal', 'RoundStart', 'Once', 'Skip', 'Stop']) {
      const o = document.createElement('option');
      o.value = f; o.textContent = flagLabelFull(f);
      if ((img.flag || 'Normal') === f) o.selected = true;
      flagSel.appendChild(o);
    }
    flagSel.addEventListener('change', () => { img.flag = flagSel.value; });

    const prio = document.createElement('span');
    prio.className = 'mg-priority-val';
    prio.textContent = img.priority;

    const count = img.click?.clickPos?.length || 0;
    const pointBtn = document.createElement('button');
    pointBtn.className = 'ghost small';
    pointBtn.textContent = `偏移点(${count})`;
    pointBtn.title = '在截图上点选客户区偏移点击点';
    pointBtn.addEventListener('click', () => enterPointMode(idx));

    const clearBtn = document.createElement('button');
    clearBtn.className = 'ghost small';
    clearBtn.textContent = '清点';
    clearBtn.addEventListener('click', () => {
      if (img.click) img.click.clickPos = [];
      renderPoints(); renderMgImages();
    });

    const del = document.createElement('button');
    del.className = 'ghost small danger';
    del.textContent = '删';
    del.addEventListener('click', () => mgDeleteImage(img.file));

    row.addEventListener('dragstart', (e) => {
      mgDragFrom = idx;
      row.classList.add('dragging');
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', String(idx));
    });
    row.addEventListener('dragend', () => {
      row.draggable = false;
      row.classList.remove('dragging');
      mgDragFrom = -1;
    });
    row.addEventListener('dragover', (e) => {
      if (mgDragFrom < 0) return;
      e.preventDefault();
      e.dataTransfer.dropEffect = 'move';
    });
    row.addEventListener('drop', (e) => {
      e.preventDefault();
      const from = mgDragFrom;
      if (from < 0 || from === idx) return;
      moveImage(from, idx);
    });

    row.append(handle, thumb, nameCol, flagSel, prio, pointBtn, clearBtn, del);
    box.appendChild(row);
  });
}

async function mgDeleteImage(file) {
  if (!confirm(`删除模板 ${file}?(会从磁盘删除)`)) return;
  try {
    await api(`/api/targets/${encodeURIComponent(mg.name)}/images/${encodeURIComponent(file)}`, { method: 'DELETE' });
    mg.model.images = mg.model.images.filter((i) => i.file !== file);
    setCropMode();
    renderMgImages();
    toast('已删除 ' + file, 'ok');
    await mgLoadSets();
    await loadTargets();
  } catch (e) { toast('删除失败:' + e.message, 'error'); }
}

async function saveTargetJson() {
  if (!mg.model || !mg.name) return;
  await api(`/api/targets/${encodeURIComponent(mg.name)}`, {
    method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(mg.model),
  });
  toast('已保存 target.json', 'ok');
  await mgLoadSets();
  await loadTargets();
}

init();
