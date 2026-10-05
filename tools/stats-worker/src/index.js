// 전투 통계를 받는 Cloudflare Worker — 게임(duel-dx/System/BattleStats.cs)이 전투가 끝날 때 요약 한 덩이를 보낸다.
//
//   POST /v1/battle   전투 한 판의 요약(JSON)을 D1 에 넣는다. 같은 report 번호는 한 번만 들어간다(다시 보내도 안 겹친다).
//   GET  /v1/top      어빌리티별 합계(쓴 횟수 · 피해 · 처치) — 모은 것 전체의 집계만 낸다.
//
// 누가 보냈는지는 설치할 때 만든 무작위 번호(install)뿐이다. IP 는 적지 않는다.

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
    `INSERT OR IGNORE INTO battles (report, received_at, install, version, battle, difficulty, outcome, turns, flags)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`)
    .bind(b.report, new Date().toISOString(), b.install, text(b.version, 32), int(b.battle, 99999), int(b.difficulty, 99),
          outcome, int(b.turns, 99999), JSON.stringify(b.flags ?? {}).slice(0, 1024))
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

export default {
  async fetch(request, env) {
    const { pathname } = new URL(request.url);
    try {
      if (request.method === 'POST' && pathname === '/v1/battle') return await postBattle(request, env);
      if (request.method === 'GET' && pathname === '/v1/top') return await getTop(env);
      return json({ error: 'not found' }, 404);
    } catch (e) {
      return json({ error: 'server error' }, 500);
    }
  },
};
