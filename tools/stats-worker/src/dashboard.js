// 대시보드 — /dashboard 가 이 HTML 을 그대로 내고, 페이지가 /v1/dashboard 에서 집계를 받아 그린다.
// 바깥 라이브러리 없이 표와 막대만 쓴다.

export const DASHBOARD_HTML = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>창세기전3 — 전투 통계</title>
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
  .envgrid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 14px 24px; }
  .envgrid h3 { font-size: 13px; margin: 0 0 4px; color: var(--accent); }
  .envgrid td.bar { width: 30%; }
  .tabs { display: flex; gap: 6px; margin-bottom: 10px; flex-wrap: wrap; }
  #error { color: var(--bad); }
</style>
</head>
<body>
<main>
  <h1>창세기전3 — 전투 통계 (파트1 · 파트2)</h1>
  <div class="sub">익명으로 수집한 전투 요약입니다. 내 편 주인공이 직접 쓴 어빌리티만 셉니다(군단 부하 · 적 제외).</div>
  <div class="filters">
    <label>버전 <select id="version"><option value="">전체</option></select></label>
    <label>타키리온 개편 <select id="tachyrion"><option value="">전체</option><option value="1">켠 판</option><option value="0">끈 판</option></select></label>
    <span id="error"></span>
  </div>
  <div class="tiles" id="tiles"></div>
  <section><h2>어빌리티 순위 · 파트2 (상위 20)</h2><table id="abilities"></table></section>
  <section><h2>어빌리티 순위 · 파트1 (상위 20)</h2><table id="abilities1"></table></section>
  <section><h2>인물별 (파트마다 많이 쓴 20명 · 어빌리티 상위 20)</h2><div class="sub">파트2</div><div class="tabs" id="chartabs"></div><div class="sub">파트1</div><div class="tabs" id="chartabs1"></div><table id="chars"></table></section>
  <section><h2>분기별</h2><table id="battles"></table></section>
  <section><h2>환경 · 설정</h2><div class="sub">기기 종류 · 화면 설정은 v0.36.0 부터, 조작 방식은 v0.31.1 부터 들어옵니다. 비중은 그 값을 보낸 판 가운데서입니다.</div><div class="envgrid" id="env"></div></section>
  <section><h2>날짜별 · 버전별</h2><table id="days"></table></section>
</main>
<script>
const $ = (id) => document.getElementById(id);
const n = (v) => Number(v || 0).toLocaleString('ko-KR');
// 걸린 시간 — 초 합계와 잰 판 수로 평균을 내 「분:초」로. 잰 판이 없으면(옛 판 게임이 보낸 것) −.
const clock = (sum, count) => { if (!count) return '-'; const s = Math.round(sum / count); return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0'); };
const esc = (s) => String(s).replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
let data = null, sortKey = 'uses', pickedChar = null;
const state = { version: '', tachyrion: '' };

const abilityName = (r) => r.ability > 0 ? (data.names.abilities[r.ability] || '어빌리티 ' + r.ability) : r.work === 0 ? '일반 공격' : '아이템·기타 (work ' + r.work + ')';
const charName = (id) => data.names.characters[id] || '인물 ' + id;

const COLS = [['uses', '사용'], ['hits', '명중'], ['damage', '총 피해'], ['perUse', '1회당 피해'], ['kills', '처치'], ['heal', '회복'], ['battles', '전투 수']];

function abilityTable(el, rows, sortable, limit = Infinity) {
  rows = rows.map((r) => ({ ...r, perUse: r.uses ? Math.round(r.damage / r.uses) : 0 })).sort((a, b) => b[sortKey] - a[sortKey] || b.damage - a.damage).slice(0, limit);
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

// 환경 · 설정 표들 — [자료 열쇠, 제목, 값을 사람이 읽는 말로].
const onOff = (k) => (k ? '켬' : '끔');
const ENV_TABLES = [
  ['platform', '기기 종류', (k) => ({ desktop: '데스크톱', laptop: '노트북', umpc: '휴대용 PC (UMPC)', deck: '스팀 덱', wine: 'Wine (리눅스 · 안드로이드 등)' }[k] || k)],
  ['control', '조작 방식', (k) => ({ pad: '패드 위주', mixed: '패드 + 키보드 · 마우스', keys: '키보드 · 마우스' }[k] || k)],
  ['res', '해상도 (설정)', (k) => String(k).replace('x', '×')],
  ['screen', '모니터 크기', (k) => String(k).replace('x', '×')],
  ['fullscreen', '전체화면', onOff],
  ['hiResUi', '새 UI', onOff],
  ['font', '글자 크기', (k) => k + '%'],
  ['zoom', '배율', (k) => (Number(k) === 0 ? '자동' : k + '%')],
  ['touch', '터치 화면', (k) => (k ? '있음' : '없음')],
  // 원본 설치본 폴더의 마지막 이름(menu-111) — 앞 경로는 안 받는다. 없음 = 그 파트 원본을 모르는 판.
  ['part2Dir', '파트2 설치본 폴더', (k) => String(k)],
  ['part1Dir', '파트1 설치본 폴더', (k) => String(k)],
];

function envTables() {
  $('env').innerHTML = ENV_TABLES.map(([key, title, label]) => {
    const rows = (data.env && data.env[key]) || [];
    const known = rows.filter((r) => r.k !== null), none = rows.find((r) => r.k === null);
    const sum = known.reduce((s, r) => s + r.battles, 0);
    return '<div><h3>' + title + '</h3><table><tr><th class="name">값</th><th>전투</th><th>설치</th><th>비중</th><th></th></tr>'
      + (known.length ? known.map((r) => '<tr><td class="name">' + esc(label(r.k)) + '</td><td>' + n(r.battles) + '</td><td>' + n(r.installs) + '</td><td>'
          + (100 * r.battles / sum).toFixed(1) + '%</td><td class="bar"><i style="width:' + (100 * r.battles / sum).toFixed(1) + '%"></i></td></tr>').join('')
        : '<tr><td colspan="5" class="dim name">아직 자료가 없습니다.</td></tr>')
      + (none ? '<tr><td class="name dim">자료 없음 (옛 판)</td><td class="dim">' + n(none.battles) + '</td><td class="dim">' + n(none.installs) + '</td><td></td><td></td></tr>' : '')
      + '</table></div>';
  }).join('');
}

function render() {
  const t = data.total;
  $('tiles').innerHTML = [[t.battles, '전투'], [t.installs, '설치'], [t.battles ? Math.round(100 * t.wins / t.battles) + '%' : '-', '승률'], [t.battles ? Math.round(t.turns / t.battles) : '-', '평균 턴'], [clock(t.secSum, t.secN), '평균 시간']]
    .map(([v, label]) => '<div class="tile"><b>' + (typeof v === 'number' ? n(v) : v) + '</b><span>' + label + '</span></div>').join('');

  // 파트1 · 파트2 를 나눠 상위 20 개씩만(사용자 요청 menu-110). 옛 캐시(p1 칸이 없는 것)는 모두 파트2 로 본다.
  abilityTable($('abilities'), data.abilities.filter((r) => !r.p1), true, 20);
  abilityTable($('abilities1'), data.abilities.filter((r) => r.p1), true, 20);

  // 인물도 파트로 나눈다 — 파트1 인물은 20000 + 번호. 파트마다 쓴 횟수가 많은 20명, 고른 인물의 어빌리티는 상위 20개(menu-110).
  const usesOf = new Map();
  for (const r of data.chars) usesOf.set(r.chr, (usesOf.get(r.chr) || 0) + r.uses);
  const ranked = [...usesOf.keys()].sort((a, b) => usesOf.get(b) - usesOf.get(a));
  const part2 = ranked.filter((id) => id < 20000).slice(0, 20), part1 = ranked.filter((id) => id >= 20000).slice(0, 20);
  if (![...part2, ...part1].includes(pickedChar)) pickedChar = part2[0] ?? part1[0] ?? null;
  const tabs = (el, ids) => {
    el.innerHTML = ids.map((id) => '<button data-id="' + id + '" class="' + (id === pickedChar ? 'on' : '') + '">' + esc(charName(id)) + '</button>').join('')
      || '<span class="dim">아직 자료가 없습니다.</span>';
    el.querySelectorAll('button').forEach((b) => b.onclick = () => { pickedChar = Number(b.dataset.id); render(); });
  };
  tabs($('chartabs'), part2);
  tabs($('chartabs1'), part1);
  abilityTable($('chars'), data.chars.filter((r) => r.chr === pickedChar), false, 20);

  // 분기마다 판 수만 보인다(사용자 요청 menu-110) — 파트2 Episode 4 · 5, 파트1 시반 슈미터 · 크림슨 크루세이더 · 아포칼립스. 분기를 모르는 전투는 그 파트의 「분기 모름」.
  const branchOf = (id) => data.names.branches[id] || (id >= 20000 ? '파트1 · 분기 모름' : '파트2 · 분기 모름');
  const counts = new Map();
  for (const b of data.battles) counts.set(branchOf(b.battle), (counts.get(branchOf(b.battle)) || 0) + b.n);
  const order = (key) => { const i = data.names.branchOrder.indexOf(key); return i < 0 ? 9999 : i; };
  $('battles').innerHTML = '<tr><th class="name">분기</th><th>판 수</th></tr>'
    + ([...counts.entries()].sort((a, b) => order(a[0]) - order(b[0])).map(([key, count]) =>
        '<tr><td class="name">' + esc(key) + '</td><td>' + n(count) + '</td></tr>').join('')
      || '<tr><td colspan="2" class="dim name">아직 자료가 없습니다.</td></tr>');

  envTables();

  $('days').innerHTML = '<tr><th class="name">날짜</th><th class="name">버전</th><th>전투</th><th>설치</th></tr>'
    + data.days.map((d) => '<tr><td class="name">' + esc(d.day) + '</td><td class="name">' + esc(d.version) + '</td><td>' + n(d.battles) + '</td><td>' + n(d.installs) + '</td></tr>').join('');
}

async function load() {
  $('error').textContent = '';
  try {
    const q = new URLSearchParams({ version: state.version, tachyrion: state.tachyrion });
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
$('tachyrion').onchange = (e) => { state.tachyrion = e.target.value; load(); };
load();
</script>
</body>
</html>`;
