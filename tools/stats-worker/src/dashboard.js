// 대시보드 — /dashboard 가 이 HTML 을 그대로 내고, 페이지가 /v1/dashboard 에서 집계를 받아 그린다.
// 바깥 라이브러리 없이 표와 막대만 쓴다.

export const DASHBOARD_HTML = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>창세기전3 파트2 — 전투 통계</title>
<style>
  :root { --bg: #0d1424; --panel: #16203a; --line: #2b3a63; --text: #e8ecf7; --dim: #93a0c2; --accent: #6ea8ff; --gold: #ffe070; --bar: #3b6fd6; --good: #5fd08a; --bad: #ff7a7a; }
  @media (prefers-color-scheme: light) { :root { --bg: #f4f6fb; --panel: #ffffff; --line: #d5dbea; --text: #1b2338; --dim: #5d6885; --accent: #2a5fd0; --gold: #a06a00; --bar: #8fb2f2; --good: #1d8a4c; --bad: #c23636; } }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.5 "Segoe UI", "Malgun Gothic", sans-serif; }
  main { max-width: 1100px; margin: 0 auto; padding: 20px 16px 60px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  h2 { font-size: 15px; margin: 0 0 10px; color: var(--gold); }
  .sub { color: var(--dim); margin-bottom: 16px; }
  .filters { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-bottom: 16px; }
  select, button { background: var(--panel); color: var(--text); border: 1px solid var(--line); border-radius: 6px; padding: 5px 10px; font: inherit; }
  button { cursor: pointer; }
  button.on { border-color: var(--accent); color: var(--accent); }
  .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin-bottom: 16px; }
  .tile, section { background: var(--panel); border: 1px solid var(--line); border-radius: 10px; padding: 14px; }
  .tile b { display: block; font-size: 24px; font-variant-numeric: tabular-nums; }
  .tile span { color: var(--dim); }
  section { margin-bottom: 16px; overflow-x: auto; }
  table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; }
  th, td { padding: 5px 8px; text-align: right; white-space: nowrap; border-bottom: 1px solid var(--line); }
  th:first-child, td:first-child, td.name, th.name { text-align: left; }
  th { color: var(--dim); font-weight: 600; }
  th.sort { cursor: pointer; }
  th.sort.on { color: var(--gold); }
  tr.chapter td { background: var(--bg); font-weight: 600; border-top: 2px solid var(--line); }
  td.indent { padding-left: 22px; }
  td.bar { width: 28%; padding-right: 0; }
  td.bar i { display: block; height: 10px; background: var(--bar); border-radius: 3px; min-width: 1px; }
  .dim { color: var(--dim); }
  .good { color: var(--good); } .bad { color: var(--bad); }
  .tabs { display: flex; gap: 6px; margin-bottom: 10px; flex-wrap: wrap; }
  #error { color: var(--bad); }
</style>
</head>
<body>
<main>
  <h1>창세기전3 파트2 — 전투 통계</h1>
  <div class="sub">익명으로 수집한 전투 요약입니다. 내 편 주인공이 직접 쓴 어빌리티만 셉니다(군단 부하 · 적 제외).</div>
  <div class="filters">
    <label>버전 <select id="version"><option value="">전체</option></select></label>
    <span id="error"></span>
  </div>
  <div class="tiles" id="tiles"></div>
  <section><h2>어빌리티 순위</h2><table id="abilities"></table></section>
  <section><h2>인물별</h2><div class="tabs" id="chartabs"></div><table id="chars"></table></section>
  <section><h2>전투별</h2><table id="battles"></table></section>
  <section><h2>날짜별 · 버전별</h2><table id="days"></table></section>
</main>
<script>
const $ = (id) => document.getElementById(id);
const n = (v) => Number(v || 0).toLocaleString('ko-KR');
// 걸린 시간 — 초 합계와 잰 판 수로 평균을 내 「분:초」로. 잰 판이 없으면(옛 판 게임이 보낸 것) −.
const clock = (sum, count) => { if (!count) return '-'; const s = Math.round(sum / count); return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0'); };
const esc = (s) => String(s).replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
let data = null, sortKey = 'uses', pickedChar = null;
const state = { version: '' };

const abilityName = (r) => r.ability > 0 ? (data.names.abilities[r.ability] || '어빌리티 ' + r.ability) : r.work === 0 ? '일반 공격' : '아이템·기타 (work ' + r.work + ')';
const charName = (id) => data.names.characters[id] || '인물 ' + id;

const COLS = [['uses', '사용'], ['hits', '명중'], ['damage', '총 피해'], ['perUse', '1회당 피해'], ['kills', '처치'], ['heal', '회복'], ['battles', '전투 수']];

function abilityTable(el, rows, sortable) {
  rows = rows.map((r) => ({ ...r, perUse: r.uses ? Math.round(r.damage / r.uses) : 0 })).sort((a, b) => b[sortKey] - a[sortKey] || b.damage - a.damage);
  const top = Math.max(1, ...rows.map((r) => r[sortKey]));
  el.innerHTML = '<tr><th>#</th><th class="name">어빌리티</th>'
    + COLS.map(([k, label]) => '<th class="' + (sortable ? 'sort ' : '') + (k === sortKey ? 'on' : '') + '" data-k="' + k + '">' + label + (k === sortKey ? ' ▼' : '') + '</th>').join('')
    + '<th></th></tr>'
    + (rows.length ? rows.map((r, i) => '<tr><td class="dim">' + (i + 1) + '</td><td class="name">' + esc(abilityName(r)) + '</td>'
        + COLS.map(([k]) => '<td>' + n(r[k]) + '</td>').join('')
        + '<td class="bar"><i style="width:' + (100 * r[sortKey] / top).toFixed(1) + '%"></i></td></tr>').join('')
      : '<tr><td colspan="10" class="dim name">아직 자료가 없습니다.</td></tr>');
  if (sortable) el.querySelectorAll('th.sort').forEach((th) => th.onclick = () => { sortKey = th.dataset.k; render(); });
}

function render() {
  const t = data.total;
  $('tiles').innerHTML = [[t.battles, '전투'], [t.installs, '설치'], [t.battles ? Math.round(100 * t.wins / t.battles) + '%' : '-', '승률'], [t.battles ? Math.round(t.turns / t.battles) : '-', '평균 턴'], [clock(t.secSum, t.secN), '평균 시간']]
    .map(([v, label]) => '<div class="tile"><b>' + (typeof v === 'number' ? n(v) : v) + '</b><span>' + label + '</span></div>').join('');

  abilityTable($('abilities'), data.abilities, true);

  const ids = [...new Set(data.chars.map((r) => r.chr))].sort((a, b) => data.chars.filter((r) => r.chr === b).reduce((s, r) => s + r.uses, 0) - data.chars.filter((r) => r.chr === a).reduce((s, r) => s + r.uses, 0));
  if (!ids.includes(pickedChar)) pickedChar = ids[0] ?? null;
  $('chartabs').innerHTML = ids.map((id) => '<button data-id="' + id + '" class="' + (id === pickedChar ? 'on' : '') + '">' + esc(charName(id)) + '</button>').join('');
  $('chartabs').querySelectorAll('button').forEach((b) => b.onclick = () => { pickedChar = Number(b.dataset.id); render(); });
  abilityTable($('chars'), data.chars.filter((r) => r.chr === pickedChar), false);

  // 전투는 챕터로 묶는다 — 챕터는 이야기 순서, 그 안의 전투는 번호 순. 챕터 줄에는 그 챕터의 합계.
  const cells = (g) => '<td>' + n(g.n) + '</td><td class="good">' + n(g.wins) + '</td><td class="bad">' + n(g.n - g.wins) + '</td><td>'
    + Math.round(100 * g.wins / g.n) + '%</td><td>' + Math.round(g.turnSum / g.n) + '</td><td>' + clock(g.secSum, g.secN) + '</td>';
  const groups = new Map();
  for (const b of data.battles) {
    const [chapter, name] = data.names.battles[b.battle] || ['', ''];
    const key = chapter || '챕터 밖 · 모르는 전투';
    if (!groups.has(key)) groups.set(key, { n: 0, wins: 0, turnSum: 0, secSum: 0, secN: 0, rows: [] });
    const g = groups.get(key);
    g.n += b.n; g.wins += b.wins; g.turnSum += b.turns * b.n; g.secSum += b.secSum; g.secN += b.secN;
    g.rows.push({ ...b, name, turnSum: b.turns * b.n });
  }
  const order = (key) => { const i = data.names.chapters.indexOf(key); return i < 0 ? 9999 : i; };
  $('battles').innerHTML = '<tr><th class="name">챕터 · 전투</th><th>판 수</th><th>승</th><th>패</th><th>승률</th><th>평균 턴</th><th>평균 시간</th></tr>'
    + ([...groups.entries()].sort((a, b) => order(a[0]) - order(b[0])).map(([key, g]) =>
        '<tr class="chapter"><td class="name">' + esc(key) + '</td>' + cells(g) + '</tr>'
        + g.rows.sort((a, b) => a.battle - b.battle).map((b) => '<tr><td class="name indent"><span class="dim">Btl ' + String(b.battle).padStart(4, '0') + '</span> '
            + esc(b.name || '') + '</td>' + cells(b) + '</tr>').join('')).join('')
      || '<tr><td colspan="7" class="dim name">아직 자료가 없습니다.</td></tr>');

  $('days').innerHTML = '<tr><th class="name">날짜</th><th class="name">버전</th><th>전투</th><th>설치</th></tr>'
    + data.days.map((d) => '<tr><td class="name">' + esc(d.day) + '</td><td class="name">' + esc(d.version) + '</td><td>' + n(d.battles) + '</td><td>' + n(d.installs) + '</td></tr>').join('');
}

async function load() {
  $('error').textContent = '';
  try {
    const q = new URLSearchParams({ version: state.version });
    const res = await fetch('/v1/dashboard?' + q);
    if (!res.ok) throw new Error('HTTP ' + res.status);
    data = await res.json();
    const sel = $('version'), keep = state.version;
    sel.innerHTML = '<option value="">전체</option>' + data.versions.map((v) => '<option>' + esc(v) + '</option>').join('');
    sel.value = keep;
    render();
  } catch (e) { $('error').textContent = '불러오지 못했습니다: ' + e.message; }
}
$('version').onchange = (e) => { state.version = e.target.value; load(); };
load();
</script>
</body>
</html>`;
