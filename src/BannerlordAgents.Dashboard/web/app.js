'use strict';
// War Map: the dashboard's page. It asks the dashboard program for readings (the hourly frames the bridge writes, the walking
// map, the crossing atlas, the agent's log) and draws them. Nothing here talks to the game or to the agent.

const $ = (id) => document.getElementById(id);
const canvas = $('map');
const ctx = canvas.getContext('2d');
const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

const S = {
  status: null, cal: null,
  frame: null, idx: null,           // the frame shown, and its lookups
  campaign: null, load: null, hours: [], live: true, playing: null,
  realm: null, mode: 'sees',
  layers: { picture: true, labels: true, villages: true, small: true, trails: true, crossings: true, allways: false, truth: false },
  map: null, img: null, atlas: null, mapCrc: null,
  view: { s: 1, ox: 0, oy: 0, fitted: false },
  model: null, prev: null, animStart: 0,
  selected: null, way: null, asked: null, picking: false,
  log: [], logKey: null, kinds: new Set(['woke', 'answer', 'council', 'no_answer', 'carried', 'refused', 'checked', 'ended', 'report', 'asked']),
  hover: null, dpr: 1,
};

const KINDS = [
  ['woke', 'Woken'], ['answer', 'Answers'], ['council', 'Councils'], ['carried', 'Carried out'], ['refused', 'Refused'],
  ['ended', 'Ended'], ['report', 'Reported back'], ['asked', 'Asked the way'],
];
const KIND_LABEL = { woke: 'Woken', answer: 'Answer', council: 'Council', no_answer: 'No answer', carried: 'Carried out', refused: 'Refused', checked: 'Checked', ended: 'Ended', report: 'Reported', asked: 'Asked the way' };

// ---------------------------------------------------------------- readings

async function getJSON(url, opts) {
  const r = await fetch(url, opts);
  const body = await r.json().catch(() => ({}));
  if (!r.ok) throw new Error(body.error || ('HTTP ' + r.status));
  return body;
}

function token(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || '#888'; }
let C = {};
function readColours() {
  C = { own: token('--own'), enemy: token('--enemy'), ally: token('--ally'), neutral: token('--neutral'), cut: token('--cut'), accent: token('--accent'),
        ink: token('--ink'), panel: token('--panel'), warn: token('--warn'), good: token('--good') };
}
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { readColours(); buildLegend(); requestDraw(); });

// ---------------------------------------------------------------- the calendar

function when(hours, short) {
  if (hours == null || isNaN(hours)) return '?';
  const c = S.cal || { hours_in_day: 24, days_in_season: 21, seasons_in_year: 4, season_names: ['Spring', 'Summer', 'Autumn', 'Winter'] };
  const h = Math.floor(hours), hid = c.hours_in_day || 24, dis = c.days_in_season || 21, siy = c.seasons_in_year || 4;
  const days = Math.floor(h / hid), hour = h % hid, diy = dis * siy, year = Math.floor(days / diy), doy = days % diy;
  const season = Math.floor(doy / dis), day = doy % dis;
  const name = (c.season_names && c.season_names[season]) || ('Season ' + (season + 1));
  const hh = String(hour).padStart(2, '0') + ':00';
  return short ? name + ' ' + (day + 1) + ', ' + hh : name + ' ' + (day + 1) + ', ' + year + ', ' + hh;
}
const N = (n) => n == null || isNaN(n) ? '?' : Math.round(n).toLocaleString('en-US');
const D1 = (n) => n == null || isNaN(n) ? '?' : (Math.round(n * 10) / 10).toFixed(1);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

// ---------------------------------------------------------------- the frame and what each realm saw

function indexFrame(f) {
  const parties = new Map(), places = new Map(), factions = new Map(), heroes = new Map(), inside = new Map();
  for (const p of f.parties || []) {
    parties.set(p.id, p);
    if (p.leader && p.kind === 'lord') heroes.set(p.leader, p.id);
    if (p.in_settlement && p.kind === 'lord' && !p.attached) {
      if (!inside.has(p.in_settlement)) inside.set(p.in_settlement, []);
      inside.get(p.in_settlement).push(p);
    }
  }
  for (const s of f.settlements || []) places.set(s.id, s);
  for (const x of f.factions || []) factions.set(x.id, x);
  return { parties, places, factions, heroes, inside };
}

function relationTo(realm, faction, kind) {
  if (!realm) return 'neutral';
  if (faction === realm) return 'own';
  if (kind === 'bandit') return 'enemy';
  const fr = S.idx.factions.get(realm);
  if (fr && (fr.at_war || []).includes(faction)) return 'enemy';
  if (fr && (fr.allies || []).includes(faction)) return 'ally';
  return 'neutral';
}

function factionColour(id) {
  const f = S.idx && id ? S.idx.factions.get(id) : null;
  const c = f && f.color && f.color !== '#000000' ? f.color : '#8a8f8a';
  return c;
}

function doingText(p) {
  const place = (id) => (S.idx.places.get(id) || {}).name || 'somewhere';
  const party = (id) => (S.idx.parties.get(id) || {}).name || 'a party';
  if (p.raiding) return 'raiding ' + place(p.raiding);
  if (p.in_battle) return 'fighting';
  if (p.besieging) return 'besieging ' + place(p.besieging);
  if (p.in_settlement) return 'inside ' + place(p.in_settlement);
  switch (p.doing) {
    case 'GoToSettlement': return 'going to ' + place(p.target_settlement);
    case 'BesiegeSettlement': return 'marching to besiege ' + place(p.target_settlement);
    case 'AssaultSettlement': return 'assaulting ' + place(p.target_settlement);
    case 'RaidSettlement': return 'on the way to raid ' + place(p.target_settlement);
    case 'DefendSettlement': return 'defending ' + place(p.target_settlement);
    case 'PatrolAroundPoint': return p.target_settlement ? 'patrolling around ' + place(p.target_settlement) : 'patrolling';
    case 'EngageParty': return 'chasing ' + party(p.target_party);
    case 'EscortParty': return 'following ' + party(p.target_party);
    case 'JoinParty': return 'joining ' + party(p.target_party);
    case 'GoAroundParty': case 'FleeToPoint': case 'FleeToGate': case 'FleeToParty': return 'fleeing';
    case 'GoToPoint': return 'on the move';
    case 'Hold': return 'holding position';
    default: return 'idle';
  }
}

function towardOf(p) {
  if (p.raiding) return p.raiding;
  if (p.in_battle || p.in_settlement) return null;
  if (p.besieging) return p.besieging;
  if (['GoToSettlement', 'BesiegeSettlement', 'AssaultSettlement', 'RaidSettlement', 'DefendSettlement', 'PatrolAroundPoint'].includes(p.doing)) return p.target_settlement || null;
  return null;
}

// The items to draw for the realm followed, in the mode chosen. Each party item keeps the view's own entry (what the realm
// knows) apart from the frame's row (the truth), so the card can tell them apart.
function buildModel() {
  const f = S.frame;
  if (!f) return null;
  const R = S.realm, rf = R && f.realms ? f.realms[R] : null;
  const items = [], ghosts = [], trails = [], insideMarks = [];
  const add = (it) => items.push(it);
  const fromRow = (p, extra) => Object.assign({
    type: 'party', id: p.id, x: p.x, y: p.y, kind: p.kind, rel: relationTo(R, p.faction, p.kind), row: p, vp: null, name: p.name || kindName(p.kind),
    men: p.army_men || p.men, strength: p.army_strength || p.strength, army: p.army_leader === p.id && p.army_men > 0, battle: !!p.in_battle,
    held: rf && rf.orders && rf.orders[p.id] ? rf.orders[p.id] : null, faction: p.faction,
  }, extra || {});
  const fromView = (vp, extra) => Object.assign({
    type: 'party', id: vp.id, x: vp.x, y: vp.y, kind: vp.kind, rel: vp.relation || 'neutral', row: S.idx.parties.get(vp.id) || null, vp,
    name: vp.name, men: vp.army_men || vp.men, strength: vp.strength, army: vp.army_men > 0, battle: (vp.doing || '') === 'fighting', faction: vp.faction,
  }, extra || {});

  if (S.mode === 'truth' || !rf) {
    for (const p of f.parties) {
      if (p.attached || p.kind === 'garrison' || p.kind === 'militia') continue;
      if (p.in_settlement) continue;
      if (!S.layers.small && (p.kind === 'caravan' || p.kind === 'villager' || p.kind === 'bandit' || p.kind === 'other')) continue;
      add(fromRow(p));
    }
  } else {
    const shown = new Set();
    for (const p of f.parties) {
      if (p.faction !== R || p.kind !== 'lord' || p.attached || p.in_settlement) continue;
      add(fromRow(p, { rel: 'own' })); shown.add(p.id);
    }
    for (const vp of rf.view.seen || []) {
      if (shown.has(vp.id)) continue;
      const row = S.idx.parties.get(vp.id);
      if (row && row.in_settlement) continue;
      add(fromView(vp)); shown.add(vp.id);
    }
    for (const id of rf.cut || []) {
      const p = S.idx.parties.get(id);
      if (!p || shown.has(id) || p.in_settlement) continue;
      add(fromRow(p, { cut: true })); shown.add(id);
    }
    if (S.layers.small) for (const id of rf.counted_only || []) {
      const p = S.idx.parties.get(id);
      if (!p || shown.has(id) || p.in_settlement) continue;
      add(fromRow(p, { counted: true })); shown.add(id);
    }
    for (const vp of rf.view.last_seen || []) ghosts.push(fromView(vp, { ghost: true, ago: f.hours - vp.seen_hours }));
    for (const vp of rf.remembered || []) ghosts.push(fromView(vp, { ghost: true, cut: true, ago: f.hours - vp.seen_hours }));
    for (const t of rf.view.trails || []) trails.push(t);
  }
  // lords inside a holding: small marks around it (the realm's own in its own view; everyone's in the truth)
  for (const [sid, list] of S.idx.inside) {
    const s = S.idx.places.get(sid);
    if (!s) continue;
    const mine = list.filter((p) => S.mode === 'truth' || !rf || p.faction === R || (rf.in_sight || []).includes(p.id));
    mine.forEach((p, k) => insideMarks.push(fromRow(p, { inside: sid, slot: k, of: mine.length, x: s.x, y: s.y })));
  }
  const truth = [];
  if (S.layers.truth && S.mode === 'sees' && rf) {
    for (const p of f.parties) if (!p.attached && !p.in_settlement && p.kind === 'lord' && p.faction !== R) truth.push(p);
  }
  return { items, ghosts, trails, insideMarks, truth, rf };
}

function kindName(k) { return { lord: 'Lord', caravan: 'Caravan', villager: 'Villagers', bandit: 'Bandits', garrison: 'Garrison', militia: 'Militia' }[k] || 'Party'; }

// ---------------------------------------------------------------- the view: map units to the screen (north up)

function bounds() {
  if (S.map) return { x0: S.map.meta.min_x, y0: S.map.meta.min_y, x1: S.map.meta.max_x, y1: S.map.meta.max_y };
  const ss = S.frame ? S.frame.settlements : [];
  if (!ss.length) return { x0: 0, y0: 0, x1: 1000, y1: 1000 };
  return { x0: Math.min(...ss.map((s) => s.x)) - 20, y0: Math.min(...ss.map((s) => s.y)) - 20, x1: Math.max(...ss.map((s) => s.x)) + 20, y1: Math.max(...ss.map((s) => s.y)) + 20 };
}
const B = () => S._b || (S._b = bounds());
function sx(x) { return (x - B().x0) * S.view.s + S.view.ox; }
function sy(y) { return (B().y1 - y) * S.view.s + S.view.oy; }
function wx(px) { return (px - S.view.ox) / S.view.s + B().x0; }
function wy(py) { return B().y1 - (py - S.view.oy) / S.view.s; }

function fit(box) {
  const w = canvas.clientWidth, h = canvas.clientHeight;
  const bw = Math.max(20, box.x1 - box.x0), bh = Math.max(20, box.y1 - box.y0);
  S.view.s = Math.min(w / bw, h / bh) * 0.9;
  S.view.ox = w / 2 - (((box.x0 + box.x1) / 2) - B().x0) * S.view.s;
  S.view.oy = h / 2 - (B().y1 - (box.y0 + box.y1) / 2) * S.view.s;
}

function fitRealm() {
  const f = S.frame;
  if (!f) return;
  const own = f.settlements.filter((s) => s.faction === S.realm && s.kind !== 'village');
  const pts = own.length ? own : f.settlements;
  if (!pts.length) return;
  const box = { x0: Math.min(...pts.map((p) => p.x)) - 40, x1: Math.max(...pts.map((p) => p.x)) + 40, y0: Math.min(...pts.map((p) => p.y)) - 40, y1: Math.max(...pts.map((p) => p.y)) + 40 };
  fit(box);
  S.view.fitted = true;
}

function centreOn(x, y, zoom) {
  if (zoom) S.view.s = Math.max(S.view.s, zoom);
  S.view.ox = canvas.clientWidth / 2 - (x - B().x0) * S.view.s;
  S.view.oy = canvas.clientHeight / 2 - (B().y1 - y) * S.view.s;
  requestDraw();
}

// ---------------------------------------------------------------- drawing

let drawQueued = false;
function requestDraw() { if (!drawQueued) { drawQueued = true; requestAnimationFrame(draw); } }

function resize() {
  S.dpr = window.devicePixelRatio || 1;
  const r = canvas.getBoundingClientRect();
  canvas.width = Math.max(1, Math.round(r.width * S.dpr));
  canvas.height = Math.max(1, Math.round(r.height * S.dpr));
  requestDraw();
}
window.addEventListener('resize', resize);

function relColour(rel) { return C[rel] || C.neutral; }

function partyRadius(it) {
  if (it.kind === 'lord') {
    if (it.army) return Math.max(7, Math.min(15, 5 + 3.2 * Math.log10(Math.max(100, it.strength || 100) / 100)));
    return 5;
  }
  if (it.kind === 'bandit') return 3.5;
  if (it.kind === 'caravan') return 3.2;
  return 2.4;
}

// one icon, at screen (x, y): used by the map and by the legend alike
function drawPartyIcon(g, x, y, it, alpha) {
  const r = partyRadius(it);
  g.save();
  g.globalAlpha = alpha ?? 1;
  const fill = it.kind === 'lord' ? factionColour(it.faction) : it.kind === 'bandit' ? '#3b2a22' : it.kind === 'caravan' ? '#8a6a3c' : '#6f7d6a';
  if (it.kind === 'lord') {
    g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2);
    g.fillStyle = fill; g.fill();
    g.lineWidth = it.army ? 3 : 2.2; g.strokeStyle = relColour(it.rel);
    if (it.ghost) g.setLineDash([3, 2]);
    g.stroke(); g.setLineDash([]);
    if (it.army) { g.beginPath(); g.arc(x, y, r + 3, 0, Math.PI * 2); g.lineWidth = 1.2; g.stroke(); }
  } else if (it.kind === 'bandit') {
    g.beginPath(); g.moveTo(x, y - r - 1); g.lineTo(x + r + 1, y + r); g.lineTo(x - r - 1, y + r); g.closePath();
    g.fillStyle = fill; g.fill(); g.lineWidth = 1; g.strokeStyle = relColour(it.rel); g.stroke();
  } else if (it.kind === 'caravan') {
    g.fillStyle = fill; g.fillRect(x - r, y - r, r * 2, r * 2); g.lineWidth = 1; g.strokeStyle = relColour(it.rel); g.strokeRect(x - r, y - r, r * 2, r * 2);
  } else {
    g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2); g.fillStyle = fill; g.fill();
  }
  if (it.cut) { g.beginPath(); g.arc(x, y, r + 5, 0, Math.PI * 2); g.setLineDash([2, 2]); g.lineWidth = 1.6; g.strokeStyle = C.cut; g.stroke(); g.setLineDash([]); }
  if (it.counted) { g.beginPath(); g.arc(x, y, r + 2.5, 0, Math.PI * 2); g.lineWidth = 1; g.strokeStyle = C.neutral; g.stroke(); }
  if (it.battle) {
    g.strokeStyle = C.enemy; g.lineWidth = 1.8;
    const bx = x + r + 2, by = y - r - 2;
    g.beginPath(); g.moveTo(bx - 3, by - 3); g.lineTo(bx + 3, by + 3); g.moveTo(bx + 3, by - 3); g.lineTo(bx - 3, by + 3); g.stroke();
  }
  if (it.held) {
    const hx = x + r - 1, hy = y + r - 1;
    g.fillStyle = '#ffffff'; g.fillRect(hx, hy, 5, 5); g.lineWidth = 1.2; g.strokeStyle = C.accent; g.strokeRect(hx, hy, 5, 5);
  }
  g.restore();
}

function drawPlaceIcon(g, x, y, s, extra) {
  const fill = factionColour(s.faction);
  g.save();
  if (s.kind === 'town') {
    const r = 6.5;
    g.fillStyle = fill; g.fillRect(x - r, y - r, r * 2, r * 2);
    g.lineWidth = 1.5; g.strokeStyle = '#1b1f1d'; g.strokeRect(x - r, y - r, r * 2, r * 2);
    g.strokeStyle = 'rgba(255,255,255,.85)'; g.lineWidth = 1; g.strokeRect(x - r + 2, y - r + 2, r * 2 - 4, r * 2 - 4);
  } else if (s.kind === 'castle') {
    const r = 6.5;
    g.beginPath(); g.moveTo(x, y - r); g.lineTo(x + r, y); g.lineTo(x, y + r); g.lineTo(x - r, y); g.closePath();
    g.fillStyle = fill; g.fill(); g.lineWidth = 1.5; g.strokeStyle = '#1b1f1d'; g.stroke();
  } else if (s.kind === 'hideout') {
    g.beginPath(); g.moveTo(x, y - 4); g.lineTo(x + 4, y + 3); g.lineTo(x - 4, y + 3); g.closePath(); g.fillStyle = '#2b2522'; g.fill();
  } else {
    g.beginPath(); g.arc(x, y, 3, 0, Math.PI * 2); g.fillStyle = fill; g.fill(); g.lineWidth = 1; g.strokeStyle = '#1b1f1d'; g.stroke();
  }
  if (s.besieged_by) { g.beginPath(); g.arc(x, y, 11, 0, Math.PI * 2); g.setLineDash([3, 2]); g.lineWidth = 2; g.strokeStyle = C.warn; g.stroke(); g.setLineDash([]); }
  if (s.village_state === 'BeingRaided') { g.beginPath(); g.moveTo(x, y - 9); g.lineTo(x + 3.5, y - 3.5); g.lineTo(x - 3.5, y - 3.5); g.closePath(); g.fillStyle = C.warn; g.fill(); }
  if (s.village_state === 'Looted') { g.strokeStyle = '#2a2a2a'; g.lineWidth = 1.4; g.beginPath(); g.moveTo(x - 3, y - 3); g.lineTo(x + 3, y + 3); g.moveTo(x + 3, y - 3); g.lineTo(x - 3, y + 3); g.stroke(); }
  if (extra && extra.own) { g.beginPath(); g.arc(x, y, s.kind === 'village' ? 5.5 : 9.5, 0, Math.PI * 2); g.lineWidth = 1.2; g.strokeStyle = C.own; g.stroke(); }
  g.restore();
}

function drawCrossingIcon(g, x, y, c, big) {
  const k = big ? 1.4 : 1;
  g.save();
  g.lineWidth = 2; g.strokeStyle = c.kind === 'bridge' ? '#7a2a18' : c.kind === 'ford' ? '#a65f10' : '#4a3d33';
  g.fillStyle = 'rgba(255,248,235,.92)';
  g.beginPath(); g.arc(x, y, 7 * k, 0, Math.PI * 2); g.fill();
  if (c.critical) { g.lineWidth = 2.2; g.strokeStyle = C.accent; g.stroke(); g.strokeStyle = c.kind === 'bridge' ? '#7a2a18' : c.kind === 'ford' ? '#a65f10' : '#4a3d33'; }
  g.lineWidth = 1.6;
  g.beginPath();
  if (c.kind === 'bridge') { g.moveTo(x - 4 * k, y - 2 * k); g.lineTo(x + 4 * k, y - 2 * k); g.moveTo(x - 4 * k, y + 2 * k); g.lineTo(x + 4 * k, y + 2 * k); }
  else if (c.kind === 'ford') { for (let i = -1; i <= 1; i += 2) { g.moveTo(x - 4 * k, y + i * 1.6 * k); g.quadraticCurveTo(x - 2 * k, y + i * 1.6 * k - 2 * k, x, y + i * 1.6 * k); g.quadraticCurveTo(x + 2 * k, y + i * 1.6 * k + 2 * k, x + 4 * k, y + i * 1.6 * k); } }
  else { g.moveTo(x - 4 * k, y - 3 * k); g.lineTo(x - 1 * k, y); g.lineTo(x - 4 * k, y + 3 * k); g.moveTo(x + 4 * k, y - 3 * k); g.lineTo(x + 1 * k, y); g.lineTo(x + 4 * k, y + 3 * k); }
  g.stroke();
  g.restore();
}

function polyline(g, pts, colour, width, dash, alpha) {
  if (!pts || pts.length < 2) return;
  g.save();
  g.globalAlpha = alpha ?? 1;
  g.strokeStyle = colour; g.lineWidth = width; g.lineJoin = 'round'; g.lineCap = 'round';
  if (dash) g.setLineDash(dash);
  g.beginPath();
  g.moveTo(sx(pts[0][0]), sy(pts[0][1]));
  for (let i = 1; i < pts.length; i++) g.lineTo(sx(pts[i][0]), sy(pts[i][1]));
  g.stroke();
  g.restore();
}

function routeOf(p) {
  if (!p) return null;
  if (p.route && p.route.length >= 2) {
    const pts = [[p.x, p.y]];
    for (let i = 0; i + 1 < p.route.length; i += 2) pts.push([p.route[i], p.route[i + 1]]);
    return pts;
  }
  if (p.straight && p.tx != null) return [[p.x, p.y], [p.tx, p.ty]];
  return null;
}

function lerpPos(it) {
  if (!S.prev || reduceMotion) return [it.x, it.y];
  const t = Math.min(1, (performance.now() - S.animStart) / 700);
  const p = S.prev.get(it.id);
  if (!p || t >= 1) return [it.x, it.y];
  const dx = it.x - p[0], dy = it.y - p[1];
  if (dx * dx + dy * dy > 900) return [it.x, it.y];
  const e = 1 - Math.pow(1 - t, 3);
  return [p[0] + dx * e, p[1] + dy * e];
}

function draw() {
  drawQueued = false;
  const g = ctx;
  g.setTransform(S.dpr, 0, 0, S.dpr, 0, 0);
  const w = canvas.clientWidth, h = canvas.clientHeight;
  g.fillStyle = '#3c4441'; g.fillRect(0, 0, w, h);
  S.hits = [];
  if (!S.frame && !S.map) return;
  // the ground
  if (S.img && S.layers.picture && S.map) {
    const m = S.map.meta;
    g.imageSmoothingEnabled = S.view.s < 2.5;
    g.drawImage(S.img, sx(m.min_x), sy(m.max_y), (m.max_x - m.min_x) * S.view.s, (m.max_y - m.min_y) * S.view.s);
  } else if (S.map || S.frame) {
    const b = B();
    g.fillStyle = '#d9d6c4'; g.fillRect(sx(b.x0), sy(b.y1), (b.x1 - b.x0) * S.view.s, (b.y1 - b.y0) * S.view.s);
  }
  const M = S.model;
  if (!M) return;
  const animating = S.prev && !reduceMotion && performance.now() - S.animStart < 700;

  // crossings that matter
  if (S.layers.crossings && S.atlas && S.atlas.ready) {
    const showAll = S.view.s >= 1.2;
    for (const c of S.atlas.crossings) {
      if (!showAll && !c.critical) continue;
      const x = sx(c.x), y = sy(c.y);
      if (x < -20 || y < -20 || x > w + 20 || y > h + 20) continue;
      drawCrossingIcon(g, x, y, c, c.critical);
      S.hits.push({ type: 'crossing', x, y, r: 9, c });
    }
  }
  // every lord's planned way
  if (S.layers.allways) {
    for (const it of M.items) {
      if (it.kind !== 'lord' || !it.row) continue;
      if (S.mode === 'sees' && it.rel !== 'own') continue;
      polyline(g, routeOf(it.row), relColour(it.rel), 1.4, null, 0.55);
    }
  }
  // enemy armies' trails
  if (S.layers.trails) {
    for (const t of M.trails) {
      const pts = (t.points || []).map((p) => [p.x, p.y]);
      polyline(g, pts, C.enemy, 2, [1, 4], 0.9);
      for (const p of t.points || []) {
        g.beginPath(); g.arc(sx(p.x), sy(p.y), 2.6, 0, Math.PI * 2); g.fillStyle = C.enemy; g.fill();
        if (S.view.s >= 2.2) { g.font = '11px ' + token('--font-ui'); g.fillStyle = '#1b1f1d'; g.fillText(when(p.hours, true), sx(p.x) + 5, sy(p.y) - 4); }
      }
    }
  }
  // holdings
  const ownHold = new Set((M.rf && S.mode === 'sees' ? M.rf.view.holdings || [] : []).map((x) => x.id));
  for (const s of S.frame.settlements) {
    if (s.kind === 'village' && !S.layers.villages) continue;
    const x = sx(s.x), y = sy(s.y);
    if (x < -20 || y < -20 || x > w + 20 || y > h + 20) continue;
    drawPlaceIcon(g, x, y, s, { own: ownHold.has(s.id) });
    S.hits.push({ type: 'place', x, y, r: s.kind === 'village' ? 5 : 8, s });
  }
  // lords inside holdings
  for (const it of M.insideMarks) {
    const a = (it.slot / Math.max(1, it.of)) * Math.PI * 2 - Math.PI / 2, rr = 13;
    const x = sx(it.x) + Math.cos(a) * rr, y = sy(it.y) + Math.sin(a) * rr;
    drawPartyIcon(g, x, y, Object.assign({}, it, { army: false }), 0.95);
    S.hits.push({ type: 'party', x, y, r: 6, it });
  }
  // the truth under the picture
  if (M.truth.length) {
    g.save(); g.globalAlpha = 0.55;
    for (const p of M.truth) {
      g.beginPath(); g.arc(sx(p.x), sy(p.y), 3.2, 0, Math.PI * 2); g.lineWidth = 1.2; g.strokeStyle = '#f4f4f0'; g.stroke();
    }
    for (const gh of M.ghosts) {
      const p = S.idx.parties.get(gh.id);
      if (p && !p.in_settlement) polyline(g, [[gh.x, gh.y], [p.x, p.y]], '#f4f4f0', 1, [2, 3], 0.7);
    }
    g.restore();
  }
  // seen earlier: faded where last seen
  for (const it of M.ghosts) {
    const x = sx(it.x), y = sy(it.y);
    drawPartyIcon(g, x, y, it, 0.42);
    if (S.view.s >= 1.6 && it.kind === 'lord') { g.font = '10.5px ' + token('--font-ui'); g.fillStyle = '#f4f4f0'; g.fillText(Math.round(it.ago) + ' h ago', x + 7, y + 11); }
    S.hits.push({ type: 'party', x, y, r: partyRadius(it) + 3, it });
  }
  // parties in view
  const order = M.items.slice().sort((a, b) => (a.kind === 'lord') - (b.kind === 'lord') || (a.army - b.army));
  for (const it of order) {
    const [px, py] = lerpPos(it);
    const x = sx(px), y = sy(py);
    if (x < -30 || y < -30 || x > w + 30 || y > h + 30) continue;
    drawPartyIcon(g, x, y, it, it.counted ? 0.8 : 1);
    S.hits.push({ type: 'party', x, y, r: partyRadius(it) + 3, it });
    if (it.army && S.view.s >= 0.9) {
      g.font = '600 11px ' + token('--font-num'); g.fillStyle = '#111'; g.strokeStyle = 'rgba(255,255,255,.85)'; g.lineWidth = 3;
      const label = N(it.men);
      g.strokeText(label, x + partyRadius(it) + 5, y + 4); g.fillText(label, x + partyRadius(it) + 5, y + 4);
    }
  }
  // the selection, its way and the way asked
  if (S.way && S.way.points) polyline(g, S.way.points, S.way.colour, 3.2, S.way.dash, 0.95);
  if (S.asked && S.asked.points) polyline(g, S.asked.points, C.accent, 3, [6, 4], 1);
  for (const way of [S.way, S.asked]) {
    if (!way || !way.crossings) continue;
    for (const c of way.crossings) drawCrossingIcon(g, sx(c.x), sy(c.y), c, true);
  }
  if (S.selected) {
    const sel = S.selected;
    let x, y;
    if (sel.type === 'party') { const p = lerpPos(sel.it); x = sx(p[0]); y = sy(p[1]); }
    else if (sel.type === 'place') { x = sx(sel.s.x); y = sy(sel.s.y); }
    else { x = sx(sel.c.x); y = sy(sel.c.y); }
    g.save(); g.beginPath(); g.arc(x, y, 16, 0, Math.PI * 2); g.lineWidth = 2.5; g.strokeStyle = C.accent; g.stroke(); g.restore();
  }
  // names of holdings
  if (S.layers.labels) {
    g.save();
    g.font = '12.5px ' + token('--font-display');
    g.lineWidth = 3; g.strokeStyle = 'rgba(250,248,240,.88)'; g.fillStyle = '#1b1f1d';
    for (const s of S.frame.settlements) {
      if (s.kind === 'village' ? S.view.s < 3.2 : S.view.s < 0.75) continue;
      if (s.kind === 'village' && !S.layers.villages) continue;
      const x = sx(s.x) + 9, y = sy(s.y) - 7;
      if (x < -100 || y < -20 || x > w + 20 || y > h + 20) continue;
      g.strokeText(s.name, x, y); g.fillText(s.name, x, y);
    }
    g.restore();
  }
  if (animating) requestDraw();
}

// ---------------------------------------------------------------- the legend

function legendCanvas(drawer) {
  const c = document.createElement('canvas');
  const d = window.devicePixelRatio || 1;
  c.width = 30 * d; c.height = 20 * d; c.style.width = '30px'; c.style.height = '20px';
  const g = c.getContext('2d'); g.scale(d, d); drawer(g); return c;
}

function buildLegend() {
  const body = $('legend-body');
  body.innerHTML = '';
  const group = (t) => { const d = document.createElement('div'); d.className = 'legend-group'; d.textContent = t; body.appendChild(d); };
  const row = (label, drawer) => { const r = document.createElement('div'); r.className = 'legend-row'; r.appendChild(legendCanvas(drawer)); const s = document.createElement('span'); s.textContent = label; r.appendChild(s); body.appendChild(r); };
  const lord = (rel, extra) => Object.assign({ kind: 'lord', rel, faction: null }, extra || {});
  group('Holdings (coloured by owner)');
  row('Town', (g) => drawPlaceIcon(g, 15, 10, { kind: 'town', faction: null }));
  row('Castle', (g) => drawPlaceIcon(g, 15, 10, { kind: 'castle', faction: null }));
  row('Village', (g) => drawPlaceIcon(g, 15, 10, { kind: 'village', faction: null }));
  row('One of the followed realm\'s', (g) => drawPlaceIcon(g, 15, 10, { kind: 'castle', faction: null }, { own: true }));
  row('Under siege', (g) => drawPlaceIcon(g, 15, 10, { kind: 'castle', faction: null, besieged_by: 'x' }));
  row('Being raided', (g) => drawPlaceIcon(g, 15, 12, { kind: 'village', faction: null, village_state: 'BeingRaided' }));
  group('Parties (ring: whose side)');
  row('Its own lord', (g) => drawPartyIcon(g, 15, 10, lord('own')));
  row('Enemy lord', (g) => drawPartyIcon(g, 15, 10, lord('enemy')));
  row('Ally', (g) => drawPartyIcon(g, 15, 10, lord('ally')));
  row('Neither', (g) => drawPartyIcon(g, 15, 10, lord('neutral')));
  row('Army (size by strength)', (g) => drawPartyIcon(g, 15, 10, lord('enemy', { army: true, strength: 600 })));
  row('Bandits', (g) => drawPartyIcon(g, 15, 10, { kind: 'bandit', rel: 'enemy' }));
  row('Caravan', (g) => drawPartyIcon(g, 15, 10, { kind: 'caravan', rel: 'neutral' }));
  row('Villagers', (g) => drawPartyIcon(g, 15, 10, { kind: 'villager', rel: 'neutral' }));
  row('Fighting', (g) => drawPartyIcon(g, 13, 11, lord('enemy', { battle: true })));
  row('Holding the agent\'s order', (g) => drawPartyIcon(g, 13, 9, lord('own', { held: 'x' })));
  group('What it knows');
  row('Seen earlier, now out of sight', (g) => drawPartyIcon(g, 15, 10, lord('enemy', { ghost: true }), 0.42));
  row('In sight but left out of its report', (g) => drawPartyIcon(g, 15, 10, lord('enemy', { cut: true })));
  row('In sight, only counted', (g) => drawPartyIcon(g, 15, 10, { kind: 'caravan', rel: 'neutral', counted: true }));
  row('Enemy army\'s trail', (g) => { g.strokeStyle = C.enemy; g.lineWidth = 2; g.setLineDash([1, 4]); g.beginPath(); g.moveTo(2, 14); g.lineTo(28, 6); g.stroke(); g.setLineDash([]); g.fillStyle = C.enemy; g.beginPath(); g.arc(2, 14, 2.6, 0, 7); g.arc(28, 6, 2.6, 0, 7); g.fill(); });
  row('The truth (when shown)', (g) => { g.beginPath(); g.arc(15, 10, 3.2, 0, 7); g.lineWidth = 1.2; g.strokeStyle = '#7d7d78'; g.stroke(); });
  group('Ways');
  row('The game\'s own planned way', (g) => { g.strokeStyle = C.own; g.lineWidth = 3; g.beginPath(); g.moveTo(2, 14); g.lineTo(14, 8); g.lineTo(28, 10); g.stroke(); });
  row('Predicted way (ours)', (g) => { g.strokeStyle = C.enemy; g.lineWidth = 3; g.setLineDash([5, 4]); g.beginPath(); g.moveTo(2, 14); g.lineTo(28, 7); g.stroke(); });
  row('A way you asked for', (g) => { g.strokeStyle = C.accent; g.lineWidth = 3; g.setLineDash([6, 4]); g.beginPath(); g.moveTo(2, 7); g.lineTo(28, 14); g.stroke(); });
  row('Bridge', (g) => drawCrossingIcon(g, 15, 10, { kind: 'bridge' }));
  row('Ford', (g) => drawCrossingIcon(g, 15, 10, { kind: 'ford' }));
  row('Pass', (g) => drawCrossingIcon(g, 15, 10, { kind: 'pass' }));
  row('Critical: the only way, or a long way round', (g) => drawCrossingIcon(g, 15, 10, { kind: 'bridge', critical: true }));
}

// ---------------------------------------------------------------- the card

function cardFor(sel) {
  const card = $('card');
  card.hidden = false;
  if (sel.type === 'place') return placeCard(card, sel.s);
  if (sel.type === 'crossing') return crossingCard(card, sel.c);
  return partyCard(card, sel.it);
}

function relChip(rel) { return '<span class="rel rel-' + rel + '">' + ({ own: 'its own', enemy: 'enemy', ally: 'ally', neutral: 'neither' }[rel] || rel) + '</span>'; }

function partyCard(card, it) {
  const p = it.row, vp = it.vp, f = S.frame;
  const fac = S.idx.factions.get(it.faction);
  const facName = (vp && vp.faction_name) || (fac && fac.name) || it.faction || '';
  const known = S.mode === 'sees' && it.rel !== 'own';
  let dl = '';
  const add = (k, v) => { if (v != null && v !== '') dl += '<dt>' + k + '</dt><dd>' + v + '</dd>'; };
  add('Men', it.army ? N(it.men) + ' in an army of ' + N((vp && vp.army_parties) || (p && p.army_parties)) + ' parties' : N(it.men));
  add('Strength', N(it.strength));
  add(known && it.ghost ? 'Was' : 'Doing', esc(vp && known ? vp.doing : p ? doingText(p) : vp?.doing));
  if (vp && vp.near) add('Where', esc((vp.near_distance < 1 ? 'at ' : D1(vp.near_distance) + ' from ') + vp.near));
  if (it.ghost) add('Last seen', N(it.ago) + ' hours ago' + (vp.seen_by ? ', by ' + esc(vp.seen_by) : ''));
  else if (vp && vp.seen_by) add('Seen by', esc(vp.seen_by));
  if (it.cut) add('Its report', '<span class="flag">' + (it.ghost ? 'left out of SEEN EARLIER' : 'left out of IN VIEW') + '</span> (the list was full)');
  if (it.counted) add('Its report', 'only counted ("caravans, villagers and other small parties")');
  if (p && !known && p.speed) add('Pace', D1(p.speed) + ' an hour on open ground' + (p.final_speed ? ', ' + D1(p.final_speed) + ' now' : ''));
  if (p && !known && p.food_days >= 0 && p.faction === S.realm) add('Food', p.food_days + ' days');
  if (it.held) add('Order', esc(it.held));
  if (it.inside) add('Inside', esc((S.idx.places.get(it.inside) || {}).name));
  card.innerHTML = '<button class="close" aria-label="Close">&times;</button>'
    + '<h3>' + esc(it.name) + '</h3><div class="sub">' + esc(facName) + ' &middot; ' + kindName(it.kind).toLowerCase() + ' ' + relChip(it.rel) + '</div>'
    + '<dl>' + dl + '</dl><div class="way" id="way"><h4>Its way</h4><div class="muted">Working it out&hellip;</div></div>'
    + '<div class="way" id="asked" hidden></div>'
    + '<div class="actions"><button class="btn" id="ask">Ask the way from here</button><button class="btn" id="follow">Centre on it</button></div>';
  card.querySelector('.close').onclick = deselect;
  $('ask').onclick = () => startPick();
  $('follow').onclick = () => centreOn(it.x, it.y, 2);
  wayFor(it);
}

function placeCard(card, s) {
  const fac = S.idx.factions.get(s.faction);
  const rf = S.model && S.model.rf;
  const vh = rf && S.mode === 'sees' ? (rf.view.holdings || []).concat(rf.view.frontier || []).find((h) => h.id === s.id) : null;
  let dl = '';
  const add = (k, v) => { if (v != null && v !== '') dl += '<dt>' + k + '</dt><dd>' + v + '</dd>'; };
  add('Owner', esc(fac ? fac.name : s.faction));
  if (s.kind !== 'village') {
    const g = vh ? vh.garrison : (S.mode === 'truth' ? s.garrison : null);
    add('Garrison', g == null ? 'not in sight' : g < 0 ? 'not in sight' : N(g));
    add('Militia', N(vh ? vh.militia : s.militia));
  }
  if (vh && vh.wall_level) {
    add('Walls', 'level ' + vh.wall_level);
    add('Defenders', 'strength ' + N(vh.defence) + (vh.lords_inside ? ' (' + N(vh.lords_inside) + ' of it lords inside)' : ''));
    add('Holds against', 'besiegers up to strength ' + N(vh.holds_up_to));
  }
  if (vh && vh.status) add('Now', esc(vh.status));
  else if (s.besieged_by) add('Now', 'under siege by ' + esc((S.idx.parties.get(s.besieger) || {}).name || '?'));
  if (s.village_state && s.village_state !== 'Normal') add('Village', esc(s.village_state === 'BeingRaided' ? 'being raided' : s.village_state.toLowerCase()));
  if (vh && vh.threat) add('Nearest enemy', esc(vh.threat) + ', ' + N(vh.threat_men) + ' men at ' + D1(vh.threat_distance));
  const inside = (S.idx.inside.get(s.id) || []).filter((p) => S.mode === 'truth' || p.faction === S.realm || (rf && (rf.in_sight || []).includes(p.id)));
  if (inside.length) add('Lords inside', inside.map((p) => '<button class="who" data-party="' + esc(p.id) + '">' + esc(p.name) + '</button>').join(', '));
  card.innerHTML = '<button class="close" aria-label="Close">&times;</button><h3>' + esc(s.name) + '</h3><div class="sub">' + s.kind + ' &middot; [' + esc(s.id) + ']</div><dl>' + dl + '</dl>';
  card.querySelector('.close').onclick = deselect;
  card.querySelectorAll('.who').forEach((b) => (b.onclick = () => selectParty(b.dataset.party)));
}

function crossingCard(card, c) {
  const ex = (c.examples || []).map((e) => '<li>' + esc(e.from) + ' to ' + esc(e.to) + ': ' + N(e.before) + (e.after == null || e.after === 'NaN' || isNaN(e.after) ? ', and no other way' : ' becomes ' + N(e.after)) + '</li>').join('');
  card.hidden = false;
  card.innerHTML = '<button class="close" aria-label="Close">&times;</button><h3>' + esc(c.name) + '</h3>'
    + '<div class="sub">' + c.kind + (c.critical ? ' &middot; <span class="flag">critical</span>' : '') + '</div>'
    + '<dl><dt>Width</dt><dd>' + D1(c.width) + ' map units across</dd><dt>Ways through it</dt><dd>' + N(c.ways) + ' between neighbouring holdings</dd>'
    + '<dt>If it were closed</dt><dd>' + (c.cuts_off ? 'some way has no other route at all' : 'the longest way round adds ' + N(c.detour)) + '</dd></dl>'
    + (ex ? '<div class="way"><h4>Ways that use it</h4><ul>' + ex + '</ul></div>' : '');
  card.querySelector('.close').onclick = deselect;
}

function waysHtml(title, d) {
  if (!d || !d.ok) return '<h4>' + title + '</h4><div class="muted">' + esc(d ? d.why || 'no way found' : 'no way found') + '</div>';
  return '<h4>' + title + '</h4><ul>' + (d.lines || []).map((l) => '<li>' + esc(l) + '</li>').join('') + '</ul>';
}

function paceFor(it) {
  const p = it.row;
  if (p && (S.mode === 'truth' || it.rel === 'own') && p.speed) return [p.speed, 'its own pace'];
  const t = S.model && S.model.trails.find((x) => x.id === it.id);
  if (t && t.points && t.points.length >= 2) {
    const a = t.points[0], b = t.points[t.points.length - 1];
    let d = 0;
    for (let i = 1; i < t.points.length; i++) d += Math.hypot(t.points[i].x - t.points[i - 1].x, t.points[i].y - t.points[i - 1].y);
    const span = b.hours - a.hours;
    if (span >= 2 && d >= 3) return [d / span, 'the pace its trail shows'];
  }
  return [3.4, 'a typical pace (its own is not known)'];
}

async function wayFor(it) {
  const box = $('way');
  S.way = null;
  const p = it.row;
  const known = S.mode === 'sees' && it.rel !== 'own' && !S.layers.truth;
  const [pace, paceFrom] = paceFor(it);
  try {
    if (!known && p && routeOf(p) && !it.ghost) {
      const pts = routeOf(p);
      const d = await getJSON('/api/way', { method: 'POST', body: JSON.stringify({ crc: S.mapCrc, points: pts, pace, hours: S.frame.hours, pace_from: paceFrom }) });
      S.way = { points: pts, colour: relColour(it.rel), dash: null, crossings: d.crossings };
      box.innerHTML = waysHtml(p.straight ? 'Walking straight there' : 'The way the game planned for it', d);
    } else {
      const toward = it.vp ? it.vp.toward : p ? towardOf(p) : null;
      const s = toward ? S.idx.places.get(toward) : null;
      const gate = s && S.map ? (S.map.places.find((x) => x.id === s.id) || s) : s;
      if (!gate) { box.innerHTML = '<h4>Its way</h4><div class="muted">' + (it.ghost ? 'Not known: it was not heading for a holding when last seen.' : 'Not heading for a holding' + (p && known ? ' (as far as its realm can tell)' : '') + '.') + '</div>'; requestDraw(); return; }
      const gx = gate.gx || gate.x, gy = gate.gy || gate.y;
      const start = it.ghost ? S.frame.hours - it.ago : S.frame.hours;
      const d = await getJSON('/api/route?crc=' + encodeURIComponent(S.mapCrc || '') + '&fx=' + it.x + '&fy=' + it.y + '&tx=' + gx + '&ty=' + gy + '&pace=' + pace + '&hours=' + start + '&pace_from=' + encodeURIComponent(paceFrom));
      S.way = { points: d.points, colour: relColour(it.rel), dash: [6, 5], crossings: d.crossings };
      box.innerHTML = waysHtml((it.ghost ? 'If it marched on from where it was seen to ' : 'If it marches on to ') + esc(s.name) + ' (our prediction)', d);
    }
  } catch (e) { box.innerHTML = '<h4>Its way</h4><div class="muted">' + esc(e.message) + '</div>'; }
  requestDraw();
}

function startPick() {
  S.picking = true;
  canvas.classList.add('picking');
  $('pick-hint').hidden = false;
}
function endPick() { S.picking = false; canvas.classList.remove('picking'); $('pick-hint').hidden = true; }
$('pick-cancel').onclick = endPick;

async function askTo(x, y, placeName) {
  endPick();
  const sel = S.selected;
  if (!sel || sel.type !== 'party') return;
  const it = sel.it;
  const [pace, paceFrom] = paceFor(it);
  const box = $('asked');
  box.hidden = false;
  box.innerHTML = '<h4>Asked: the way to ' + esc(placeName || 'that spot') + '</h4><div class="muted">Working it out&hellip;</div>';
  try {
    const d = await getJSON('/api/route?crc=' + encodeURIComponent(S.mapCrc || '') + '&fx=' + it.x + '&fy=' + it.y + '&tx=' + x + '&ty=' + y + '&pace=' + pace + '&hours=' + S.frame.hours + '&pace_from=' + encodeURIComponent(paceFrom));
    S.asked = { points: d.points, crossings: d.crossings };
    box.innerHTML = waysHtml('Asked: the way to ' + esc(placeName || 'that spot'), d);
  } catch (e) { box.innerHTML = '<h4>Asked</h4><div class="muted">' + esc(e.message) + '</div>'; }
  requestDraw();
}

function select(sel) {
  S.selected = sel;
  S.way = null; S.asked = null;
  cardFor(sel);
  renderLog();
  requestDraw();
}
function deselect() { S.selected = null; S.way = null; S.asked = null; $('card').hidden = true; endPick(); renderLog(); requestDraw(); }

function selectParty(id) {
  if (!S.model) return;
  const pid = S.idx.parties.has(id) ? id : S.idx.heroes.get(id) || id;
  const all = S.model.items.concat(S.model.ghosts, S.model.insideMarks);
  let it = all.find((x) => x.id === pid);
  if (!it) {
    const p = S.idx.parties.get(pid);
    const s = S.idx.places.get(id);
    if (s) { select({ type: 'place', s }); centreOn(s.x, s.y); return; }
    if (!p) return;
    it = { type: 'party', id: p.id, x: p.x, y: p.y, kind: p.kind, rel: relationTo(S.realm, p.faction, p.kind), row: p, name: p.name, men: p.army_men || p.men, strength: p.army_strength || p.strength, army: p.army_leader === p.id && p.army_men > 0, faction: p.faction };
  }
  select({ type: 'party', it });
  centreOn(it.x, it.y);
}

// ---------------------------------------------------------------- the pointer

let drag = null;
canvas.addEventListener('pointerdown', (e) => { drag = { x: e.clientX, y: e.clientY, ox: S.view.ox, oy: S.view.oy, moved: false }; canvas.setPointerCapture(e.pointerId); });
canvas.addEventListener('pointermove', (e) => {
  if (!drag) return;
  const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
  if (Math.abs(dx) + Math.abs(dy) > 3) { drag.moved = true; canvas.classList.add('dragging'); }
  if (drag.moved) { S.view.ox = drag.ox + dx; S.view.oy = drag.oy + dy; requestDraw(); }
});
canvas.addEventListener('pointerup', (e) => {
  canvas.classList.remove('dragging');
  const d = drag; drag = null;
  if (!d || d.moved) return;
  const r = canvas.getBoundingClientRect();
  const x = e.clientX - r.left, y = e.clientY - r.top;
  const hit = hitTest(x, y);
  if (S.picking) {
    if (hit && hit.type === 'place') { const gate = S.map && S.map.places.find((p) => p.id === hit.s.id); askTo(gate ? gate.gx || gate.x : hit.s.x, gate ? gate.gy || gate.y : hit.s.y, hit.s.name); }
    else askTo(wx(x), wy(y), null);
    return;
  }
  if (!hit) { deselect(); return; }
  if (hit.type === 'party') select({ type: 'party', it: hit.it });
  else if (hit.type === 'place') select({ type: 'place', s: hit.s });
  else select({ type: 'crossing', c: hit.c });
});
canvas.addEventListener('wheel', (e) => {
  e.preventDefault();
  const r = canvas.getBoundingClientRect();
  const x = e.clientX - r.left, y = e.clientY - r.top;
  const mx = wx(x), my = wy(y);
  S.view.s = Math.max(0.15, Math.min(48, S.view.s * Math.exp(-e.deltaY * 0.0015)));
  S.view.ox = x - (mx - B().x0) * S.view.s;
  S.view.oy = y - (B().y1 - my) * S.view.s;
  requestDraw();
}, { passive: false });
canvas.addEventListener('keydown', (e) => {
  const step = 60;
  if (e.key === 'Escape') deselect();
  else if (e.key === 'ArrowLeft') S.view.ox += step; else if (e.key === 'ArrowRight') S.view.ox -= step;
  else if (e.key === 'ArrowUp') S.view.oy += step; else if (e.key === 'ArrowDown') S.view.oy -= step;
  else if (e.key === '+' || e.key === '=') zoomBy(1.25); else if (e.key === '-') zoomBy(0.8);
  else return;
  e.preventDefault(); requestDraw();
});
function zoomBy(k) {
  const x = canvas.clientWidth / 2, y = canvas.clientHeight / 2, mx = wx(x), my = wy(y);
  S.view.s = Math.max(0.15, Math.min(48, S.view.s * k));
  S.view.ox = x - (mx - B().x0) * S.view.s; S.view.oy = y - (B().y1 - my) * S.view.s;
}

function hitTest(x, y) {
  let best = null, bd = Infinity;
  for (const h of S.hits || []) {
    const d = Math.hypot(h.x - x, h.y - y) - (h.type === 'party' ? 2 : 0);
    if (d <= Math.max(7, h.r) && d < bd) { bd = d; best = h; }
  }
  return best;
}

// ---------------------------------------------------------------- the log

function nameOf(id) {
  if (!id || !S.idx) return id || '?';
  const p = S.idx.parties.get(id) || S.idx.parties.get(S.idx.heroes.get(id));
  if (p) return p.name || id;
  const s = S.idx.places.get(id);
  if (s) return s.name;
  return id;
}
function who(id) { return '<button class="who" data-id="' + esc(id) + '">' + esc(nameOf(id)) + '</button>'; }

function buildFilters() {
  const box = $('filters');
  box.innerHTML = '';
  for (const [k, label] of KINDS) {
    const b = document.createElement('button');
    b.type = 'button'; b.textContent = label; b.setAttribute('aria-pressed', S.kinds.has(k) ? 'true' : 'false');
    b.onclick = () => {
      const on = !S.kinds.has(k);
      const group = k === 'answer' ? ['answer', 'no_answer'] : k === 'carried' ? ['carried', 'checked'] : [k];
      for (const g of group) on ? S.kinds.add(g) : S.kinds.delete(g);
      b.setAttribute('aria-pressed', on ? 'true' : 'false');
      renderLog();
    };
    box.appendChild(b);
  }
}
$('only-selected').onchange = renderLog;

function selectedIds() {
  const sel = S.selected;
  if (!sel) return null;
  if (sel.type === 'party') { const ids = new Set([sel.it.id]); const p = sel.it.row; if (p && p.leader) ids.add(p.leader); if (sel.it.vp && sel.it.vp.lord) ids.add(sel.it.vp.lord); return ids; }
  if (sel.type === 'place') return new Set([sel.s.id]);
  return null;
}

function renderLog() {
  const list = $('log-list');
  const ids = selectedIds();
  const only = $('only-selected').checked && ids;
  const rows = [];
  for (let i = S.log.length - 1; i >= 0; i--) {
    const e = S.log[i];
    if (!S.kinds.has(e.kind)) continue;
    const names = (e.ids || []).concat(e.places || []).concat((e.orders || []).flatMap((o) => [o.lord, o.target, o.at].concat(o.lords || [])));
    const hit = ids && names.some((n) => ids.has(n));
    if (only && !hit) continue;
    if (S.frame && !S.live && e.hours > S.frame.hours + 0.5) continue;
    let text = esc(e.text);
    if (e.orders && e.orders.length) {
      text += '<ul>' + e.orders.map((o) => '<li>' + who(o.lord) + ': ' + esc(o.do) + (o.at ? ' ' + who(o.at) : '') + (o.target ? ' ' + who(o.target) : '')
        + (o.lords && o.lords.length ? ' with ' + o.lords.map(who).join(', ') : '') + (o.then ? '; then ' + esc(o.then) : '') + (o.why ? ' <span class="muted">(' + esc(o.why) + ')</span>' : '') + '</li>').join('') + '</ul>';
    }
    if (e.plan) text += '<div class="plan">' + esc(e.plan) + '</div>';
    if (e.kind !== 'answer' && e.kind !== 'council' && e.ids && e.ids.length) text += ' <span class="muted">' + e.ids.slice(0, 4).map(who).join(' ') + '</span>';
    rows.push('<li class="' + (hit ? 'hit' : '') + '"><span class="log-time">' + (e.hours ? when(e.hours, true) : '') + (e.poke >= 0 && (e.kind === 'woke' || e.kind === 'answer' || e.kind === 'council') ? '<br>#' + e.poke : '') + '</span>'
      + '<span class="log-kind k-' + e.kind + '">' + (KIND_LABEL[e.kind] || e.kind) + '</span><div class="log-text">' + text + '</div></li>');
    if (rows.length >= 300) break;
  }
  list.innerHTML = rows.join('') || '<li><span></span><div class="log-text muted">Nothing yet for this load.</div></li>';
  list.querySelectorAll('.who').forEach((b) => (b.onclick = () => selectParty(b.dataset.id)));
  $('log-count').textContent = S.log.length ? S.log.length + ' lines' : '';
}

async function refreshLog() {
  if (!S.campaign) return;
  const rf = S.frame && S.frame.realms ? S.frame.realms[S.realm] : null;
  const realm = rf && rf.agent ? S.realm : (S.status && S.status.current && S.status.current.agent) || S.realm;
  try {
    const d = await getJSON('/api/log?campaign=' + encodeURIComponent(S.campaign) + '&load=' + encodeURIComponent(S.load || '') + '&realm=' + encodeURIComponent(realm || ''));
    S.log = d.entries || [];
    const realmName = S.frame && S.frame.realms && S.frame.realms[realm] ? S.frame.realms[realm].name : realm;
    $('log-title').textContent = 'Moves of ' + (realmName || 'the agent');
    renderLog();
  } catch (e) { /* the next look tries again */ }
}

// ---------------------------------------------------------------- checks

async function renderChecks() {
  const box = $('checks');
  try {
    const c = await getJSON('/api/checks');
    const st = S.status || {};
    const acc = (c.accuracy || []).map((a) => '<tr><td>' + esc(a.guess) + '</td><td>' + N(a.samples) + '</td><td>' + (a.median_gap_pct == null ? '' : D1(a.median_gap_pct) + '%') + '</td><td>' + (a.mean_gap_pct == null ? '' : D1(a.mean_gap_pct) + '%') + '</td><td>' + (a.median_apart == null ? '' : D1(a.median_apart)) + '</td></tr>').join('');
    const sp = (c.speeds || []).map((s) => '<tr><td>' + esc(s.ground) + '</td><td>' + N(s.samples) + '</td><td>' + s.median_ratio + '</td></tr>').join('');
    box.innerHTML = '<h3>Walking map</h3><div>' + esc(st.map ? st.map.progress : '?') + '</div>'
      + '<h3>Crossing atlas</h3><div>' + esc(c.atlas) + '</div>'
      + '<h3>Our ways against the game\'s</h3><div class="muted">From ' + N(c.frames_checked) + ' hours seen: how much longer our way is than the one the game planned, and how far apart they run (map units).</div>'
      + '<table><tr><th>Guess</th><th>Ways</th><th>Median gap</th><th>Mean</th><th>Apart</th></tr>' + acc + '</table>'
      + '<h3>Speed on each ground</h3><div class="muted">Each marching lord\'s speed now against its pace on open ground, by day.</div>'
      + '<table><tr><th>Ground</th><th>Seen</th><th>Ratio</th></tr>' + sp + '</table>';
  } catch (e) { box.innerHTML = '<div class="muted">' + esc(e.message) + '</div>'; }
}

function menu(btnId, popId, onOpen) {
  const b = $(btnId), p = $(popId);
  b.onclick = (e) => { e.stopPropagation(); const open = p.hidden; document.querySelectorAll('.pop').forEach((x) => (x.hidden = true)); document.querySelectorAll('.menu .btn').forEach((x) => x.setAttribute('aria-expanded', 'false')); p.hidden = !open; b.setAttribute('aria-expanded', open ? 'true' : 'false'); if (open && onOpen) onOpen(); };
  p.addEventListener('click', (e) => e.stopPropagation());
}
document.addEventListener('click', () => { document.querySelectorAll('.pop').forEach((x) => (x.hidden = true)); });
menu('layers-btn', 'layers');
menu('checks-btn', 'checks', renderChecks);
document.querySelectorAll('[data-layer]').forEach((cb) => {
  cb.onchange = () => { S.layers[cb.dataset.layer] = cb.checked; rebuild(); };
});

// ---------------------------------------------------------------- frames, live and rewind

function rebuild(keepAnim) {
  if (!S.frame) return;
  S.model = buildModel();
  if (!keepAnim) S.prev = null;
  if (S.selected && S.selected.type === 'party') {
    const id = S.selected.it.id;
    const all = S.model.items.concat(S.model.ghosts, S.model.insideMarks);
    const it = all.find((x) => x.id === id);
    if (it) { S.selected.it = it; }
  }
  requestDraw();
}

async function loadMap(crc) {
  if (!crc || crc === S.mapCrc) return;
  try {
    const m = await getJSON('/api/map/meta?crc=' + encodeURIComponent(crc));
    S.map = m; S.mapCrc = crc; S._b = null;
    const img = new Image();
    img.onload = () => { S.img = img; requestDraw(); };
    img.src = m.picture;
    S.atlas = await getJSON('/api/atlas?crc=' + encodeURIComponent(crc)).catch(() => null);
    S.view.fitted = false;
  } catch (e) { S.mapCrc = null; }
}

async function loadFrame(hour) {
  if (!S.campaign) return;
  try {
    const f = await getJSON('/api/frame?campaign=' + encodeURIComponent(S.campaign) + '&load=' + encodeURIComponent(S.load) + '&h=' + (hour == null ? 'latest' : hour));
    const prevModel = S.model;
    S.frame = f;
    S.idx = indexFrame(f);
    if (!S.map) S._b = null;
    if (!S.realm || !(f.realms && f.realms[S.realm])) S.realm = f.watched && f.realms && f.realms[f.watched] ? f.watched : f.player;
    fillRealms();
    const prev = new Map();
    if (prevModel) for (const it of prevModel.items) prev.set(it.id, [it.x, it.y]);
    S.prev = S.live ? prev : null;
    S.animStart = performance.now();
    S.model = buildModel();
    if (!S.view.fitted) fitRealm();
    if (S.selected) {
      if (S.selected.type === 'party') {
        const all = S.model.items.concat(S.model.ghosts, S.model.insideMarks);
        const it = all.find((x) => x.id === S.selected.it.id);
        if (it) { S.selected.it = it; partyCard($('card'), it); } else deselect();
      } else if (S.selected.type === 'place') { const s = S.idx.places.get(S.selected.s.id); if (s) { S.selected.s = s; placeCard($('card'), s); } }
    }
    $('date').textContent = when(f.hours) + (f.night ? ' · night' : '');
    if (pendingSelect) { const id = pendingSelect; pendingSelect = null; setTimeout(() => selectParty(id), 0); }
    $('empty').hidden = true;
    syncSlider();
    requestDraw();
    if (S.atlas && !S.atlas.ready && S.mapCrc) S.atlas = await getJSON('/api/atlas?crc=' + encodeURIComponent(S.mapCrc)).catch(() => S.atlas);
  } catch (e) { showEmpty('No picture of this hour yet', e.message); }
}

function fillRealms() {
  const sel = $('realm');
  const f = S.frame;
  const opts = Object.entries(f.realms || {}).map(([id, r]) => [id, r.name + (r.agent ? ' (the agent)' : r.player ? ' (yours)' : '')]);
  const key = opts.map((o) => o[0] + o[1]).join('|');
  if (sel.dataset.key !== key) {
    sel.innerHTML = opts.map(([id, n]) => '<option value="' + esc(id) + '">' + esc(n) + '</option>').join('');
    sel.dataset.key = key;
  }
  sel.value = S.realm || '';
}
$('realm').onchange = () => { S.realm = $('realm').value; S.view.fitted = true; deselect(); rebuild(); refreshLog(); };
document.querySelectorAll('.seg button').forEach((b) => (b.onclick = () => {
  S.mode = b.dataset.mode;
  document.querySelectorAll('.seg button').forEach((x) => x.setAttribute('aria-checked', x === b ? 'true' : 'false'));
  rebuild();
  if (S.selected) cardFor(S.selected);
}));

function syncSlider() {
  const sl = $('slider');
  sl.max = Math.max(0, S.hours.length - 1);
  const i = S.frame ? S.hours.indexOf(Math.floor(S.frame.hours + 1e-6)) : -1;
  if (i >= 0) sl.value = i;
  $('slider-label').textContent = S.frame ? when(S.frame.hours, true) : '';
  $('golive').setAttribute('aria-pressed', S.live ? 'true' : 'false');
  const chip = $('live');
  const age = S.status ? S.status.seconds_since_frame : null;
  if (!S.live) { chip.textContent = 'rewound'; chip.className = 'chip chip-past'; }
  else if (age != null && age < 45) { chip.textContent = 'live'; chip.className = 'chip chip-live'; }
  else { chip.textContent = age == null ? 'idle' : 'no new hour'; chip.className = 'chip chip-idle'; chip.title = age == null ? '' : 'The last hour came ' + Math.round(age) + ' s ago: the game is paused, in a menu or closed.'; }
}
$('slider').oninput = () => {
  S.live = false; stopPlay();
  const h = S.hours[+$('slider').value];
  if (h != null) loadFrame(h);
};
$('golive').onclick = () => { S.live = true; stopPlay(); loadFrame(null); refreshLog(); };
$('play').onclick = () => {
  if (S.playing) { stopPlay(); return; }
  S.live = false;
  $('play').innerHTML = '&#10074;&#10074;';
  S.playing = setInterval(() => {
    const sl = $('slider');
    const i = +sl.value + 1;
    if (i >= S.hours.length) { S.live = true; stopPlay(); loadFrame(null); return; }
    sl.value = i; loadFrame(S.hours[i]);
  }, 650);
};
function stopPlay() { if (S.playing) clearInterval(S.playing); S.playing = null; $('play').innerHTML = '&#9654;'; }
$('load').onchange = async () => {
  S.load = $('load').value;
  const cur = S.status && S.status.current;
  S.live = !!(cur && cur.load_id === S.load);
  await refreshHours();
  loadFrame(S.live ? null : S.hours[S.hours.length - 1]);
  refreshLog();
};

async function refreshHours() {
  if (!S.campaign || !S.load) return;
  const d = await getJSON('/api/frames?campaign=' + encodeURIComponent(S.campaign) + '&load=' + encodeURIComponent(S.load)).catch(() => null);
  if (d) S.hours = d.hours || [];
}

function fillLoads(st) {
  const sel = $('load');
  const loads = st.loads || [];
  const key = loads.map((l) => l.load_id + ':' + l.frames).join('|');
  if (sel.dataset.key === key) return;
  sel.dataset.key = key;
  sel.innerHTML = loads.map((l) => {
    const wall = l.load_id.replace(/^(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2}).*$/, '$3.$2 $4:$5');
    return '<option value="' + esc(l.load_id) + '">Load of ' + esc(wall) + ': ' + when(l.first, true) + ' to ' + when(l.last, true) + '</option>';
  }).join('');
  sel.value = S.load || '';
}

function showEmpty(title, text) {
  const e = $('empty');
  e.hidden = false;
  e.innerHTML = '<div><strong>' + esc(title) + '</strong>' + esc(text || '') + '</div>';
}

let lastFrameName = null, lastLogAt = 0;
async function poll() {
  try {
    const st = await getJSON('/api/status');
    S.status = st;
    const cur = st.current;
    if (!cur) {
      showEmpty('Waiting for the game', 'Load a campaign with Bannerlord Agents 0.7 (its dashboard setting on). The first hourly picture appears an in-game hour later.');
    } else {
      S.cal = cur.calendar || S.cal;
      if (!S.campaign) { S.campaign = cur.campaign; S.load = cur.load_id; }
      if (!S.frame && !S.live && HASH.get('hour')) { await loadMap(st.map && st.map.ready ? st.map.crc : null); await refreshHours(); await loadFrame(+HASH.get('hour')); }
      if (S.live && (cur.campaign !== S.campaign || cur.load_id !== S.load)) { S.campaign = cur.campaign; S.load = cur.load_id; S.view.fitted = false; S.log = []; }
      fillLoads(st);
      await loadMap(st.map && st.map.ready ? st.map.crc : null);
      if (!S.map && st.map) $('date').title = st.map.progress || '';
      if (S.live && cur.frame !== lastFrameName) {
        lastFrameName = cur.frame;
        await refreshHours();
        await loadFrame(null);
      }
      if (Date.now() - lastLogAt > 4000) { lastLogAt = Date.now(); refreshLog(); }
      syncSlider();
    }
  } catch (e) {
    showEmpty('The dashboard program is not answering', 'Start it again with scripts\\dashboard.ps1, then reload this page.');
  }
  setTimeout(poll, 1500);
}

// links can open the page on a mode, a realm and a selection: #mode=truth&realm=mistymountainorcs&select=lord_MM1_1
const HASH = new URLSearchParams(location.hash.replace(/^#/, ''));
if (HASH.get('mode') === 'truth') { S.mode = 'truth'; document.querySelectorAll('.seg button').forEach((x) => x.setAttribute('aria-checked', x.dataset.mode === 'truth' ? 'true' : 'false')); }
if (HASH.get('realm')) S.realm = HASH.get('realm');
if (HASH.get('hour')) S.live = false;
for (const l of (HASH.get('layers') || '').split(',').filter(Boolean)) { S.layers[l] = true; const cb = document.querySelector('[data-layer="' + l + '"]'); if (cb) cb.checked = true; }
let pendingSelect = HASH.get('select');

// the legend stays as you left it (this browser only)
try { if (localStorage.getItem('warmap.legend') === 'closed') $('legend').open = false; } catch (e) { /* private window: it opens */ }
$('legend').addEventListener('toggle', () => { try { localStorage.setItem('warmap.legend', $('legend').open ? 'open' : 'closed'); } catch (e) { /* not kept */ } });

readColours();
buildLegend();
buildFilters();
resize();
poll();
