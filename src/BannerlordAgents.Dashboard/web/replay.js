'use strict';
// Recordings (dashboard 0.10): a whole test run played back from its first hour to its last, across every load it kept, with the
// agents' decisions as captions, the game's news as pop-ups on the map, and a presentation mode for demos. It also owns the
// timeline for the live session and for earlier loads. It shares app.js's state (S) and drawing; nothing here talks to the game.

const R = {
  source: 'live',                 // 'live', 'load:<id>' or 'run:<id>'
  runs: [], run: null, line: [],  // the runs on disk, the run open, and its kept line: [{ load, h }]
  t: 0, speed: 6, playing: false, lastTs: 0, lastT: null,
  cache: new Map(), fetching: new Set(), shown: -1,
  news: [], decisions: [], newsI: 0, decI: 0,
  captions: [], pops: [], toasts: [],
  present: false, idleTimer: 0, prepTimer: 0, logAt: 0, fullTimer: 0, marks: [],
};
const SPEEDS = [1, 3, 6, 12, 24, 48];
const runMode = () => R.source.startsWith('run:');
const CAPTION_MS = 9000, POP_MS = 4500, TOAST_MS = 9000;
const MAJOR = new Set(['battle', 'captured', 'lord_killed', 'siege_started', 'siege_ended', 'holding_changed', 'war', 'peace']);
const NEWS_LABEL = {
  battle: 'Battle', captured: 'Taken prisoner', lord_killed: 'Killed', siege_started: 'Siege', siege_ended: 'Siege lifted', holding_changed: 'Taken',
  raided: 'Raid', looted: 'Looted', army_formed: 'Army', war: 'War', peace: 'Peace', alliance: 'Alliance', letter: 'Letter',
};

// ---------------------------------------------------------------- names as the game meant them
// Some names reach the frames double-encoded (UTF-8 read as Latin-1 or Windows-1252): put them back for showing.

const CP1252 = { 0x20ac: 0x80, 0x201a: 0x82, 0x0192: 0x83, 0x201e: 0x84, 0x2026: 0x85, 0x2020: 0x86, 0x2021: 0x87, 0x02c6: 0x88, 0x2030: 0x89, 0x0160: 0x8a, 0x2039: 0x8b,
  0x0152: 0x8c, 0x017d: 0x8e, 0x2018: 0x91, 0x2019: 0x92, 0x201c: 0x93, 0x201d: 0x94, 0x2022: 0x95, 0x2013: 0x96, 0x2014: 0x97, 0x02dc: 0x98, 0x2122: 0x99, 0x0161: 0x9a,
  0x203a: 0x9b, 0x0153: 0x9c, 0x017e: 0x9e, 0x0178: 0x9f };
const utf8 = new TextDecoder('utf-8', { fatal: true });
function fixText(s) {
  if (typeof s !== 'string' || !/[Â-Å]/.test(s)) return s;
  const bytes = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    const b = c < 256 ? c : CP1252[c];
    if (b === undefined) return s;
    bytes[i] = b;
  }
  try { return utf8.decode(bytes); } catch (e) { return s; }
}
function fixFrame(f) {
  if (!f || f._fixed) return f;
  for (const p of f.parties || []) p.name = fixText(p.name);
  for (const x of f.settlements || []) x.name = fixText(x.name);
  for (const x of f.factions || []) { x.name = fixText(x.name); x.ruler = fixText(x.ruler); }
  for (const r of Object.values(f.realms || {})) {
    r.name = fixText(r.name);
    const v = r.view || {};
    for (const list of [v.seen, v.last_seen, r.remembered]) for (const vp of list || []) vp.name = fixText(vp.name);
  }
  f._fixed = true;
  return f;
}

// ---------------------------------------------------------------- an hour's light copy, back in the frame's shape

function inflate(z) {
  const f = {
    v: 1, slim: true, campaign: z.campaign, load_id: z.load_id, hours: z.hours, date: z.date, night: z.night, player: z.player,
    player_party: z.player_party, watched: z.watched, map: z.map, factions: z.factions || [],
  };
  f.settlements = (z.settlements || []).map((r) => ({ id: r[0], name: r[1], kind: r[2], faction: r[3], x: r[4], y: r[5], besieged_by: r[6], village_state: r[7] }));
  f.parties = (z.parties || []).map((r) => ({
    id: r[0], name: r[1], kind: r[2], faction: r[3], x: r[4], y: r[5], men: r[6], strength: r[7], army_leader: r[8], army_men: r[9], army_strength: r[10],
    in_battle: !!r[11], in_settlement: r[12], leader: r[13], doing: r[14], target_settlement: r[15], besieging: r[16], raiding: r[17], attached: !!r[18],
  }));
  const vp = (r) => ({ id: r[0], lord: r[1], name: r[2], faction: r[3], relation: r[4], kind: r[5], doing: r[6], men: r[7], army_men: r[8], strength: r[9], x: r[10], y: r[11], seen_hours: r[12] });
  f.realms = {};
  for (const [k, r] of Object.entries(z.realms || {})) {
    const v = r.view || {};
    f.realms[k] = {
      name: r.name, agent: r.agent, player: r.player, cut: r.cut || [], counted_only: r.counted_only || [], in_sight: r.in_sight || [], orders: r.orders || {},
      remembered: (r.remembered || []).map(vp), view: { seen: (v.seen || []).map(vp), last_seen: (v.last_seen || []).map(vp), holdings: v.holdings || [], trails: [] },
    };
  }
  f.sieges = (z.sieges || []).map((r) => ({ settlement: r[0], besieger: r[1], besieger_faction: r[2], start_hours: r[3] }));
  f.battles = (z.battles || []).map((r) => ({
    key: r[0], kind: r[1], settlement: r[2], x: r[3], y: r[4], start_hours: r[5],
    attackers: { leader: r[6], faction: r[7], healthy: r[8], start_healthy: r[9] }, defenders: { leader: r[10], faction: r[11], healthy: r[12], start_healthy: r[13] },
  }));
  return fixFrame(f);
}

function fetchSlim(i) {
  if (i < 0 || i >= R.line.length || R.cache.has(i) || R.fetching.has(i)) return;
  const at = R.line[i], run = R.run;
  R.fetching.add(i);
  getJSON('/api/slim?campaign=' + encodeURIComponent(run.campaign) + '&load=' + encodeURIComponent(at.load) + '&h=' + at.h)
    .then((z) => { if (R.run === run) R.cache.set(i, inflate(z)); })
    .catch(() => { /* asked again when needed */ })
    .finally(() => R.fetching.delete(i));
}

function prefetch(i) {
  const ahead = Math.max(16, Math.round(R.speed * 3));
  for (let k = i; k < Math.min(R.line.length, i + ahead) && R.fetching.size < 6; k++) fetchSlim(k);
  // keep the light copies near the playhead only
  if (R.cache.size > 360) for (const k of R.cache.keys()) if (k < i - 60 || k > i + 300) R.cache.delete(k);
}

// ---------------------------------------------------------------- the sources: the live session, earlier loads, test runs

function runLabel(r) {
  const days = Math.round(r.hours / 24 * 10) / 10;
  const when = (r.started || '').replace(/^\d{4}-(\d{2})-(\d{2}) (\d{2}):(\d{2}).*$/, '$2.$1 $3:$4');
  return 'Run ' + r.id + ' · ' + (r.save || '?') + ' · ' + days + ' days (' + when + ')';
}

function fillSources(st) {
  const sel = $('source');
  const cur = st && st.current;
  const loads = (st && st.loads) || [];
  const key = loads.map((l) => l.load_id + ':' + l.frames).join('|') + '#' + R.runs.map((r) => r.id + ':' + r.hours).join('|') + '#' + (cur ? cur.load_id : '');
  if (sel.dataset.key === key) { sel.value = R.source; return; }
  sel.dataset.key = key;
  let html = '<option value="live">The live session</option>';
  const earlier = loads.filter((l) => !cur || l.load_id !== cur.load_id);
  if (earlier.length) {
    html += '<optgroup label="Earlier loads of this campaign">' + earlier.map((l) => {
      const wall = l.load_id.replace(/^(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2}).*$/, '$3.$2 $4:$5');
      return '<option value="load:' + esc(l.load_id) + '">Load of ' + esc(wall) + ': ' + when(l.first, true) + ' to ' + when(l.last, true) + '</option>';
    }).join('') + '</optgroup>';
  }
  if (R.runs.length) html += '<optgroup label="Test runs, played back whole">' + R.runs.map((r) => '<option value="run:' + esc(r.id) + '">' + esc(runLabel(r)) + '</option>').join('') + '</optgroup>';
  sel.innerHTML = html;
  sel.value = R.source;
}

async function refreshRuns() {
  try { const d = await getJSON('/api/runs'); R.runs = d.runs || []; fillSources(S.status); } catch (e) { /* tried again later */ }
}

$('source').onchange = () => switchSource($('source').value);

async function switchSource(v) {
  stopRunPlay(false);
  clearOverlays();
  R.source = v;
  document.body.classList.toggle('run-mode', runMode());
  if (v === 'live') {
    R.run = null; R.line = []; R.marks = []; drawMarks();
    const cur = S.status && S.status.current;
    S.live = true;
    if (cur) { S.campaign = cur.campaign; S.load = cur.load_id; }
    await refreshHours();
    await loadFrame(null);
    refreshLog();
  } else if (v.startsWith('load:')) {
    R.run = null; R.line = []; R.marks = []; drawMarks();
    S.load = v.slice(5);
    const cur = S.status && S.status.current;
    S.live = false;
    if (cur) S.campaign = cur.campaign;
    await refreshHours();
    await loadFrame(S.hours[S.hours.length - 1]);
    refreshLog();
  } else if (v.startsWith('run:')) {
    await openRun(v.slice(4));
  }
  $('source').value = R.source;
  syncSlider();
}

// ---------------------------------------------------------------- a run opened

async function openRun(id, at) {
  let d;
  try { d = await getJSON('/api/run?id=' + encodeURIComponent(id)); }
  catch (e) { showEmpty('This run cannot be played', e.message); return; }
  R.run = d;
  R.line = [];
  for (const s of d.segments || []) for (const h of s.hours) R.line.push({ load: s.load, h, seg: s });
  R.cache.clear(); R.fetching.clear(); R.shown = -1;
  R.news = (d.news || []).map((n) => Object.assign(n, { text: fixText(n.text) }));
  R.decisions = [];
  for (const [realm, list] of Object.entries(d.decisions || {})) for (const e of list) R.decisions.push(Object.assign(e, { realm, text: fixText(e.text), plan: fixText(e.plan) }));
  R.decisions.sort((a, b) => a.hours - b.hours);
  S.campaign = d.campaign; S.live = false; S.hours = []; S.log = [];
  R.t = Math.max(0, Math.min(R.line.length - 1, at || 0));
  seekPointers();
  buildMarks();
  await showFull(Math.floor(R.t));
  refreshRunLog();
  watchPrep(d.prep);
}

function watchPrep(p) {
  clearTimeout(R.prepTimer);
  const chip = $('live');
  if (!runMode()) return;
  if (p && !p.over) {
    chip.textContent = 'preparing ' + p.done + ' of ' + p.total; chip.className = 'chip chip-past';
    chip.title = 'Each hour of the run is being made ready for smooth playback; it can play already.';
    R.prepTimer = setTimeout(async () => {
      if (!runMode() || !R.run) return;
      try { watchPrep(await getJSON('/api/runprep?id=' + encodeURIComponent(R.run.id))); } catch (e) { /* stop asking */ }
    }, 1500);
  } else { chip.textContent = 'recording'; chip.className = 'chip chip-live'; chip.title = 'A test run, played back from its first hour to its last.'; }
}

// the full frame of an hour of the run (cards, ways, every layer): shown when paused
async function showFull(i) {
  if (!R.line.length) return;
  i = Math.max(0, Math.min(R.line.length - 1, i));
  S.load = R.line[i].load;
  S.playback = false; S.nextPos = null;
  await loadFrame(R.line[i].h);
  R.shown = i;
  syncRunSlider();
  renderLog();
  renderScore();
}

// a light copy shown while playing
function showSlim(j) {
  const f = R.cache.get(j);
  if (!f) return false;
  const prevRealm = S.realm;
  S.frame = f; S.idx = indexFrame(f);
  if (!S.realm || !(f.realms && f.realms[S.realm])) S.realm = f.watched && f.realms && f.realms[f.watched] ? f.watched : f.player;
  if (S.realm !== prevRealm) fillRealms();
  S.model = buildModel();
  const n = R.cache.get(j + 1);
  S.nextPos = null;
  if (n) {
    const keep = [S.frame, S.idx];
    S.frame = n; S.idx = indexFrame(n);
    const nm = buildModel();
    [S.frame, S.idx] = keep;
    S.nextPos = new Map();
    for (const it of nm.items) S.nextPos.set(it.id, [it.x, it.y]);
  }
  if (S.selected && S.selected.type === 'party') {
    const it = S.model.items.concat(S.model.ghosts, S.model.insideMarks).find((x) => x.id === S.selected.it.id);
    if (it) S.selected.it = it;
  }
  R.shown = j;
  $('date').textContent = when(f.hours) + (f.night ? ' · night' : '');
  renderScore();
  if (performance.now() - R.logAt > 250) { R.logAt = performance.now(); renderLog(); }
  return true;
}

// ---------------------------------------------------------------- playing

function hoursAt(t) {
  const i = Math.floor(t), a = R.line[i], b = R.line[Math.min(R.line.length - 1, i + 1)];
  if (!a) return null;
  return a.h + (b && b.h - a.h <= 1 ? (t - i) * (b.h - a.h) : 0);
}

function startRunPlay() {
  if (!R.line.length) return;
  if (R.t >= R.line.length - 1) { R.t = 0; seekPointers(); }
  R.playing = true; S.playback = true; S.frac = 0; R.lastTs = 0;
  R.lastT = hoursAt(R.t);
  $('play').innerHTML = '&#10074;&#10074;'; $('play').setAttribute('aria-label', 'Pause');
  clearTimeout(R.fullTimer);
  requestAnimationFrame(tick);
}

function stopRunPlay(full = true) {
  if (!R.playing) return;
  R.playing = false;
  $('play').innerHTML = '&#9654;'; $('play').setAttribute('aria-label', 'Play');
  $('buffering').hidden = true;
  R.t = Math.floor(R.t);
  if (full) showFull(R.t); else { S.playback = false; S.nextPos = null; }
}

function tick(ts) {
  if (!R.playing) return;
  const dt = R.lastTs ? Math.min(0.25, (ts - R.lastTs) / 1000) : 0;
  R.lastTs = ts;
  const i = Math.floor(R.t);
  prefetch(i);
  if (!R.cache.has(i)) { $('buffering').hidden = false; requestAnimationFrame(tick); return; }
  // play on only as far as the hours ready (and the one after, for the movement)
  let k = i;
  while (k + 1 < R.line.length && R.cache.has(k + 1)) k++;
  const end = R.line.length - 1;
  const limit = k >= end ? end : k;
  const want = R.t + dt * R.speed;
  $('buffering').hidden = want <= limit + 0.02 || k >= end;
  R.t = Math.min(want, limit, end);
  const j = Math.floor(R.t);
  if (j !== R.shown) showSlim(j);
  S.frac = R.t - j;
  const T = hoursAt(R.t);
  if (R.lastT != null && T != null && T > R.lastT) fire(R.lastT, T);
  R.lastT = T;
  syncRunSlider();
  requestDraw();
  if (R.t >= end) { stopRunPlay(); return; }
  requestAnimationFrame(tick);
}

// what passed between two moments of the playback: the agents' decisions and the game's news
function fire(from, to) {
  while (R.decI < R.decisions.length && R.decisions[R.decI].hours <= to) {
    const e = R.decisions[R.decI++];
    if (e.hours > from) caption(e);
  }
  while (R.newsI < R.news.length && R.news[R.newsI].hours <= to) {
    const n = R.news[R.newsI++];
    if (n.hours > from) pop(n);
  }
}

function seekPointers() {
  const T = hoursAt(R.t) ?? -Infinity;
  R.decI = R.decisions.findIndex((e) => e.hours > T); if (R.decI < 0) R.decI = R.decisions.length;
  R.newsI = R.news.findIndex((e) => e.hours > T); if (R.newsI < 0) R.newsI = R.news.length;
  R.lastT = T;
}

function seek(i, full) {
  R.t = Math.max(0, Math.min(R.line.length - 1, i));
  seekPointers();
  clearOverlays();
  if (R.playing) { S.nextPos = null; if (R.cache.has(Math.floor(R.t))) showSlim(Math.floor(R.t)); syncRunSlider(); return; }
  syncRunSlider();
  clearTimeout(R.fullTimer);
  if (full) showFull(Math.floor(R.t)); else R.fullTimer = setTimeout(() => showFull(Math.floor(R.t)), 140);
}

// ---------------------------------------------------------------- the timeline

function syncRunSlider() {
  const sl = $('slider');
  sl.max = Math.max(0, R.line.length - 1);
  sl.value = Math.round(R.t);
  const at = R.line[Math.floor(R.t)];
  const day = at && R.line.length ? Math.floor((at.h - R.line[0].h) / 24) + 1 : 0;
  $('slider-label').textContent = at ? when(hoursAt(R.t), true) + ' · day ' + day : '';
  $('golive').setAttribute('aria-pressed', 'false');
  $('present-date').textContent = at ? when(hoursAt(R.t)) + ' · day ' + day : '';
}

$('slider').oninput = () => {
  if (runMode()) { seek(+$('slider').value, false); return; }
  S.live = false;
  const h = S.hours[+$('slider').value];
  if (h != null) loadFrame(h);
};

$('play').onclick = () => {
  if (runMode()) { R.playing ? stopRunPlay() : startRunPlay(); return; }
  // the live session or an earlier load: step through its hours
  if (R.liveTimer) { stopLoadPlay(); return; }
  S.live = false;
  $('play').innerHTML = '&#10074;&#10074;';
  R.liveTimer = setInterval(() => {
    const sl = $('slider');
    const i = +sl.value + 1;
    if (i >= S.hours.length) { stopLoadPlay(); if (R.source === 'live') { S.live = true; loadFrame(null); } return; }
    sl.value = i; loadFrame(S.hours[i]);
  }, Math.max(80, 1000 / R.speed));
};
function stopLoadPlay() { if (R.liveTimer) clearInterval(R.liveTimer); R.liveTimer = null; $('play').innerHTML = '&#9654;'; }

function step(d) {
  if (runMode()) { stopRunPlay(false); seek(Math.floor(R.t) + d, true); return; }
  stopLoadPlay();
  const sl = $('slider');
  const i = Math.max(0, Math.min(S.hours.length - 1, +sl.value + d));
  sl.value = i; S.live = false; loadFrame(S.hours[i]);
}
$('step-back').onclick = () => step(-1);
$('step-fwd').onclick = () => step(1);

$('golive').onclick = () => { stopLoadPlay(); switchSource('live'); };

const speedSel = $('speed');
speedSel.innerHTML = SPEEDS.map((v) => '<option value="' + v + '">' + v + ' h/s</option>').join('');
speedSel.value = String(R.speed);
speedSel.onchange = () => { R.speed = +speedSel.value; if (R.liveTimer) { stopLoadPlay(); $('play').click(); } };

// marks along the run: where each load began, the agents' decisions, the game's news
function buildMarks() {
  R.marks = [];
  if (!R.line.length) { drawMarks(); return; }
  const idxOf = (hours) => {
    let lo = 0, hi = R.line.length - 1;
    while (lo < hi) { const mid = (lo + hi) >> 1; if (R.line[mid].h < Math.floor(hours)) lo = mid + 1; else hi = mid; }
    return lo;
  };
  let prevSeg = null;
  R.line.forEach((x, i) => { if (x.seg !== prevSeg && i > 0) R.marks.push({ i, kind: 'load', text: 'A load of the game: ' + (x.seg.how || '') }); prevSeg = x.seg; });
  for (const e of R.decisions) R.marks.push({ i: idxOf(e.hours), kind: 'decision', realm: e.realm, text: realmName(e.realm) + ': ' + (e.kind === 'council' ? 'strategy review' : 'answer') + ', ' + ((e.orders || []).length) + ' orders' });
  for (const n of R.news) R.marks.push({ i: idxOf(n.hours), kind: MAJOR.has(n.kind) ? 'major' : 'minor', text: (NEWS_LABEL[n.kind] || n.kind) + ': ' + n.text });
  drawMarks();
}

function drawMarks() {
  const c = $('marks');
  const dpr = window.devicePixelRatio || 1, w = c.clientWidth, h = c.clientHeight;
  c.width = Math.max(1, Math.round(w * dpr)); c.height = Math.max(1, Math.round(h * dpr));
  const g = c.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, w, h);
  if (!runMode() || R.line.length < 2) return;
  const x = (i) => 7 + (w - 14) * (i / (R.line.length - 1));
  for (const m of R.marks) {
    if (m.kind === 'load') { g.fillStyle = token('--ink-3'); g.fillRect(x(m.i) - 0.5, 0, 1, h); }
    else if (m.kind === 'decision') { g.fillStyle = factionColour(m.realm); g.fillRect(x(m.i) - 1, 0, 2, h * 0.45); }
    else if (m.kind === 'major') { g.fillStyle = C.enemy; g.fillRect(x(m.i) - 1, h * 0.5, 2, h * 0.5); }
    else { g.fillStyle = token('--ink-3'); g.globalAlpha = 0.5; g.fillRect(x(m.i) - 0.5, h * 0.7, 1, h * 0.3); g.globalAlpha = 1; }
  }
}
window.addEventListener('resize', () => drawMarks());

$('marks').addEventListener('mousemove', (e) => {
  if (!runMode() || R.line.length < 2) return;
  const c = $('marks'), r = c.getBoundingClientRect();
  const i = Math.round(((e.clientX - r.left - 7) / (r.width - 14)) * (R.line.length - 1));
  let best = null, bd = 1e9;
  for (const m of R.marks) { const d = Math.abs(m.i - i); if (d < bd) { bd = d; best = m; } }
  const at = R.line[Math.max(0, Math.min(R.line.length - 1, i))];
  c.title = (at ? when(at.h, true) : '') + (best && bd <= Math.max(1, R.line.length / 200) ? '\n' + best.text : '');
});
$('marks').addEventListener('click', (e) => {
  if (!runMode() || R.line.length < 2) return;
  const r = $('marks').getBoundingClientRect();
  seek(Math.round(((e.clientX - r.left - 7) / (r.width - 14)) * (R.line.length - 1)), true);
});

// ---------------------------------------------------------------- the side log of the run

async function refreshRunLog() {
  if (!runMode() || !R.run) return;
  const agents = Object.entries(R.run.who || {}).filter(([, w]) => w === 'agent').map(([k]) => k);
  const realm = S.realm && (R.run.who || {})[S.realm] ? S.realm : agents[0];
  try {
    const d = await getJSON('/api/runlog?id=' + encodeURIComponent(R.run.id) + '&realm=' + encodeURIComponent(realm || ''));
    S.log = d.entries || [];
    $('log-title').textContent = 'Moves of ' + (realmName(realm) || 'the agent') + ' in the run';
    renderLog();
  } catch (e) { /* asked again at the next change */ }
}
$('realm').addEventListener('change', () => { if (runMode()) refreshRunLog(); });

function realmName(id) {
  const f = S.idx && S.idx.factions.get(id);
  return f ? f.name : (S.frame && S.frame.realms && S.frame.realms[id] ? S.frame.realms[id].name : id);
}

// ---------------------------------------------------------------- captions: the agents' decisions as they happen

function partyOf(id) {
  if (!id || !S.idx) return null;
  return S.idx.parties.get(id) || S.idx.parties.get(S.idx.heroes.get(id)) || null;
}

function caption(e) {
  const orders = e.orders || [];
  if (!orders.length && e.kind !== 'council' && !e.plan) return;   // an answer that ordered nothing tells a demo nothing
  const ids = new Set();
  for (const o of orders) for (const id of [o.lord].concat(o.lords || [])) { const p = partyOf(id); if (p) ids.add(p.id); }
  R.captions.push({ e, ids, t0: performance.now() });
  while (R.captions.length > 2) R.captions.shift();
  renderCaptions();
}

function renderCaptions() {
  const box = $('captions');
  const now = performance.now();
  R.captions = R.captions.filter((c) => now - c.t0 < CAPTION_MS);
  box.innerHTML = R.captions.map((c) => {
    const e = c.e;
    const orders = (e.orders || []);
    const line = (o) => '<li><strong>' + esc(nameOf(o.lord)) + '</strong> ' + esc(orderWords(o.do)) + (o.at ? ' ' + esc(nameOf(o.at)) : '') + (o.target ? ' ' + esc(nameOf(o.target)) : '')
      + (o.lords && o.lords.length ? ' <span class="muted">with ' + o.lords.length + (o.lords.length === 1 ? ' other' : ' others') + '</span>' : '')
      + (o.why ? '<span class="why">' + esc(fixText(o.why)) + '</span>' : '') + '</li>';
    const plan = e.plan ? '<p class="cap-plan">' + esc(e.plan.length > 360 ? e.plan.slice(0, 357) + '…' : e.plan) + '</p>' : '';
    return '<article class="caption" style="--realm:' + esc(factionColour(e.realm)) + '">'
      + '<header><span class="swatch"></span><span class="cap-realm">' + esc(realmName(e.realm)) + '</span><span class="cap-kind">' + (e.kind === 'council' ? 'strategy review' : 'orders') + '</span>'
      + '<span class="cap-when">' + esc(when(e.hours, true)) + '</span></header>' + plan
      + (orders.length ? '<ul>' + orders.slice(0, 6).map(line).join('') + (orders.length > 6 ? '<li class="muted">and ' + (orders.length - 6) + ' more orders</li>' : '') + '</ul>' : '')
      + '</article>';
  }).join('');
}

// an order's name in words: home_recruit -> home recruit at, besiege -> besiege
function orderWords(d) {
  const w = String(d || '?').replace(/_/g, ' ');
  return { 'home recruit': 'recruit around', supply: 'supply', garrison: 'garrison', defend: 'defend', harass: 'harass', besiege: 'besiege', reserve: 'stand in reserve for',
           join: 'join', follow: 'follow', go: 'go to', patrol: 'patrol around', raid: 'raid', attack: 'attack', sell: 'sell at', rest: 'rest at', recruit: 'recruit at' }[w] || w;
}

// ---------------------------------------------------------------- pop-ups: the game's news where it happened

const BATTLE_RE = /^Battle: (.+?) \((.+?)\) against (.+?) \((.+?)\); (.+?) won\./;

function placeByName(name) {
  if (!S.frame || !name) return null;
  return S.frame.settlements.find((s) => s.name === name) || null;
}
function partyByName(name) {
  if (!S.frame || !name) return null;
  return S.frame.parties.find((p) => p.name === name && p.kind === 'lord') || S.frame.parties.find((p) => p.name === name) || null;
}

function whereOf(n) {
  if (n.place) { const s = S.idx.places.get(n.place); if (s) return [s.x, s.y]; }
  if (n.kind === 'battle') {
    const m = BATTLE_RE.exec(n.text);
    if (m) {
      const names = [m[1], m[3]];
      for (const b of S.frame.battles || []) {
        const a = partyOf(b.attackers.leader), d = partyOf(b.defenders.leader) || S.idx.places.get(b.defenders.leader);
        if ((a && names.includes(a.name)) || (d && names.includes(d.name))) return [b.x, b.y];
      }
      for (const nm of names) { const p = partyByName(nm); if (p) return [p.x, p.y]; }
    }
  }
  if (n.party) { const p = partyOf(n.party); if (p) return [p.x, p.y]; }
  if (n.kind === 'captured') { const m = /taken prisoner by (.+?)\.?$/.exec(n.text); const p = m && partyByName(m[1]); if (p) return [p.x, p.y]; }
  if (n.kind === 'looted') { const m = /^(.+?) was looted/.exec(n.text); const s = m && placeByName(m[1]); if (s) return [s.x, s.y]; }
  if (n.kind === 'holding_changed') { const m = /^(.+?) passed from/.exec(n.text); const s = m && placeByName(m[1]); if (s) return [s.x, s.y]; }
  return null;
}

function pop(n) {
  const at = whereOf(n);
  const major = MAJOR.has(n.kind);
  if (at) R.pops.push({ n, x: at[0], y: at[1], t0: performance.now(), major });
  if (major || n.kind === 'letter' || n.kind === 'alliance' || n.kind === 'army_formed') {
    R.toasts.unshift({ n, t0: performance.now(), major });
    R.toasts = R.toasts.slice(0, 5);
    renderToasts();
  }
}

function renderToasts() {
  const now = performance.now();
  R.toasts = R.toasts.filter((t) => now - t.t0 < TOAST_MS);
  $('toasts').innerHTML = R.toasts.map((t) => '<div class="toast ' + (t.major ? 'major' : '') + ' k-' + esc(t.n.kind) + '"><span class="toast-kind">' + esc(NEWS_LABEL[t.n.kind] || t.n.kind)
    + '</span> <span class="toast-when">' + esc(when(t.n.hours, true)) + '</span><div>' + esc(toastText(t.n)) + '</div></div>').join('');
}

// a battle's news says who fought at length: its first sentence is enough for a pop-up
function toastText(n) {
  let t = n.text || '';
  if (n.kind === 'battle') { const m = BATTLE_RE.exec(t); if (m) t = m[1] + ' (' + m[2] + ') against ' + m[3] + ' (' + m[4] + '); ' + m[5] + ' won.'; }
  return t.length > 260 ? t.slice(0, 257) + '…' : t;
}

function clearOverlays() {
  R.captions = []; R.pops = []; R.toasts = [];
  $('captions').innerHTML = ''; $('toasts').innerHTML = '';
}

// drawn by app.js's draw(), last: the pop-ups' rings and the lords a caption names
function drawReplay(g) {
  const now = performance.now();
  let alive = false;
  for (const c of R.captions) {
    if (now - c.t0 >= CAPTION_MS) continue;
    alive = true;
    const pulse = 0.5 + 0.5 * Math.sin((now - c.t0) / 180);
    g.save();
    g.strokeStyle = factionColour(c.e.realm); g.lineWidth = 2.2; g.globalAlpha = 0.55 + 0.45 * pulse;
    for (const it of (S.model ? S.model.items.concat(S.model.insideMarks) : [])) {
      if (!c.ids.has(it.id)) continue;
      const [px, py] = it.inside ? [it.x, it.y] : lerpPos(it);
      g.beginPath(); g.arc(sx(px), sy(py), partyRadius(it) + 6 + 3 * pulse, 0, Math.PI * 2); g.stroke();
    }
    g.restore();
  }
  R.pops = R.pops.filter((p) => now - p.t0 < POP_MS);
  for (const p of R.pops) {
    alive = true;
    const a = (now - p.t0) / POP_MS, x = sx(p.x), y = sy(p.y);
    const col = p.n.kind === 'raided' || p.n.kind === 'looted' ? C.warn : p.major ? C.enemy : C.accent;
    g.save();
    for (let k = 0; k < 2; k++) {
      const ak = (a * 2 + k * 0.5) % 1;
      g.globalAlpha = (1 - ak) * (p.major ? 0.95 : 0.6);
      g.beginPath(); g.arc(x, y, 6 + ak * (p.major ? 46 : 26), 0, Math.PI * 2); g.lineWidth = p.major ? 3 : 2; g.strokeStyle = col; g.stroke();
    }
    g.globalAlpha = Math.min(1, (1 - a) * 3);
    drawNewsIcon(g, x, y, p.n.kind, col);
    if (p.major && S.view.s > 0.35) {
      const label = (NEWS_LABEL[p.n.kind] || p.n.kind) + ': ' + shortText(p.n);
      g.font = '600 12.5px ' + token('--font-ui');
      const tw = g.measureText(label).width;
      g.fillStyle = 'rgba(20,24,22,.82)'; g.fillRect(x + 14, y - 26, tw + 12, 20);
      g.fillStyle = '#fff'; g.fillText(label, x + 20, y - 12);
    }
    g.restore();
  }
  if (now - (R._toastAt || 0) > 500) { R._toastAt = now; renderToasts(); renderCaptions(); }
  if (alive && !R.playing) requestDraw();
}

function shortText(n) {
  const t = n.text || '';
  if (n.kind === 'battle') { const m = BATTLE_RE.exec(t); if (m) return m[1] + ' against ' + m[3] + ', ' + m[5] + ' won'; }
  return t.length > 70 ? t.slice(0, 67) + '…' : t;
}

function drawNewsIcon(g, x, y, kind, col) {
  g.save();
  g.fillStyle = 'rgba(255,250,240,.95)'; g.beginPath(); g.arc(x, y, 9, 0, Math.PI * 2); g.fill();
  g.lineWidth = 2; g.strokeStyle = col; g.stroke();
  g.strokeStyle = '#1b1f1d'; g.fillStyle = '#1b1f1d'; g.lineWidth = 1.8; g.lineCap = 'round';
  g.beginPath();
  if (kind === 'battle') { g.moveTo(x - 4.5, y - 4.5); g.lineTo(x + 4.5, y + 4.5); g.moveTo(x + 4.5, y - 4.5); g.lineTo(x - 4.5, y + 4.5); g.stroke(); }
  else if (kind === 'siege_started' || kind === 'siege_ended' || kind === 'holding_changed') {
    g.moveTo(x - 5, y + 4); g.lineTo(x - 5, y - 2); g.lineTo(x - 3, y - 2); g.lineTo(x - 3, y - 4); g.lineTo(x - 1, y - 4); g.lineTo(x - 1, y - 2); g.lineTo(x + 1, y - 2);
    g.lineTo(x + 1, y - 4); g.lineTo(x + 3, y - 4); g.lineTo(x + 3, y - 2); g.lineTo(x + 5, y - 2); g.lineTo(x + 5, y + 4); g.closePath(); g.fill();
  } else if (kind === 'captured') { g.arc(x - 2, y, 2.6, 0, Math.PI * 2); g.moveTo(x + 4.6, y); g.arc(x + 2, y, 2.6, 0, Math.PI * 2); g.stroke(); }
  else if (kind === 'raided' || kind === 'looted') { g.moveTo(x, y - 5); g.quadraticCurveTo(x + 5, y, x, y + 5); g.quadraticCurveTo(x - 5, y, x, y - 5); g.fill(); }
  else if (kind === 'lord_killed') { g.moveTo(x, y - 5); g.lineTo(x, y + 5); g.moveTo(x - 3.5, y - 2); g.lineTo(x + 3.5, y - 2); g.stroke(); }
  else { g.arc(x, y, 3, 0, Math.PI * 2); g.fill(); }
  g.restore();
}

// ---------------------------------------------------------------- the scoreboard and presentation mode

function renderScore() {
  const box = $('score');
  if (!R.run || !S.frame) { box.innerHTML = ''; return; }
  const realms = Object.keys(R.run.who || {});
  const rows = realms.map((id) => {
    let strength = 0, lords = 0;
    for (const p of S.frame.parties) {
      if (p.kind !== 'lord' || p.faction !== id) continue;
      lords++;
      strength += p.army_leader === p.id && p.army_strength ? p.army_strength : (p.army_leader && p.army_leader !== p.id ? 0 : (p.strength || 0));
    }
    const holds = S.frame.settlements.filter((s) => s.faction === id && (s.kind === 'town' || s.kind === 'castle')).length;
    const who = R.run.who[id] === 'agent' ? 'agent' : R.run.who[id] === 'you' ? 'human' : R.run.who[id];
    return '<tr style="--realm:' + esc(factionColour(id)) + '"><th><span class="swatch"></span>' + esc(realmName(id)) + ' <span class="muted">' + esc(who) + '</span></th>'
      + '<td>' + N(holds) + '</td><td>' + N(lords) + '</td><td>' + N(strength) + '</td></tr>';
  }).join('');
  box.innerHTML = '<table><tr><th></th><th>Holdings</th><th>Lords</th><th>Strength</th></tr>' + rows + '</table>';
}

function setPresent(on) {
  R.present = on;
  document.body.classList.toggle('presenting', on);
  $('present').setAttribute('aria-pressed', on ? 'true' : 'false');
  $('score').hidden = !on || !runMode();
  $('present-title').hidden = !on;
  $('present-run').textContent = R.run ? R.run.save + ' · run ' + R.run.id : 'The live session';
  renderScore();
  setTimeout(() => { resize(); drawMarks(); }, 30);
  poke();
}
$('present').onclick = () => setPresent(!R.present);

// in presentation mode the timeline hides itself while the mouse rests
function poke() {
  document.body.classList.remove('idle');
  clearTimeout(R.idleTimer);
  if (R.present) R.idleTimer = setTimeout(() => document.body.classList.add('idle'), 2500);
}
document.addEventListener('mousemove', poke);

document.addEventListener('keydown', (e) => {
  if (e.target && (e.target.tagName === 'INPUT' && e.target.type !== 'range' || e.target.tagName === 'SELECT' || e.target.tagName === 'TEXTAREA')) return;
  if (e.key === 'p' || e.key === 'P') { setPresent(!R.present); e.preventDefault(); }
  else if (e.key === 'Escape' && R.present) setPresent(false);
  else if (e.key === ' ' && runMode()) { R.playing ? stopRunPlay() : startRunPlay(); e.preventDefault(); }
  else if ((e.key === '.' || e.key === ',') && runMode()) { step(e.key === '.' ? 1 : -1); e.preventDefault(); }
});

// clicking the map while a run plays pauses it, so the card is the full hour's
$('map').addEventListener('pointerdown', () => { R._selBefore = S.selected; });
$('map').addEventListener('pointerup', () => { if (R.playing && S.selected && S.selected !== R._selBefore) stopRunPlay(); });

// ---------------------------------------------------------------- start: links can open a run (#run=<id>&at=<hour index>&speed=12&present=1&play=1)

(async () => {
  if (HASH.get('run')) { R.source = 'run:' + HASH.get('run'); S.live = false; document.body.classList.add('run-mode'); }
  await refreshRuns();
  setInterval(refreshRuns, 60000);
  const sp = +(HASH.get('speed') || 0);
  if (SPEEDS.includes(sp)) { R.speed = sp; speedSel.value = String(sp); }
  const run = HASH.get('run');
  if (run) {
    R.source = 'run:' + run;
    document.body.classList.add('run-mode');
    S.live = false;
    await openRun(run, +(HASH.get('at') || 0));
    fillSources(S.status);
    if (HASH.get('present') === '1') setPresent(true);
    if (HASH.get('play') === '1') startRunPlay();
  }
})();
