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
// 대시보드 한 번이 battles 를 열세 번 · skill_stats 를 두 번 다 훑어 수십만 줄이 나가므로, 같은 조건은 CACHE_MS 동안 cache 표의 한 줄로 돌려준다.
// workers.dev 에서는 Cache API 가 안 들으니 D1 의 작은 표에 둔다. 같은 isolate 안에서는 메모리에서 바로 준다.
const CACHE_MS = 15 * 60 * 1000;
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

// 대시보드 집계 — 버전(없으면 전체)과 타키리온 개편 모드(tachyrion=1 켠 판 · 0 끈 판, 없으면 전체)로 거른다.
// 그 칸이 생기기 전 판(v0.34 까지)은 모드가 없었으니 끈 판으로 센다. 캐릭터 에디터를 쓴 판도 넣는다(사용자 결정) — noedit=1 을 줄 때만 뺀다.
async function getDashboard(version, noedit, tachyrion, env) {
  const where = [], args = [];
  if (version) { where.push('b.version = ?'); args.push(version); }
  if (noedit) where.push(`b.flags NOT LIKE '%"charEdit":true%'`);
  if (tachyrion === '1') where.push(`COALESCE(json_extract(b.flags, '$.tachyrion'), 0) = 1`);
  if (tachyrion === '0') where.push(`COALESCE(json_extract(b.flags, '$.tachyrion'), 0) = 0`);
  const cond = where.length ? 'WHERE ' + where.join(' AND ') : '';
  const sums = 'SUM(s.uses) AS uses, SUM(s.hits) AS hits, SUM(s.damage) AS damage, SUM(s.kills) AS kills, SUM(s.heal) AS heal, COUNT(DISTINCT s.report) AS battles';
  const all = (sql) => env.DB.prepare(sql).bind(...args).all().then((r) => r.results);
  // 환경 · 설정(flags 의 값)별 판 수와 설치 수 — 그 칸을 안 보낸 옛 판은 k 가 NULL 로 한 줄에 모인다(대시보드가 「자료 없음」으로 따로 적는다).
  const flag = (name) => `json_extract(b.flags, '$.${name}')`;
  const dim = (expr) => all(`SELECT ${expr} AS k, COUNT(*) AS battles, COUNT(DISTINCT b.install) AS installs FROM battles b ${cond} GROUP BY k ORDER BY battles DESC LIMIT 40`);
  // 조작 방식 — 그 판에서 기술을 패드로 더 많이 썼으면 pad, 패드를 조금이라도 썼으면 mixed, 아니면 keys.
  const control = `CASE WHEN ${flag('pad')} IS NULL THEN NULL WHEN ${flag('pad')} > ${flag('keys')} THEN 'pad' WHEN ${flag('pad')} > 0 THEN 'mixed' ELSE 'keys' END`;
  const ENV = { platform: flag('platform'), control, res: flag('res'), screen: flag('screen'), fullscreen: flag('fullscreen'), hiResUi: flag('hiResUi'),
                font: flag('font'), zoom: flag('zoom'), touch: flag('touch'), part2Dir: flag('part2Dir'), part1Dir: flag('part1Dir') };
  // 켠 모드(flags.mods, menu-112) — 판마다 켠 것이 여럿이라 json_each 로 펼친다. mods 칸을 보낸 판 수도 함께(비중의 밑).
  const mods = all(`SELECT j.value AS k, COUNT(*) AS battles, COUNT(DISTINCT b.install) AS installs FROM battles b, json_each(b.flags, '$.mods') j ${cond} GROUP BY k ORDER BY battles DESC LIMIT 60`);
  const modBase = env.DB.prepare(`SELECT COUNT(*) AS battles, COUNT(DISTINCT b.install) AS installs FROM battles b ${cond ? cond + ' AND' : 'WHERE'} json_type(b.flags, '$.mods') = 'array'`).bind(...args).first();
  const envRows = Promise.all(Object.values(ENV).map(dim)).then((lists) => Object.fromEntries(Object.keys(ENV).map((key, i) => [key, lists[i]])));
  const [total, abilities, chars, battles, days, versions, env_] = await Promise.all([
    env.DB.prepare(`SELECT COUNT(*) AS battles, COUNT(DISTINCT install) AS installs, COALESCE(SUM(outcome = 'win'), 0) AS wins, COALESCE(SUM(turns), 0) AS turns, COALESCE(SUM(seconds), 0) AS secSum, COUNT(seconds) AS secN FROM battles b ${cond}`).bind(...args).first(),
    // p1 = 파트1 전투(20000 + 번호)의 것 — 대시보드가 어빌리티 순위를 파트1 · 파트2 로 나눈다(menu-110)
    all(`SELECT (b.battle >= 20000) AS p1, s.ability, s.work, ${sums} FROM skill_stats s JOIN battles b ON b.report = s.report ${cond} GROUP BY p1, s.ability, s.work`),
    all(`SELECT s.chr, s.ability, s.work, ${sums} FROM skill_stats s JOIN battles b ON b.report = s.report ${cond} GROUP BY s.chr, s.ability, s.work`),
    all(`SELECT b.battle, COUNT(*) AS n, SUM(b.outcome = 'win') AS wins, AVG(b.turns) AS turns, COALESCE(SUM(b.seconds), 0) AS secSum, COUNT(b.seconds) AS secN FROM battles b ${cond} GROUP BY b.battle ORDER BY n DESC, b.battle LIMIT 300`),
    all(`SELECT substr(b.received_at, 1, 10) AS day, b.version, COUNT(*) AS battles, COUNT(DISTINCT b.install) AS installs FROM battles b ${cond} GROUP BY day, b.version ORDER BY day DESC, b.version DESC LIMIT 120`),
    env.DB.prepare('SELECT DISTINCT version FROM battles ORDER BY version DESC LIMIT 50').all().then((r) => r.results.map((v) => v.version)),
    envRows,
  ]);
  return { total, abilities, chars, battles, days, versions, env: env_, mods: await mods, modBase: await modBase };
}

// 이름 표는 코드에 있으니 cache 표에 넣지 않고 내보낼 때 붙인다.
const NAMES = JSON.stringify({ abilities: ABILITIES, characters: CHARACTERS, battles: BATTLES, chapters: CHAPTERS, branches: BRANCHES, branchOrder: BRANCH_ORDER });

async function dashboard(url, env) {
  const version = (url.searchParams.get('version') || '').slice(0, 32);
  if (version && !/^[0-9A-Za-z.+_-]+$/.test(version)) return json({ error: 'bad version' }, 400);
  const noedit = url.searchParams.get('noedit') === '1';
  const t = url.searchParams.get('tachyrion'), tachyrion = t === '1' || t === '0' ? t : '';
  const body = await cached(env, `dashboard3|${version}|${noedit ? 1 : 0}|${tachyrion}`, () => getDashboard(version, noedit, tachyrion, env));
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
    + (status === 'all' ? '' : ' AND status = ?') + ' ORDER BY id LIMIT ?';
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
