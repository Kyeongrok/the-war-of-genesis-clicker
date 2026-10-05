// 전투 통계를 받는 Cloudflare Worker — 게임(duel-dx/System/BattleStats.cs)이 전투가 끝날 때 요약 한 덩이를 보낸다.
//
//   POST /v1/battle   전투 한 판의 요약(JSON)을 D1 에 넣는다. 같은 report 번호는 한 번만 들어간다(다시 보내도 안 겹친다).
//   GET  /v1/top      어빌리티별 합계(쓴 횟수 · 피해 · 처치) — 모은 것 전체의 집계만 낸다.
//   GET  /dashboard   대시보드 페이지(dashboard.js) — /v1/dashboard?version= 에서 집계를 받아 그린다. 지금은 누구나 볼 수 있다.
//
// 누가 보냈는지는 설치할 때 만든 무작위 번호(install)뿐이다. IP 는 적지 않는다.

import { DASHBOARD_HTML } from './dashboard.js';
import { ABILITIES, CHARACTERS, BATTLES, CHAPTERS } from './names.js';

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
          outcome, int(b.turns, 99999), JSON.stringify(b.flags ?? {}).slice(0, 1024),
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

async function getTop(env) {
  const { results } = await env.DB.prepare(
    `SELECT ability, work, SUM(uses) AS uses, SUM(hits) AS hits, SUM(damage) AS damage, SUM(kills) AS kills, SUM(heal) AS heal,
            COUNT(DISTINCT report) AS battles
     FROM skill_stats GROUP BY ability, work ORDER BY uses DESC LIMIT 200`).all();
  const total = await env.DB.prepare(`SELECT COUNT(*) AS battles, COUNT(DISTINCT install) AS installs FROM battles`).first();
  return json({ total, skills: results });
}

// 대시보드 집계 — 버전(없으면 전체)으로 거른다. 캐릭터 에디터를 쓴 판도 넣는다(사용자 결정) — noedit=1 을 줄 때만 뺀다.
async function getDashboard(url, env) {
  const version = (url.searchParams.get('version') || '').slice(0, 32);
  const noedit = url.searchParams.get('noedit') === '1';
  const where = [], args = [];
  if (version) { where.push('b.version = ?'); args.push(version); }
  if (noedit) where.push(`b.flags NOT LIKE '%"charEdit":true%'`);
  const cond = where.length ? 'WHERE ' + where.join(' AND ') : '';
  const sums = 'SUM(s.uses) AS uses, SUM(s.hits) AS hits, SUM(s.damage) AS damage, SUM(s.kills) AS kills, SUM(s.heal) AS heal, COUNT(DISTINCT s.report) AS battles';
  const all = (sql) => env.DB.prepare(sql).bind(...args).all().then((r) => r.results);
  const [total, abilities, chars, battles, days, versions] = await Promise.all([
    env.DB.prepare(`SELECT COUNT(*) AS battles, COUNT(DISTINCT install) AS installs, COALESCE(SUM(outcome = 'win'), 0) AS wins, COALESCE(SUM(turns), 0) AS turns, COALESCE(SUM(seconds), 0) AS secSum, COUNT(seconds) AS secN FROM battles b ${cond}`).bind(...args).first(),
    all(`SELECT s.ability, s.work, ${sums} FROM skill_stats s JOIN battles b ON b.report = s.report ${cond} GROUP BY s.ability, s.work`),
    all(`SELECT s.chr, s.ability, s.work, ${sums} FROM skill_stats s JOIN battles b ON b.report = s.report ${cond} GROUP BY s.chr, s.ability, s.work`),
    all(`SELECT b.battle, COUNT(*) AS n, SUM(b.outcome = 'win') AS wins, AVG(b.turns) AS turns, COALESCE(SUM(b.seconds), 0) AS secSum, COUNT(b.seconds) AS secN FROM battles b ${cond} GROUP BY b.battle ORDER BY n DESC, b.battle LIMIT 300`),
    all(`SELECT substr(b.received_at, 1, 10) AS day, b.version, COUNT(*) AS battles, COUNT(DISTINCT b.install) AS installs FROM battles b ${cond} GROUP BY day, b.version ORDER BY day DESC, b.version DESC LIMIT 120`),
    env.DB.prepare('SELECT DISTINCT version FROM battles ORDER BY version DESC LIMIT 50').all().then((r) => r.results.map((v) => v.version)),
  ]);
  return json({ total, abilities, chars, battles, days, versions, names: { abilities: ABILITIES, characters: CHARACTERS, battles: BATTLES, chapters: CHAPTERS } });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url), { pathname } = url;
    try {
      if (request.method === 'POST' && pathname === '/v1/battle') return await postBattle(request, env);
      if (request.method === 'GET' && pathname === '/v1/top') return await getTop(env);
      if (request.method === 'GET' && pathname === '/v1/dashboard') return await getDashboard(url, env);
      if (request.method === 'GET' && (pathname === '/dashboard' || pathname === '/'))
        return new Response(DASHBOARD_HTML, { headers: { 'content-type': 'text/html; charset=utf-8' } });
      return json({ error: 'not found' }, 404);
    } catch (e) {
      return json({ error: 'server error' }, 500);
    }
  },
};
