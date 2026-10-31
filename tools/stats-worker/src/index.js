// 전투 통계를 받는 Cloudflare Worker — 게임(duel-dx/System/BattleStats.cs)이 전투가 끝날 때 요약 한 덩이를 보낸다.
//
//   POST /v1/battle   전투 한 판의 요약(JSON)을 D1 에 넣는다. 같은 report 번호는 한 번만 들어간다(다시 보내도 안 겹친다).
//   GET  /v1/top      어빌리티별 합계(쓴 횟수 · 피해 · 처치) — 모은 것 전체의 집계만 낸다.
//   GET  /dashboard   대시보드 페이지(dashboard.js) — /v1/dashboard?version= 에서 집계를 받아 그린다. 지금은 누구나 볼 수 있다.
//   POST /v1/bug      버그 리포트 한 건(게임의 「버그 리포트」 창, duel-dx/System/BugReport.cs) — {id} 를 돌려준다. 설치마다 한 시간에 10 건까지.
//   GET  /v1/bugs?since=<id>&status=new        받은 버그 리포트(관리자만 — Authorization: Bearer <BUG_ADMIN_KEY>)
//   POST /v1/bugs/<id>/status {status, note}   버그 리포트의 상태를 바꾼다(관리자만)
//
// 누가 보냈는지는 설치할 때 만든 무작위 번호(install)뿐이다. IP 는 적지 않는다.

import { DASHBOARD_HTML } from './dashboard.js';
import { ABILITIES, CHARACTERS, BATTLES, CHAPTERS, BRANCHES, BRANCH_ORDER } from './names.js';

const MAX_BODY = 64 * 1024, MAX_SKILLS = 300;

const json = (body, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8' } });

const int = (v, max = 2_000_000_000) => (Number.isFinite(v) ? Math.max(0, Math.min(max, Math.trunc(v))) : 0);
const text = (v, max) => (typeof v === 'string' ? v.slice(0, max) : '');
const isId = (v) => typeof v === 'string' && /^[0-9a-f-]{32,36}$/i.test(v);

async function postBattle(request, env) {
  const raw = await request.text();
  if (raw.length > MAX_BODY) return json({ error: 'too large' }, 413);
  let b;
  try { b = JSON.parse(raw); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || b.v !== 1 || !isId(b.report) || !isId(b.install) || !Array.isArray(b.skills) || b.skills.length > MAX_SKILLS)
    return json({ error: 'bad report' }, 400);

  const outcome = ['win', 'lose', 'quit'].includes(b.outcome) ? b.outcome : 'quit';
  const head = await env.DB.prepare(
    `INSERT OR IGNORE INTO battles (report, received_at, install, version, battle, difficulty, outcome, turns, flags, seconds)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
    .bind(b.report, new Date().toISOString(), b.install, text(b.version, 32), int(b.battle, 99999), int(b.difficulty, 99),
          outcome, int(b.turns, 99999), JSON.stringify(b.flags ?? {}).slice(0, 2048),
          Number.isFinite(b.seconds) ? int(b.seconds, 86400) : null)   // 걸린 시간 — 옛 판 게임은 안 보낸다
    .run();
  if (!head.meta.changes) return json({ ok: true, duplicate: true });   // 이미 받은 것 — 다시 보낸 것이다

  const row = env.DB.prepare(
    `INSERT INTO skill_stats (report, chr, ability, work, level, uses, hits, damage, kills, heal) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`);
  const rows = b.skills
    .filter((s) => s && typeof s === 'object')
    .map((s) => row.bind(b.report, int(s.chr, 99999), int(s.ability, 99999), int(s.work, 99999), int(s.level, 99),
                         int(s.uses, 100000), int(s.hits, 100000), int(s.damage), int(s.kills, 100000), int(s.heal)));
  if (rows.length) await env.DB.batch(rows);
  return json({ ok: true });
}

// 집계 결과를 잠깐 들고 있는다 — D1 은 훑은 줄을 모두 「읽은 줄」로 센다(하루 무료 500만 줄).
// 대시보드는 기간 안의 battles 를 한 번(어빌리티 · 인물 탭은 skill_stats 도 한 번) 읽고, 같은 조건은 CACHE_MS 동안 cache 표의 한 줄로 돌려준다.
// workers.dev 에서는 Cache API 가 안 들으니 D1 의 작은 표에 둔다. 같은 isolate 안에서는 메모리에서 바로 준다.
// 통계는 몇 시간 늦어도 괜찮다 — 15분이던 때 하루 읽기 한도(500만 줄)를 넘었다(2026-10-31).
const CACHE_MS = 6 * 60 * 60 * 1000;
const memo = new Map();

async function cached(env, key, make) {
  const now = Date.now();
  const hit = memo.get(key);
  if (hit && now - hit.at < CACHE_MS) return hit.body;
  try {
    const row = await env.DB.prepare('SELECT at, body FROM cache WHERE key = ?').bind(key).first();
    if (row && now - row.at < CACHE_MS) { memo.set(key, row); return row.body; }
  } catch {
    await env.DB.exec('CREATE TABLE IF NOT EXISTS cache (key TEXT PRIMARY KEY, at INTEGER NOT NULL, body TEXT NOT NULL)');
  }
  const body = JSON.stringify(await make());
  await env.DB.prepare('INSERT OR REPLACE INTO cache (key, at, body) VALUES (?, ?, ?)').bind(key, now, body).run();
  memo.set(key, { at: now, body });
  return body;
}

const jsonText = (body) => new Response(body, { headers: { 'content-type': 'application/json; charset=utf-8' } });

async function getTop(env) {
  const { results } = await env.DB.prepare(
    `SELECT ability, work, SUM(uses) AS uses, SUM(hits) AS hits, SUM(damage) AS damage, SUM(kills) AS kills, SUM(heal) AS heal,
            COUNT(DISTINCT report) AS battles
     FROM skill_stats GROUP BY ability, work ORDER BY uses DESC LIMIT 200`).all();
  const total = await env.DB.prepare(`SELECT COUNT(*) AS battles, COUNT(DISTINCT install) AS installs FROM battles`).first();
  return { total, skills: results };
}

// 대시보드 집계 — 기간(days: 최근 N일, 0 이면 전체 · 기본 7) 안의 판만 읽는다. battles_received 색인(migrations/0003)으로 그 앞 줄만 훑는다.
// part=battles : 전투 표를 한 번만 읽어(판마다 한 줄) 타일 · 버전 목록 · 분기 · 날짜 · 환경 · 모드를 여기서 센다 — 예전에는 집계마다 표를 다시 훑어 17번 읽었다.
// part=skills  : 어빌리티 · 인물 탭을 열 때만 — skill_stats 를 기간 안 판과 이어 한 번 읽는다.
// 거르기: 버전 · 타키리온 개편(tachyrion=1 켠 판 · 0 끈 판) · 캐릭터 에디터 뺌(noedit=1). 그 칸이 생기기 전 판(v0.34 까지)은 개편을 끈 판으로 센다.
const PARTS = ['battles', 'skills'];
const DAY_CHOICES = [7, 30, 0];
const since = (days) => (days ? new Date(Date.now() - days * 86400000).toISOString() : '');

async function getDashboard(version, noedit, tachyrion, days, env, part) {
  if (part === 'skills') return getSkills(version, noedit, tachyrion, days, env);
  const rows = (await env.DB.prepare(
    `SELECT received_at, install, version, battle, outcome, turns, seconds, flags FROM battles ${days ? 'WHERE received_at >= ?' : ''}`)
    .bind(...(days ? [since(days)] : [])).all()).results;
  for (const r of rows) { try { r.f = JSON.parse(r.flags) || {}; } catch { r.f = {}; } }
  // 버전 목록은 버전 거르기 전의 것(고르는 상자에 다른 버전도 보이게).
  const versions = [...new Set(rows.map((r) => r.version))].sort((a, b) => b.localeCompare(a, undefined, { numeric: true })).slice(0, 50);
  const picked = rows.filter((r) => (!version || r.version === version) && (!noedit || r.f.charEdit !== true)
    && (tachyrion === '' || String(r.f.tachyrion ? 1 : 0) === tachyrion));

  const total = { battles: picked.length, installs: new Set(picked.map((r) => r.install)).size, wins: 0, turns: 0, secSum: 0, secN: 0 };
  for (const r of picked) { total.wins += r.outcome === 'win'; total.turns += r.turns; if (r.seconds != null) { total.secSum += r.seconds; total.secN++; } }

  // 묶음 — 키마다 판 수와 설치 수(뒤에 더 붙일 칸은 add 가).
  const group = (keyOf, add) => {
    const m = new Map();
    for (const r of picked) {
      for (const k of [].concat(keyOf(r))) {
        let g = m.get(k);
        if (!g) m.set(k, g = { k, battles: 0, inst: new Set() });
        g.battles++; g.inst.add(r.install);
        add?.(g, r);
      }
    }
    return [...m.values()].map(({ inst, ...g }) => ({ ...g, installs: inst.size }));
  };
  const top = (list, n) => list.sort((a, b) => b.battles - a.battles).slice(0, n);

  const battles = top(group((r) => r.battle, (g, r) => {
    g.wins = (g.wins || 0) + (r.outcome === 'win'); g.turnSum = (g.turnSum || 0) + r.turns;
    g.secSum = (g.secSum || 0) + (r.seconds ?? 0); g.secN = (g.secN || 0) + (r.seconds != null);
  }), 300).map((g) => ({ battle: g.k, n: g.battles, wins: g.wins, turns: g.turnSum / g.battles, secSum: g.secSum, secN: g.secN }));
  const days_ = group((r) => r.received_at.slice(0, 10) + '|' + r.version)
    .map((g) => ({ day: g.k.split('|')[0], version: g.k.split('|')[1], battles: g.battles, installs: g.installs }))
    .sort((a, b) => b.day.localeCompare(a.day) || b.version.localeCompare(a.version, undefined, { numeric: true })).slice(0, 120);

  // 환경 · 설정(flags 의 값) — 그 칸을 안 보낸 옛 판은 k 가 null 로 한 줄에 모인다(대시보드가 「자료 없음」으로 따로 적는다).
  // 조작 방식 — 기술을 패드로 더 많이 썼으면 pad, 패드를 조금이라도 썼으면 mixed, 아니면 keys.
  const control = (f) => f.pad == null ? null : f.pad > f.keys ? 'pad' : f.pad > 0 ? 'mixed' : 'keys';
  const ENV = { platform: 'platform', control, res: 'res', screen: 'screen', fullscreen: 'fullscreen', hiResUi: 'hiResUi',
                font: 'font', zoom: 'zoom', touch: 'touch', part2Dir: 'part2Dir', part1Dir: 'part1Dir' };
  const envRows = Object.fromEntries(Object.entries(ENV).map(([key, get]) =>
    [key, top(group((r) => { const v = typeof get === 'function' ? get(r.f) : r.f[get]; return v === undefined ? null : v; }), 40)]));
  // 켠 모드(flags.mods, menu-112) — 판마다 여럿. 비중의 밑은 mods 칸을 보낸 판 수.
  const withMods = picked.filter((r) => Array.isArray(r.f.mods));
  const mods = top(group((r) => (Array.isArray(r.f.mods) ? r.f.mods : [])), 60);
  const modBase = { battles: withMods.length, installs: new Set(withMods.map((r) => r.install)).size };

  return { total, versions, battles, days: days_, env: envRows, mods, modBase };
}

async function getSkills(version, noedit, tachyrion, days, env) {
  const where = [], args = [];
  if (days) { where.push('b.received_at >= ?'); args.push(since(days)); }
  if (version) { where.push('b.version = ?'); args.push(version); }
  if (noedit) where.push(`b.flags NOT LIKE '%"charEdit":true%'`);
  if (tachyrion === '1') where.push(`COALESCE(json_extract(b.flags, '$.tachyrion'), 0) = 1`);
  if (tachyrion === '0') where.push(`COALESCE(json_extract(b.flags, '$.tachyrion'), 0) = 0`);
  const rows = (await env.DB.prepare(
    `SELECT s.report, (b.battle >= 20000) AS p1, s.chr, s.ability, s.work, s.uses, s.hits, s.damage, s.kills, s.heal
     FROM battles b JOIN skill_stats s ON s.report = b.report ${where.length ? 'WHERE ' + where.join(' AND ') : ''}`).bind(...args).all()).results;
  // p1 = 파트1 전투(20000 + 번호)의 것 — 어빌리티 순위를 파트1 · 파트2 로 나눈다(menu-110).
  const sum = (keyOf, head) => {
    const m = new Map();
    for (const r of rows) {
      const k = keyOf(r);
      let g = m.get(k);
      if (!g) m.set(k, g = { ...head(r), uses: 0, hits: 0, damage: 0, kills: 0, heal: 0, reports: new Set() });
      g.uses += r.uses; g.hits += r.hits; g.damage += r.damage; g.kills += r.kills; g.heal += r.heal; g.reports.add(r.report);
    }
    return [...m.values()].map(({ reports, ...g }) => ({ ...g, battles: reports.size }));
  };
  return {
    abilities: sum((r) => `${r.p1}|${r.ability}|${r.work}`, (r) => ({ p1: r.p1, ability: r.ability, work: r.work })),
    chars: sum((r) => `${r.chr}|${r.ability}|${r.work}`, (r) => ({ chr: r.chr, ability: r.ability, work: r.work })),
  };
}

// 이름 표는 코드에 있으니 cache 표에 넣지 않고 내보낼 때 붙인다.
const NAMES = JSON.stringify({ abilities: ABILITIES, characters: CHARACTERS, battles: BATTLES, chapters: CHAPTERS, branches: BRANCHES, branchOrder: BRANCH_ORDER });

async function dashboard(url, env) {
  const version = (url.searchParams.get('version') || '').slice(0, 32);
  if (version && !/^[0-9A-Za-z.+_-]+$/.test(version)) return json({ error: 'bad version' }, 400);
  const noedit = url.searchParams.get('noedit') === '1';
  const t = url.searchParams.get('tachyrion'), tachyrion = t === '1' || t === '0' ? t : '';
  const p = url.searchParams.get('part'), part = PARTS.includes(p) ? p : 'battles';
  const d = Number(url.searchParams.get('days') ?? 7), days = DAY_CHOICES.includes(d) ? d : 7;
  const body = await cached(env, `dashboard5|${part}|${days}|${version}|${noedit ? 1 : 0}|${tachyrion}`, () => getDashboard(version, noedit, tachyrion, days, env, part));
  return jsonText(body.slice(0, -1) + ',"names":' + NAMES + '}');
}

// ── 버그 리포트 ─────────────────────────────────────────────────────────────
// 게임이 보내는 꼴: { v: 1, install, version, text, context: {...} }. context 는 게임이 붙인 것(판 · OS · 화면 · 전투 · 난이도 · 모드 · 오류 기록 끝 ≤ 20KB).
const BUG_MAX_BODY = 48 * 1024, BUG_MAX_TEXT = 8000, BUG_MAX_CONTEXT = 32 * 1024;
const BUG_PER_HOUR = 10, BUG_ALL_PER_HOUR = 300;   // 설치 하나 · 모두 합쳐 한 시간에 받는 수

async function postBug(request, env) {
  const raw = await request.text();
  if (raw.length > BUG_MAX_BODY) return json({ error: 'too large' }, 413);
  let b;
  try { b = JSON.parse(raw); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || b.v !== 1 || !isId(b.install) || typeof b.text !== 'string') return json({ error: 'bad report' }, 400);
  const body = b.text.trim();
  if (!body) return json({ error: 'empty' }, 400);
  if (body.length > BUG_MAX_TEXT) return json({ error: 'text too long' }, 413);
  const context = JSON.stringify(b.context && typeof b.context === 'object' && !Array.isArray(b.context) ? b.context : {});
  if (context.length > BUG_MAX_CONTEXT) return json({ error: 'context too large' }, 413);

  const hourAgo = new Date(Date.now() - 3600_000).toISOString();
  const recent = await env.DB.prepare(
    `SELECT COUNT(*) AS n, SUM(install = ?) AS mine FROM bug_reports WHERE created_at > ?`).bind(b.install, hourAgo).first();
  if ((recent?.mine ?? 0) >= BUG_PER_HOUR || (recent?.n ?? 0) >= BUG_ALL_PER_HOUR) return json({ error: 'rate limited' }, 429);

  const row = await env.DB.prepare(
    `INSERT INTO bug_reports (created_at, version, install, text, context) VALUES (?, ?, ?, ?, ?) RETURNING id`)
    .bind(new Date().toISOString(), text(b.version, 32), b.install, body, context).first();
  return json({ ok: true, id: row.id });
}

// 관리자 열쇠 — worker 비밀(wrangler secret put BUG_ADMIN_KEY). 없으면 아무도 못 본다. 견줄 때 걸린 시간으로 새지 않게 끝까지 본다.
function isAdmin(request, env) {
  const key = env.BUG_ADMIN_KEY;
  const got = (request.headers.get('authorization') || '').replace(/^Bearer\s+/i, '');
  if (typeof key !== 'string' || key.length < 16 || got.length !== key.length) return false;
  let diff = 0;
  for (let i = 0; i < key.length; i++) diff |= key.charCodeAt(i) ^ got.charCodeAt(i);
  return diff === 0;
}

const STATUS = /^[a-z][a-z_-]{0,19}$/;

async function listBugs(url, env) {
  const since = int(Number(url.searchParams.get('since') || 0));
  const status = url.searchParams.get('status') || 'new';
  const limit = Math.max(1, Math.min(200, int(Number(url.searchParams.get('limit') || 100))));
  if (status !== 'all' && !STATUS.test(status)) return json({ error: 'bad status' }, 400);
  const sql = `SELECT id, created_at, version, install, text, context, status, note FROM bug_reports WHERE id > ?`
    + (status === 'all' ? '' : ' AND status = ?') + (url.searchParams.get('order') === 'desc' ? ' ORDER BY id DESC LIMIT ?' : ' ORDER BY id LIMIT ?');   // desc — 대시보드는 새 것부터
  const args = status === 'all' ? [since, limit] : [since, status, limit];
  const { results } = await env.DB.prepare(sql).bind(...args).all();
  const bugs = results.map((r) => {
    let context = {};
    try { context = JSON.parse(r.context); } catch { context = { raw: r.context }; }
    return { ...r, context };
  });
  return json({ bugs });
}

async function setBugStatus(id, request, env) {
  let b;
  try { b = JSON.parse(await request.text()); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || typeof b.status !== 'string' || !STATUS.test(b.status)) return json({ error: 'bad status' }, 400);
  const note = typeof b.note === 'string' ? b.note.slice(0, 2000) : null;
  const done = await env.DB.prepare(`UPDATE bug_reports SET status = ?, note = COALESCE(?, note) WHERE id = ?`).bind(b.status, note, id).run();
  if (!done.meta.changes) return json({ error: 'not found' }, 404);
  return json({ ok: true, id, status: b.status });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url), { pathname } = url;
    try {
      if (request.method === 'POST' && pathname === '/v1/battle') return await postBattle(request, env);
      if (request.method === 'GET' && pathname === '/v1/top') return jsonText(await cached(env, 'top', () => getTop(env)));
      if (request.method === 'GET' && pathname === '/v1/dashboard') return await dashboard(url, env);
      if (request.method === 'POST' && pathname === '/v1/bug') return await postBug(request, env);
      if (pathname === '/v1/bugs' || pathname.startsWith('/v1/bugs/')) {
        if (!isAdmin(request, env)) return json({ error: 'unauthorized' }, 401);
        if (request.method === 'GET' && pathname === '/v1/bugs') return await listBugs(url, env);
        const m = pathname.match(/^\/v1\/bugs\/(\d{1,12})\/status$/);
        if (request.method === 'POST' && m) return await setBugStatus(Number(m[1]), request, env);
        return json({ error: 'not found' }, 404);
      }
      if (request.method === 'GET' && (pathname === '/dashboard' || pathname === '/'))
        return new Response(DASHBOARD_HTML, { headers: { 'content-type': 'text/html; charset=utf-8' } });
      return json({ error: 'not found' }, 404);
    } catch (e) {
      return json({ error: 'server error' }, 500);
    }
  },
};
