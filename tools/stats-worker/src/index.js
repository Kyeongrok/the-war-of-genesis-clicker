// 전투 통계를 받는 Cloudflare Worker — 게임(duel-dx/System/BattleStats.cs)이 전투가 끝날 때 요약 한 덩이를 보낸다.
//
//   POST /v1/battle   전투 한 판의 요약(JSON)을 D1 에 넣는다. 같은 report 번호는 한 번만 들어간다(다시 보내도 안 겹친다).
//   GET  /v1/top      어빌리티별 합계(쓴 횟수 · 피해 · 처치) — 모은 것 전체의 집계만 낸다.
//   GET  /dashboard   대시보드 페이지(dashboard.js) — /v1/dashboard?version= 에서 집계를 받아 그린다. 지금은 누구나 볼 수 있다.
//   POST /v1/bug      버그 리포트 한 건(게임의 「버그 리포트」 창, duel-dx/System/BugReport.cs) — {id} 를 돌려준다. 설치마다 한 시간에 10 건까지.
//   GET  /v1/bugs?since=<id>&status=new        받은 버그 리포트(관리자만 — Authorization: Bearer <BUG_ADMIN_KEY>)
//   POST /v1/bugs/<id>/status {status, note, reply}   버그 리포트의 상태를 바꾼다(관리자만) — reply 는 보낸 사람에게 보이는 답
//   GET  /v1/bugs/mine?install=<id>            그 설치가 보낸 창세기전3 리포트의 상태 · 답(게임의 「내 리포트」, 키 없이)
//   GET  /v1/fd2/summary                       용의기사2 탭의 집계 — 리포트를 상태 · 버전 · 기기 · 화면 · 날짜별로 센 것(관리자만)
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
// 직접 지정한 기간(from · to = 한국 날짜 YYYY-MM-DD, to 는 그날까지) → received_at(UTC ISO) 범위 [lo, hi).
const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;
const kstStart = (d) => new Date(d + 'T00:00:00+09:00').toISOString();
const kstNext = (d) => new Date(new Date(d + 'T00:00:00+09:00').getTime() + 86400000).toISOString();
// 기간 → SQL 조건(열 이름 col) · 묶을 값.
function rangeWhere(range, col) {
  if (range.from || range.to) {
    const w = [], a = [];
    if (range.from) { w.push(`${col} >= ?`); a.push(kstStart(range.from)); }
    if (range.to) { w.push(`${col} < ?`); a.push(kstNext(range.to)); }
    return [w, a];
  }
  return range.days ? [[`${col} >= ?`], [since(range.days)]] : [[], []];
}
// 받은 때(UTC ISO) → 한국 날짜 · 주(그 주 월요일) · 달.
const kstDay = (iso) => new Date(new Date(iso).getTime() + 9 * 3600000).toISOString().slice(0, 10);
const kstWeek = (day) => { const t = new Date(day + 'T00:00:00Z'); t.setUTCDate(t.getUTCDate() - (t.getUTCDay() + 6) % 7); return t.toISOString().slice(0, 10); };

async function getDashboard(version, noedit, tachyrion, range, env, part) {
  if (part === 'skills') return getSkills(version, noedit, tachyrion, range, env);
  const [rw, ra] = rangeWhere(range, 'received_at');
  const rows = (await env.DB.prepare(
    `SELECT received_at, install, version, battle, outcome, turns, seconds, flags FROM battles ${rw.length ? 'WHERE ' + rw.join(' AND ') : ''}`)
    .bind(...ra).all()).results;
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
  // 날짜별 탭 — 한국 날짜로 하루 · 주(월요일 시작) · 달마다, 그리고 하루 × 버전.
  for (const r of picked) { r.day = kstDay(r.received_at); }
  const winsOf = (g, r) => { g.wins = (g.wins || 0) + (r.outcome === 'win'); };
  const period = (keyOf) => group(keyOf, winsOf).map((g) => ({ key: g.k, battles: g.battles, installs: g.installs, wins: g.wins || 0 }))
    .sort((a, b) => b.key.localeCompare(a.key));
  const days_ = {
    day: period((r) => r.day).slice(0, 400),
    week: period((r) => kstWeek(r.day)).slice(0, 200),
    month: period((r) => r.day.slice(0, 7)).slice(0, 120),
    dayVersion: group((r) => r.day + '|' + r.version)
      .map((g) => ({ day: g.k.split('|')[0], version: g.k.split('|')[1], battles: g.battles, installs: g.installs }))
      .sort((a, b) => b.day.localeCompare(a.day) || b.version.localeCompare(a.version, undefined, { numeric: true })).slice(0, 400),
  };

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

async function getSkills(version, noedit, tachyrion, range, env) {
  const [where, args] = rangeWhere(range, 'b.received_at');
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
  const f = url.searchParams.get('from') || '', to = url.searchParams.get('to') || '';
  if ((f && !DATE_RE.test(f)) || (to && !DATE_RE.test(to))) return json({ error: 'bad date' }, 400);
  const range = f || to ? { from: f, to } : { days };
  const rangeKey = f || to ? `${f}~${to}` : days;
  const body = await cached(env, `dashboard6|${part}|${rangeKey}|${version}|${noedit ? 1 : 0}|${tachyrion}`, () => getDashboard(version, noedit, tachyrion, range, env, part));
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

// 용의기사2 리포트 글은 여기서 안 읽는다(사용자 요청) — 그 게임은 /v1/fd2/summary 의 집계만 본다.
const bugDb = (url, env) => (url.searchParams.get('game') === 'fd2' ? null : env.DB);

async function listBugs(url, env) {
  const since = int(Number(url.searchParams.get('since') || 0));
  const status = url.searchParams.get('status') || 'new';
  const limit = Math.max(1, Math.min(200, int(Number(url.searchParams.get('limit') || 100))));
  if (status !== 'all' && !STATUS.test(status)) return json({ error: 'bad status' }, 400);
  if (!bugDb(url, env)) return json({ error: 'not found' }, 404);
  const cols = 'id, created_at, version, install, text, context, status, note, reply';
  const sql = `SELECT ${cols} FROM bug_reports WHERE id > ?`
    + (status === 'all' ? '' : ' AND status = ?') + (url.searchParams.get('order') === 'desc' ? ' ORDER BY id DESC LIMIT ?' : ' ORDER BY id LIMIT ?');   // desc — 대시보드는 새 것부터
  const args = status === 'all' ? [since, limit] : [since, status, limit];
  const { results } = await bugDb(url, env).prepare(sql).bind(...args).all();
  const bugs = results.map((r) => {
    let context = {};
    try { context = JSON.parse(r.context); } catch { context = { raw: r.context }; }
    return { ...r, context };
  });
  return json({ bugs });
}

// 내 리포트 — 게임이 제 설치 번호로 자기가 보낸 리포트의 상태와 답을 본다(관리자 키 없이). 설치 번호는 무작위라 남의 것은 못 고른다.
// 글은 앞 300자만, 같이 온 것 · 관리자 메모(note)는 안 준다.
async function myBugs(url, env) {
  const install = url.searchParams.get('install');
  if (!isId(install)) return json({ error: 'bad install' }, 400);
  const { results } = await env.DB.prepare(
    `SELECT id, created_at, version, substr(text, 1, 300) AS text, status, reply FROM bug_reports WHERE install = ? ORDER BY id DESC LIMIT 50`)
    .bind(install).all();
  return json({ bugs: results });
}

async function setBugStatus(id, request, url, env) {
  let b;
  try { b = JSON.parse(await request.text()); } catch { return json({ error: 'bad json' }, 400); }
  if (!b || typeof b.status !== 'string' || !STATUS.test(b.status)) return json({ error: 'bad status' }, 400);
  const note = typeof b.note === 'string' ? b.note.slice(0, 2000) : null;
  // reply — 보낸 사람이 「내 리포트」에서 보는 답(창세기전3만). 빈 글이면 지운다, 없으면 그대로 둔다.
  const reply = typeof b.reply === 'string' ? b.reply.trim().slice(0, 2000) : null;
  if (!bugDb(url, env)) return json({ error: 'not found' }, 404);
  const done = reply === null
    ? await env.DB.prepare(`UPDATE bug_reports SET status = ?, note = COALESCE(?, note) WHERE id = ?`).bind(b.status, note, id).run()
    : await env.DB.prepare(`UPDATE bug_reports SET status = ?, note = COALESCE(?, note), reply = NULLIF(?, '') WHERE id = ?`).bind(b.status, note, reply, id).run();
  if (!done.meta.changes) return json({ error: 'not found' }, 404);
  return json({ ok: true, id, status: b.status });
}

// 용의기사2 탭의 집계(관리자만) — fd2-remake D1 의 버그 리포트를 상태 · 버전 · 기기 · 화면(장)별로, 플레이 통계(plays)를 장 · 날짜 · 버전 · 기기별로 센다.
// 줄이 적어 지금은 캐시 없이 바로 센다 — 늘어나면 cached() 로 감싼다(D1 은 훑은 줄을 모두 읽은 줄로 센다).
async function fd2Summary(env) {
  const db = env.FD2_DB;
  const group = (expr) => db.prepare(
    `SELECT ${expr} AS k, COUNT(*) AS n, SUM(status = 'new') AS fresh, COUNT(DISTINCT install) AS installs FROM bug_reports GROUP BY k ORDER BY n DESC LIMIT 50`);
  const [total, status, version, platform, where, days] = await db.batch([
    db.prepare(`SELECT COUNT(*) AS n, SUM(status = 'new') AS fresh, SUM(status = 'fixed') AS fixed, COUNT(DISTINCT install) AS installs, MAX(created_at) AS last FROM bug_reports`),
    group('status'),
    group('version'),
    group(`json_extract(context, '$.platform')`),
    // 게임 화면이면 「N장」(전투 중이면 「N장 전투」), 아니면 화면 이름(Title · Load …)
    group(`CASE WHEN json_extract(context, '$.chapter') IS NOT NULL THEN json_extract(context, '$.chapter') || '장' || CASE WHEN json_extract(context, '$.battle') THEN ' 전투' ELSE '' END ELSE json_extract(context, '$.screen') END`),
    db.prepare(`SELECT substr(created_at, 1, 10) AS k, COUNT(*) AS n, COUNT(DISTINCT install) AS installs FROM bug_reports GROUP BY k ORDER BY k DESC LIMIT 60`),
  ]);
  // 플레이 통계(plays — fd2-remake 의 POST /v1/play, 전투 한 판에 한 줄) — result 는 win · lose · quit, chapter 0 은 프롤로그, how 는 new · load
  const DAY = `substr(COALESCE(started_at, received_at), 1, 10)`;
  const [pTotal, pChapter, pDays, pVersion, pPlatform] = await db.batch([
    db.prepare(`SELECT COUNT(DISTINCT install) AS installs, COUNT(*) AS plays, SUM(result = 'win') AS wins, SUM(result = 'lose') AS loses, SUM(result = 'quit') AS quits,
                       SUM(seconds) AS secSum, MAX(CASE WHEN result = 'win' THEN chapter END) AS maxChapter, MAX(received_at) AS last FROM plays`),
    db.prepare(`SELECT chapter AS k, SUM(result = 'win') AS wins, SUM(result = 'lose') AS loses, SUM(result = 'quit') AS quits, COUNT(DISTINCT install) AS installs,
                       SUM(platform = 'android') AS phone, AVG(CASE WHEN result = 'win' THEN turns END) AS turns, AVG(CASE WHEN result = 'win' THEN seconds END) AS seconds,
                       SUM(how = 'load') AS loads
                FROM plays GROUP BY chapter ORDER BY chapter`),
    db.prepare(`SELECT ${DAY} AS k, COUNT(DISTINCT install) AS installs, COUNT(*) AS plays, SUM(seconds) AS seconds FROM plays GROUP BY k ORDER BY k DESC LIMIT 60`),
    db.prepare(`SELECT version AS k, COUNT(DISTINCT install) AS installs, COUNT(*) AS plays, SUM(seconds) AS seconds FROM plays GROUP BY k ORDER BY MAX(id) DESC LIMIT 30`),
    db.prepare(`SELECT platform || CASE WHEN mobile_view = 1 AND platform <> 'android' THEN ' · 모바일 화면' ELSE '' END AS k, COUNT(DISTINCT install) AS installs,
                       COUNT(*) AS plays, SUM(seconds) AS seconds FROM plays GROUP BY k ORDER BY installs DESC`),
  ]);
  return json({ total: total.results[0], status: status.results, version: version.results, platform: platform.results, where: where.results, days: days.results,
    plays: { total: pTotal.results[0], chapters: pChapter.results, days: pDays.results, versions: pVersion.results, platforms: pPlatform.results } });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url), { pathname } = url;
    try {
      if (request.method === 'POST' && pathname === '/v1/battle') return await postBattle(request, env);
      if (request.method === 'GET' && pathname === '/v1/top') return jsonText(await cached(env, 'top', () => getTop(env)));
      if (request.method === 'GET' && pathname === '/v1/dashboard') return await dashboard(url, env);
      if (request.method === 'POST' && pathname === '/v1/bug') return await postBug(request, env);
      if (request.method === 'GET' && pathname === '/v1/fd2/summary') {
        if (!isAdmin(request, env)) return json({ error: 'unauthorized' }, 401);
        return await fd2Summary(env);
      }
      if (request.method === 'GET' && pathname === '/v1/bugs/mine') return await myBugs(url, env);
      if (pathname === '/v1/bugs' || pathname.startsWith('/v1/bugs/')) {
        if (!isAdmin(request, env)) return json({ error: 'unauthorized' }, 401);
        if (request.method === 'GET' && pathname === '/v1/bugs') return await listBugs(url, env);
        const m = pathname.match(/^\/v1\/bugs\/(\d{1,12})\/status$/);
        if (request.method === 'POST' && m) return await setBugStatus(Number(m[1]), request, url, env);
        return json({ error: 'not found' }, 404);
      }
      if (request.method === 'GET' && (pathname === '/dashboard' || pathname === '/'))
        return new Response(DASHBOARD_HTML, { headers: { 'content-type': 'text/html; charset=utf-8' } });
      return json({ error: 'not found' }, 404);
    } catch (e) {
      // wrangler tail 로 원인을 보게 남긴다 — D1 하루 읽기 한도가 차면 여기로 온다(code 7500).
      console.error(e && e.stack || e);
      return json({ error: 'server error' }, 500);
    }
  },
};
