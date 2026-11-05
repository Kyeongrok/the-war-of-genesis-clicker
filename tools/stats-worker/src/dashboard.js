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
  h1 { font-size: 20px; margin: 0 0 4px; position: relative; }
  /* 제목의 게임 이름을 누르면 다른 게임의 통계로 바꾼다 */
  #gamepick { font: inherit; font-weight: inherit; color: inherit; background: none; border: none; border-radius: 6px; padding: 0 4px; margin-left: -4px; }
  #gamepick:hover, #gamepick[aria-expanded="true"] { background: var(--panel); }
  #gamepick::after { content: ' ▾'; color: var(--dim); font-size: 14px; }
  #gamemenu { position: absolute; top: 100%; left: 0; z-index: 3; background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 4px; min-width: 200px; box-shadow: 0 6px 20px #0006; }
  #gamemenu button { display: block; width: 100%; text-align: left; border: none; font-size: 15px; font-weight: 600; }
  #gamemenu button:hover { background: var(--bg); }
  #gamemenu button.on { color: var(--accent); }
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
  .pagetabs { margin-bottom: 16px; position: sticky; top: 0; z-index: 1; background: var(--bg); padding: 6px 0; }
  .pagetabs button { padding: 7px 14px; }
  main > [data-tab][hidden] { display: none; }
  #error { color: var(--bad); }
  .bug { border-top: 1px solid var(--line); padding: 10px 0; }
  .bug .head { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
  .bug .text { white-space: pre-wrap; margin: 6px 0; }
  .bug .reply { white-space: pre-wrap; margin: 4px 0; padding: 4px 8px; border-left: 3px solid var(--accent, #6aa0ff); }
  .bug pre { white-space: pre-wrap; word-break: break-all; font-size: 12px; color: var(--dim); max-height: 300px; overflow: auto; }
  .tag { border: 1px solid var(--line); border-radius: 4px; padding: 0 6px; font-size: 12px; }
  .tag.new { color: var(--gold); border-color: var(--gold); } .tag.fixed { color: var(--good); border-color: var(--good); }
</style>
</head>
<body>
<main>
  <h1><button id="gamepick" aria-haspopup="true" aria-expanded="false">창세기전3</button> <span id="gamesub">— 전투 통계 (파트1 · 파트2)</span>
    <div id="gamemenu" hidden><button data-game="dow">창세기전3</button><button data-game="fd2">용의기사2</button><button data-game="cds">대항해시대</button><button data-game="mc">마그나카르타</button></div></h1>
  <div class="sub" id="dowsub">익명으로 수집한 전투 요약입니다. 내 편 주인공이 직접 쓴 어빌리티만 셉니다(군단 부하 · 적 제외).</div>
  <div class="filters" id="dowfilters">
    <label>기간 <select id="period"><option value="today">오늘</option><option value="7" selected>최근 1주</option><option value="30">최근 4주</option><option value="0">전체</option><option value="custom">직접 지정</option></select></label>
    <span id="customRange" hidden><input type="date" id="from"> ~ <input type="date" id="to"></span>
    <label>버전 <select id="version"><option value="">전체</option></select></label>
    <label>타키리온 개편 <select id="tachyrion"><option value="">전체</option><option value="1">켠 판</option><option value="0">끈 판</option></select></label>
    <span id="error"></span>
  </div>
  <div class="tiles" id="tiles"></div>
  <div class="tabs pagetabs" id="pagetabs">
    <button data-tab="bugs">버그 리포트</button><button data-tab="ability">어빌리티</button><button data-tab="chars">인물별</button><button data-tab="battles">분기별</button>
    <button data-tab="env">모드 · 환경</button><button data-tab="days">날짜별</button>
  </div>
  <section data-tab="ability"><h2>어빌리티 순위 · 파트2 (상위 20)</h2><table id="abilities"></table></section>
  <section data-tab="ability"><h2>어빌리티 순위 · 파트1 (상위 20)</h2><table id="abilities1"></table></section>
  <section data-tab="chars"><h2>인물별 (파트마다 많이 쓴 20명 · 어빌리티 상위 20)</h2><div class="sub">파트2</div><div class="tabs" id="chartabs"></div><div class="sub">파트1</div><div class="tabs" id="chartabs1"></div><table id="chars"></table></section>
  <section data-tab="battles"><h2>분기별</h2><table id="battles"></table></section>
  <section data-tab="env"><h2>켠 모드</h2><div class="sub">모드 창에서 켜 둔 것입니다. 비중은 모드 칸을 보낸 판(v1.0.31 부터) 가운데 그 모드를 켠 판입니다.</div><table id="mods"></table></section>
  <section data-tab="env"><h2>환경 · 설정</h2><div class="sub">기기 종류 · 화면 설정은 v0.36.0 부터, 조작 방식은 v0.31.1 부터 들어옵니다. 비중은 그 값을 보낸 판 가운데서입니다.</div><div class="envgrid" id="env"></div></section>
  <section data-tab="bugs"><h2>버그 리포트</h2>
    <div class="sub">창세기전3 리메이크의 「버그 리포트」로 받은 글입니다(용의기사2 는 그 게임의 버그 리포트 탭에서 봅니다). 오류 기록이 들어 있어 관리자 키(BUG_ADMIN_KEY)를 넣어야 보입니다 — 키는 이 브라우저에만 남습니다.</div>
    <div class="filters"><label>상태 <select id="bugstatus"><option value="new">새로 온 것</option><option value="all">전체</option><option value="seen">봤음</option><option value="fixed">고침</option><option value="wontfix">안 고침</option></select></label>
      <button id="bugkey">관리자 키</button><button id="bugreload">새로 고침</button><span id="bugerr" class="bad"></span></div>
    <div id="bugs" class="dim">관리자 키를 넣으면 보입니다.</div></section>
  <section data-tab="days"><h2>날짜별</h2>
    <div class="filters"><label>묶음 <select id="dayUnit"><option value="day">하루</option><option value="week">주(월요일 시작)</option><option value="month">달</option><option value="dayVersion">하루 · 버전</option></select></label></div>
    <table id="daysTable"></table></section>
  <div class="tabs pagetabs" data-tab="fd2" id="fd2tabs"><button data-fd2="bugs">버그 리포트</button><button data-fd2="summary">리포트 집계</button><button data-fd2="play">플레이</button></div>
  <section data-tab="fd2" data-fd2="bugs"><h2>용의기사2 · 리포트</h2>
    <div class="filters"><label>상태 <select id="fd2status"><option value="new">새로 온 것</option><option value="all">전체</option><option value="seen">봤음</option><option value="fixed">고침</option><option value="wontfix">안 고침</option></select></label>
      <button id="fd2key">관리자 키</button><button id="fd2reload">새로 고침</button><span id="fd2err" class="bad"></span></div>
    <div id="fd2bugs" class="dim">관리자 키를 넣으면 보입니다.</div></section>
  <section data-tab="fd2" data-fd2="summary"><h2>용의기사2 · 버그 리포트 집계</h2>
    <div class="tiles" id="fd2tiles"></div>
    <div class="envgrid" id="fd2grid"></div></section>
  <section data-tab="fd2" data-fd2="play"><h2>용의기사2 리메이크 · 플레이</h2>
    <div class="sub">fd2-remake 가 전투 한 판마다 보낸 익명 기록입니다(v0.14.33 부터, 설정에서 끌 수 있음). 관리자 키(버그 리포트 탭의 것)를 넣어야 보입니다.</div>
    <div class="tiles" id="fd2ptiles"></div>
    <h2>장별</h2><div class="sub">평균 턴 · 시간은 이긴 판만입니다. 시간은 손을 댄 지 3분 안인 동안만 셉니다. 승률은 그만둔 판을 뺍니다.</div><table id="fd2chapters"></table>
    <div class="envgrid" id="fd2pgrid" style="margin-top:14px"></div></section>
  <div data-tab="cds"><cds-dashboard id="cdsdash" api="https://cds-remake-stats.oceanfog1.workers.dev"></cds-dashboard></div>
  <section data-tab="mc"><h2>마그나카르타 · 버그 리포트</h2>
    <div class="sub">마그나카르타 패치판(Kyeongrok/magnacarta-remake)의 게임 안 「버그 리포트」 창에서 온 것입니다. 관리자 키는 다른 게임과 같습니다.</div>
    <div class="filters"><label>상태 <select id="mcstatus"><option value="new">새로 온 것</option><option value="all">전체</option><option value="seen">봤음</option><option value="fixed">고침</option><option value="wontfix">안 고침</option></select></label>
      <button id="mckey">관리자 키</button><button id="mcreload">새로 고침</button><span id="mcerr" class="bad"></span></div>
    <div class="tiles" id="mctiles"></div>
    <div id="mcbugs" class="dim">관리자 키를 넣으면 보입니다.</div></section>
</main>
<script>
const $ = (id) => document.getElementById(id);
const n = (v) => Number(v || 0).toLocaleString('ko-KR');
// 걸린 시간 — 초 합계와 잰 판 수로 평균을 내 「분:초」로. 잰 판이 없으면(옛 판 게임이 보낸 것) −.
const clock = (sum, count) => { if (!count) return '-'; const s = Math.round(sum / count); return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0'); };
const esc = (s) => String(s).replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
let data = null, sortKey = 'uses', pickedChar = null, currentTab = 'bugs';
// 탭이 쓰는 집계 갈래(/v1/dashboard?part=) — 버그 리포트 탭은 집계가 없다. 받은 갈래는 거르기를 바꿀 때까지 다시 안 받는다.
const TAB_PART = { ability: 'skills', chars: 'skills' };
let loadedParts = new Set();
const state = { version: '', tachyrion: '', days: '7', from: '', to: '' };
let dayUnit = 'day';

// 아래 표들은 탭으로 나눠 하나씩 보인다 — 고른 탭은 이 브라우저에 남는다
function pickTab(tab) {
  document.querySelectorAll('main > [data-tab]').forEach((el) => el.hidden = el.dataset.tab !== tab);
  document.querySelectorAll('#pagetabs button').forEach((b) => b.classList.toggle('on', b.dataset.tab === tab));
  try { localStorage.setItem('dashTab2', tab); } catch (e) { }
  currentTab = tab;
  if (data) loadPart(tab);
  if (tab === 'fd2') setTimeout(() => { if (!fd2Loaded) loadFd2(); });   // 아래 스크립트가 다 읽힌 뒤에
  if (tab === 'mc') setTimeout(() => { if (!mcLoaded) loadMc(); });
  // 대항해시대 화면은 cds-remake-stats 가 내는 부품(<cds-dashboard>, /embed.js)이다 — 화면 코드는 그쪽 저장소(cds-helper)에 하나만 둔다.
  // 부품 코드는 처음 고를 때 한 번 불러온다(안 보는 동안 그쪽 집계를 부르지 않게). 불러오면 부품이 제자리에 그려진다.
  if (tab === 'cds' && !document.getElementById('cdsembed')) {
    const js = document.createElement('script');
    js.id = 'cdsembed';
    js.src = 'https://cds-remake-stats.oceanfog1.workers.dev/embed.js';
    document.head.appendChild(js);
  }
}

// 게임 — 제목의 게임 이름을 눌러 고른다. 용의기사2 · 대항해시대는 섹션 하나씩이라(data-tab 이 게임 이름) 창세기전3 의
// 설명 · 거르기 · 합계 타일 · 탭 줄을 숨기고 그 섹션만 보인다. 창세기전3 으로 돌아오면 마지막으로 본 탭을 다시 연다.
const GAMES = { dow: '— 전투 통계 (파트1 · 파트2)', fd2: '— 리메이크 플레이 · 버그 리포트', cds: '— 코스타 델 솔 게임 통계', mc: '— 패치판 버그 리포트' };
const GAME_NAMES = { dow: '창세기전3', fd2: '용의기사2', cds: '대항해시대', mc: '마그나카르타' };
let dowTab = 'bugs';
function pickGame(game) {
  if (!GAMES[game]) game = 'dow';
  $('gamepick').textContent = GAME_NAMES[game];
  $('gamesub').textContent = GAMES[game];
  document.title = GAME_NAMES[game] + ' 통계';
  document.querySelectorAll('#gamemenu button').forEach((b) => b.classList.toggle('on', b.dataset.game === game));
  document.querySelectorAll('#dowsub, #dowfilters, #tiles, #pagetabs').forEach((el) => el.style.display = game === 'dow' ? '' : 'none');   // hidden 은 display: flex 에 진다
  try { localStorage.setItem('dashGame', game); } catch (e) { }
  pickTab(game === 'dow' ? dowTab : game);
}
const closeGameMenu = () => { $('gamemenu').hidden = true; $('gamepick').setAttribute('aria-expanded', 'false'); };
$('gamepick').onclick = (e) => { e.stopPropagation(); const open = $('gamemenu').hidden; $('gamemenu').hidden = !open; $('gamepick').setAttribute('aria-expanded', String(open)); };
document.querySelectorAll('#gamemenu button').forEach((b) => b.onclick = () => { closeGameMenu(); pickGame(b.dataset.game); });
document.addEventListener('click', closeGameMenu);
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeGameMenu(); });

document.querySelectorAll('#pagetabs button').forEach((b) => b.onclick = () => { dowTab = b.dataset.tab; pickTab(dowTab); });

// 용의기사2 의 탭 — 버그 리포트가 맨 앞이고 처음은 그 탭이다. 섹션 안쪽 탭이라 바깥 탭(hidden)과 따로 style.display 로 가린다.
// 자료는 탭과 상관없이 한 번에 불러온다(loadFd2).
function pickFd2Tab(tab) {
  if (!document.querySelector('#fd2tabs button[data-fd2="' + tab + '"]')) tab = 'bugs';
  document.querySelectorAll('section[data-fd2]').forEach((el) => el.style.display = el.dataset.fd2 === tab ? '' : 'none');
  document.querySelectorAll('#fd2tabs button').forEach((b) => b.classList.toggle('on', b.dataset.fd2 === tab));
  try { localStorage.setItem('fd2Tab', tab); } catch (e) { }
}
document.querySelectorAll('#fd2tabs button').forEach((b) => b.onclick = () => pickFd2Tab(b.dataset.fd2));
{ let t = null; try { t = localStorage.getItem('fd2Tab'); } catch (e) { } pickFd2Tab(t || 'bugs'); }
let savedTab = null, savedGame = null, oldTab = null;
try { savedTab = localStorage.getItem('dashTab2'); oldTab = localStorage.getItem('dashTab'); savedGame = localStorage.getItem('dashGame'); } catch (e) { }
// 기본 탭은 버그 리포트다 — 그때 열쇠를 dashTab2 로 갈아, 예전(dashTab)에 고른 창세기전3 탭은 안 이어받는다.
if (document.querySelector('#pagetabs button[data-tab="' + savedTab + '"]')) dowTab = savedTab;
// 예전에는 용의기사2 · 대항해시대가 탭이었다 — 그 탭을 보던 채면 그 게임으로 연다.
pickGame(savedGame || (GAMES[oldTab] ? oldTab : 'dow'));

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
  if (!t) return;
  $('tiles').innerHTML = [[t.battles, '전투'], [t.installs, '설치'], [t.battles ? Math.round(100 * t.wins / t.battles) + '%' : '-', '승률'], [t.battles ? Math.round(t.turns / t.battles) : '-', '평균 턴'], [clock(t.secSum, t.secN), '평균 시간']]
    .map(([v, label]) => '<div class="tile"><b>' + (typeof v === 'number' ? n(v) : v) + '</b><span>' + label + '</span></div>').join('');

  // 파트1 · 파트2 를 나눠 상위 20 개씩만(사용자 요청 menu-110). 옛 캐시(p1 칸이 없는 것)는 모두 파트2 로 본다.
  if (data.abilities) {
    abilityTable($('abilities'), data.abilities.filter((r) => !r.p1), true, 20);
    abilityTable($('abilities1'), data.abilities.filter((r) => r.p1), true, 20);
  }
  if (data.chars) renderChars();
  if (data.battles) renderBattles();
  if (data.env) { renderMods(); envTables(); }
  if (data.days) renderDays();
}

function renderChars() {

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
}

function renderBattles() {

  // 분기마다 판 수만 보인다(사용자 요청 menu-110) — 파트2 Episode 4 · 5, 파트1 시반 슈미터 · 크림슨 크루세이더 · 아포칼립스. 분기를 모르는 전투는 그 파트의 「분기 모름」.
  const branchOf = (id) => data.names.branches[id] || (id >= 20000 ? '파트1 · 분기 모름' : '파트2 · 분기 모름');
  const counts = new Map();
  for (const b of data.battles) counts.set(branchOf(b.battle), (counts.get(branchOf(b.battle)) || 0) + b.n);
  const order = (key) => { const i = data.names.branchOrder.indexOf(key); return i < 0 ? 9999 : i; };
  $('battles').innerHTML = '<tr><th class="name">분기</th><th>판 수</th></tr>'
    + ([...counts.entries()].sort((a, b) => order(a[0]) - order(b[0])).map(([key, count]) =>
        '<tr><td class="name">' + esc(key) + '</td><td>' + n(count) + '</td></tr>').join('')
      || '<tr><td colspan="2" class="dim name">아직 자료가 없습니다.</td></tr>');
}

function renderMods() {
  // 켠 모드(menu-112) — 판마다 여러 개라 비중의 밑은 모드 칸을 보낸 판 수다.
  const base = (data.modBase && data.modBase.battles) || 0;
  $('mods').innerHTML = '<tr><th class="name">모드</th><th>전투</th><th>설치</th><th>비중</th><th></th></tr>'
    + ((data.mods || []).map((r) => '<tr><td class="name">' + esc(String(r.k)) + '</td><td>' + n(r.battles) + '</td><td>' + n(r.installs) + '</td><td>'
        + (base ? (100 * r.battles / base).toFixed(1) + '%' : '-') + '</td><td class="bar"><i style="width:' + (base ? (100 * r.battles / base).toFixed(1) : 0) + '%"></i></td></tr>').join('')
      || '<tr><td colspan="5" class="dim name">아직 자료가 없습니다 — 모드 칸은 v1.0.31 판부터 들어옵니다.</td></tr>');
}

// 날짜별 탭 — 한국 날짜로 하루 · 주 · 달마다(기간 안), 또는 하루 × 버전.
function renderDays() {
  const d = data.days || {};
  if (dayUnit === 'dayVersion') {
    $('daysTable').innerHTML = '<tr><th class="name">날짜</th><th class="name">버전</th><th>전투</th><th>설치</th></tr>'
      + (d.dayVersion || []).map((r) => '<tr><td class="name">' + esc(r.day) + '</td><td class="name">' + esc(r.version) + '</td><td>' + n(r.battles) + '</td><td>' + n(r.installs) + '</td></tr>').join('');
    return;
  }
  const head = { day: '날짜', week: '주(월요일)', month: '달' }[dayUnit];
  const rows = d[dayUnit] || [];
  const max = Math.max(1, ...rows.map((r) => r.battles));
  $('daysTable').innerHTML = '<tr><th class="name">' + head + '</th><th>전투</th><th>설치</th><th>승률</th><th></th></tr>'
    + (rows.map((r) => '<tr><td class="name">' + esc(r.key) + '</td><td>' + n(r.battles) + '</td><td>' + n(r.installs) + '</td><td>'
        + (r.battles ? Math.round(100 * r.wins / r.battles) + '%' : '-') + '</td><td class="bar"><i style="width:' + (100 * r.battles / max).toFixed(1) + '%"></i></td></tr>').join('')
      || '<tr><td colspan="5" class="dim name">이 기간에는 자료가 없습니다.</td></tr>');
}

async function fetchPart(part) {
  const q = new URLSearchParams({ part, version: state.version, tachyrion: state.tachyrion });
  if (state.from || state.to) { if (state.from) q.set('from', state.from); if (state.to) q.set('to', state.to); } else q.set('days', state.days);
  const res = await fetch('/v1/dashboard?' + q);
  if (!res.ok) throw new Error('HTTP ' + res.status);
  return res.json();
}

// 처음 · 거르기를 바꿀 때 — 전투 표 집계(타일 · 버전 목록 · 분기 · 날짜 · 환경 · 모드)와, 지금 탭이 어빌리티 · 인물이면 그 몫도 받는다.
async function load() {
  $('error').textContent = '';
  loadedParts = new Set();
  try {
    data = await fetchPart('battles');
    loadedParts.add('battles');
    const sel = $('version'), keep = state.version;
    sel.innerHTML = '<option value="">전체</option>' + data.versions.map((v) => '<option>' + esc(v) + '</option>').join('');
    sel.value = keep;
    render();
    await loadPart(currentTab);
  } catch (e) { $('error').textContent = '불러오지 못했습니다: ' + e.message; }
}

// 탭을 열 때 그 탭의 집계를 받는다(이미 받았으면 그대로).
async function loadPart(tab) {
  const part = TAB_PART[tab];
  if (!part || loadedParts.has(part) || !data) return;
  loadedParts.add(part);
  try {
    Object.assign(data, await fetchPart(part));
    render();
  } catch (e) { loadedParts.delete(part); $('error').textContent = '불러오지 못했습니다: ' + e.message; }
}
// ── 버그 리포트 — 관리자 키로 /v1/bugs 를 읽고, 카드마다 상태를 바꾼다 ──
const STATUS_NAME = { new: '새로', seen: '봤음', fixed: '고침', wontfix: '안 고침' };
const BUG_GAMES = { dow: '창세기전3', fd2: '용의기사2', mc: '마그나카르타' };   // 번호는 게임마다 따로 센다
const bugState = { status: 'new', game: 'dow' };   // 이 탭은 창세기전3 만 — 용의기사2 는 그 게임 화면의 탭에서
const bugKey = () => { try { return localStorage.getItem('bugAdminKey') || ''; } catch { return ''; } };
function bugCard(b) {
  const c = b.context || {};
  const when = new Date(b.created_at).toLocaleString('ko-KR');
  const where = c.where || c.scene || (c.chapter ? c.chapter + '장' + (c.turn ? ' ' + c.turn + '턴' : '') : c.screen || '');
  const buttons = Object.keys(STATUS_NAME).filter((s) => s !== b.status)
    .map((s) => '<button data-id="' + b.id + '" data-g="' + b.game + '" data-s="' + s + '">' + STATUS_NAME[s] + '</button>').join(' ')
    + (b.game === 'fd2' || b.game === 'mc' ? '' : ' <button data-reply="' + b.id + '">답 달기</button>');   // 답은 창세기전3만 — 게임의 「내 리포트」에 보인다
  return '<div class="bug"><div class="head"><span class="tag">' + BUG_GAMES[b.game] + '</span><b>#' + b.id + '</b><span class="tag ' + esc(b.status) + '">' + esc(STATUS_NAME[b.status] || b.status) + '</span>'
    + '<span class="dim">' + esc(when) + ' · v' + esc(b.version) + (where ? ' · ' + esc(where) : '') + '</span><span style="flex:1"></span>' + buttons + '</div>'
    + '<div class="text">' + esc(b.text) + '</div>'
    + (b.note ? '<div class="dim">메모: ' + esc(b.note) + '</div>' : '')
    + (b.reply ? '<div class="reply">답(보낸 분에게 보임): ' + esc(b.reply) + '</div>' : '')
    + '<details><summary class="dim">같이 온 것</summary><pre>' + esc(JSON.stringify(c, null, 2)) + '</pre></details></div>';
}
async function loadBugs() {
  const key = bugKey();
  $('bugerr').textContent = '';
  if (!key) { $('bugs').innerHTML = '<span class="dim">관리자 키를 넣으면 보입니다.</span>'; return; }
  try {
    const games = bugState.game ? [bugState.game] : Object.keys(BUG_GAMES);
    const lists = await Promise.all(games.map(async (game) => {
      const res = await fetch('/v1/bugs?game=' + game + '&status=' + bugState.status + '&order=desc&limit=200', { headers: { Authorization: 'Bearer ' + key } });
      if (res.status === 401) throw new Error('키가 맞지 않습니다');
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return ((await res.json()).bugs || []).map((b) => ({ ...b, game }));
    }));
    const bugs = lists.flat().sort((a, b) => (a.created_at < b.created_at ? 1 : -1));
    $('bugs').innerHTML = bugs.length ? bugs.map(bugCard).join('') : '<span class="dim">없습니다.</span>';
    $('bugs').querySelectorAll('button[data-id]').forEach((b) => b.onclick = () => markBug(b.dataset.g, Number(b.dataset.id), b.dataset.s));
    $('bugs').querySelectorAll('button[data-reply]').forEach((b) => b.onclick = () => replyBug(bugs.find((x) => x.game !== 'fd2' && x.id === Number(b.dataset.reply))));
  } catch (e) { $('bugerr').textContent = '불러오지 못했습니다: ' + e.message; }
}
async function markBug(game, id, status) {
  const note = status === 'fixed' || status === 'wontfix' ? prompt('메모(선택 — 고친 커밋 · 판 번호 따위)', '') : null;
  if (note === null && (status === 'fixed' || status === 'wontfix')) return;
  const res = await fetch('/v1/bugs/' + id + '/status?game=' + game, { method: 'POST', headers: { Authorization: 'Bearer ' + bugKey(), 'content-type': 'application/json' },
    body: JSON.stringify(note ? { status, note } : { status }) });
  if (!res.ok) { $('bugerr').textContent = $('fd2err').textContent = $('mcerr').textContent = '바꾸지 못했습니다: HTTP ' + res.status; return; }
  loadBugs();
  if (fd2Loaded) loadFd2();
  if (mcLoaded) loadMc();
}
// 답 — 보낸 분이 게임의 「내 리포트」에서 본다. 상태는 그대로 두고, 비우면 답을 지운다.
async function replyBug(b) {
  if (!b) return;
  const reply = prompt('보낸 분에게 보일 답(비우면 지웁니다)', b.reply || '');
  if (reply === null) return;
  const res = await fetch('/v1/bugs/' + b.id + '/status?game=' + b.game, { method: 'POST', headers: { Authorization: 'Bearer ' + bugKey(), 'content-type': 'application/json' },
    body: JSON.stringify({ status: b.status, reply }) });
  if (!res.ok) { $('bugerr').textContent = '답을 달지 못했습니다: HTTP ' + res.status; return; }
  loadBugs();
}
function askBugKey() {
  const k = prompt('관리자 키(BUG_ADMIN_KEY)', bugKey());
  if (k === null) return;
  try { localStorage.setItem('bugAdminKey', k.trim()); } catch { }
  loadBugs();
  if (currentTab === 'fd2') loadFd2();
  if (currentTab === 'mc') loadMc();
}
$('bugkey').onclick = askBugKey;

// ── 용의기사2 탭 — /v1/fd2/summary 의 집계와 fd2 리포트 목록(관리자 키). 탭을 처음 열 때 받는다 ──
let fd2Loaded = false;
const fd2Status = { status: 'new' };
const FD2_TABLES = [
  ['status', '상태', (k) => STATUS_NAME[k] || k],
  ['version', '버전', (k) => 'v' + k],
  ['platform', '기기', (k) => ({ windows: 'Windows', android: '안드로이드' }[k] || k)],
  ['where', '화면 · 장', (k) => ({ Opening: '오프닝', Title: '타이틀', Prologue: '프롤로그', Game: '게임', Ending: '엔딩' }[k] || k)],
];
function fd2Table(title, rows, label, cols) {
  const top = Math.max(1, ...rows.map((r) => r.n));
  return '<div><h3>' + title + '</h3><table><tr><th class="name">값</th>' + cols.map(([, h]) => '<th>' + h + '</th>').join('') + '<th></th></tr>'
    + (rows.length ? rows.map((r) => '<tr><td class="name">' + (r.k === null ? '<span class="dim">모름</span>' : esc(label(r.k))) + '</td>'
        + cols.map(([c]) => '<td>' + n(r[c]) + '</td>').join('') + '<td class="bar"><i style="width:' + (100 * r.n / top).toFixed(1) + '%"></i></td></tr>').join('')
      : '<tr><td colspan="' + (cols.length + 2) + '" class="dim name">아직 자료가 없습니다.</td></tr>')
    + '</table></div>';
}
// 플레이 통계 — 타일 · 장별 표 · 날짜 · 버전 · 기기 표
function renderFd2Plays(p) {
  const t = p.total || {};
  const tile = (v, label) => '<div class="tile"><b' + (typeof v === 'string' && v.length > 8 ? ' style="font-size:15px"' : '') + '>' + (typeof v === 'string' ? esc(v) : n(v)) + '</b><span>' + label + '</span></div>';
  const decided = (t.wins || 0) + (t.loses || 0);
  const hours = (sec) => (sec ? (Math.round(sec / 360) / 10).toLocaleString('ko-KR') + '시간' : '-');
  const chap = (k) => (k === 0 ? '프롤로그' : k + '장');
  $('fd2ptiles').innerHTML = tile(t.installs, '설치') + tile(t.plays, '판') + tile(decided ? Math.round(100 * t.wins / decided) + '%' : '-', '승률(그만둔 판 빼고)')
    + tile(t.quits, '그만둔 판') + tile(hours(t.secSum), '논 시간 합') + tile(t.maxChapter != null ? chap(t.maxChapter) : '-', '가장 멀리 이긴 장')
    + tile(t.last ? new Date(t.last).toLocaleString('ko-KR') : '-', '마지막으로 받은 때');
  const rows = p.chapters || [];
  const top = Math.max(1, ...rows.map((r) => r.installs));
  const f1 = (v) => (v == null ? '-' : (Math.round(v * 10) / 10).toLocaleString('ko-KR'));
  $('fd2chapters').innerHTML = '<tr><th class="name">장</th><th>이김</th><th>짐</th><th>그만둠</th><th>승률</th><th>설치</th><th>폰 판</th><th>불러온 판</th><th>평균 턴</th><th>평균 시간</th><th></th></tr>'
    + (rows.length ? rows.map((r) => {
        const all = r.wins + r.loses;
        return '<tr><td class="name">' + chap(r.k) + '</td><td>' + n(r.wins) + '</td><td>' + n(r.loses) + '</td><td>' + n(r.quits) + '</td><td>' + (all ? Math.round(100 * r.wins / all) + '%' : '-')
          + '</td><td>' + n(r.installs) + '</td><td>' + n(r.phone) + '</td><td>' + n(r.loads) + '</td><td>' + f1(r.turns) + '</td><td>' + (r.seconds == null ? '-' : clock(r.seconds, 1))
          + '</td><td class="bar"><i style="width:' + (100 * r.installs / top).toFixed(1) + '%"></i></td></tr>';
      }).join('')
      : '<tr><td colspan="11" class="dim name">아직 자료가 없습니다.</td></tr>');
  const cols = [['installs', '설치'], ['plays', '판'], ['minutes', '분']];
  const prep = (rows) => rows.map((r) => ({ ...r, n: r.installs, minutes: Math.round((r.seconds || 0) / 60) }));   // 막대는 설치 수로
  $('fd2pgrid').innerHTML = fd2Table('날짜별', prep(p.days || []), String, cols) + fd2Table('버전별', prep(p.versions || []), (k) => 'v' + k, cols)
    + fd2Table('기기', prep(p.platforms || []), (k) => String(k).replace('windows', 'Windows').replace('android', '안드로이드'), cols);
}
async function loadFd2() {
  fd2Loaded = true;
  const key = bugKey();
  $('fd2err').textContent = '';
  if (!key) { $('fd2tiles').innerHTML = $('fd2grid').innerHTML = $('fd2ptiles').innerHTML = $('fd2chapters').innerHTML = $('fd2pgrid').innerHTML = ''; $('fd2bugs').innerHTML = '<span class="dim">관리자 키를 넣으면 보입니다.</span>'; return; }
  const get = async (path) => {
    const res = await fetch(path, { headers: { Authorization: 'Bearer ' + key } });
    if (res.status === 401) throw new Error('키가 맞지 않습니다');
    if (!res.ok) throw new Error('HTTP ' + res.status);
    return res.json();
  };
  try {
    const [s, list] = await Promise.all([get('/v1/fd2/summary'), get('/v1/bugs?game=fd2&status=' + fd2Status.status + '&order=desc&limit=200')]);
    const t = s.total || {};
    $('fd2tiles').innerHTML = [[t.n, '리포트'], [t.fresh, '새로 온 것'], [t.fixed, '고침'], [t.installs, '보낸 설치'], [t.last ? new Date(t.last).toLocaleString('ko-KR') : '-', '마지막으로 받은 때']]
      .map(([v, label]) => '<div class="tile"><b' + (typeof v === 'number' || v == null ? '' : ' style="font-size:15px"') + '>' + (typeof v === 'string' ? esc(v) : n(v)) + '</b><span>' + label + '</span></div>').join('');
    $('fd2grid').innerHTML = FD2_TABLES.map(([k, title, label]) => fd2Table(title, s[k] || [], label, [['n', '리포트'], ['fresh', '새로'], ['installs', '설치']])).join('')
      + fd2Table('날짜별', s.days || [], String, [['n', '리포트'], ['installs', '설치']]);
    renderFd2Plays(s.plays || {});
    const bugs = (list.bugs || []).map((b) => ({ ...b, game: 'fd2' }));
    $('fd2bugs').innerHTML = bugs.length ? bugs.map(bugCard).join('') : '<span class="dim">없습니다.</span>';
    $('fd2bugs').querySelectorAll('button[data-id]').forEach((b) => b.onclick = () => markBug('fd2', Number(b.dataset.id), b.dataset.s));
  } catch (e) { $('fd2err').textContent = '불러오지 못했습니다: ' + e.message; }
}
// ── 마그나카르타 — 버그 리포트만(/v1/bugs?game=mc). 같은 관리자 키 ──
const mcStatus = { status: 'new' };
let mcLoaded = false;
async function loadMc() {
  mcLoaded = true;
  const key = bugKey();
  $('mcerr').textContent = '';
  if (!key) { $('mctiles').innerHTML = ''; $('mcbugs').innerHTML = '<span class="dim">관리자 키를 넣으면 보입니다.</span>'; return; }
  try {
    const get = async (status) => {
      const res = await fetch('/v1/bugs?game=mc&status=' + status + '&order=desc&limit=200', { headers: { Authorization: 'Bearer ' + key } });
      if (res.status === 401) throw new Error('키가 맞지 않습니다');
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return ((await res.json()).bugs || []).map((b) => ({ ...b, game: 'mc' }));
    };
    const [bugs, all] = await Promise.all([get(mcStatus.status), mcStatus.status === 'all' ? null : get('all')]);
    const every = all || bugs;
    const installs = new Set(every.map((b) => b.install)).size;
    $('mctiles').innerHTML = [[every.length, '리포트'], [every.filter((b) => b.status === 'new').length, '새로 온 것'], [every.filter((b) => b.status === 'fixed').length, '고침'], [installs, '보낸 설치']]
      .map(([v, label]) => '<div class="tile"><b>' + n(v) + '</b><span>' + label + '</span></div>').join('');
    $('mcbugs').innerHTML = bugs.length ? bugs.map(bugCard).join('') : '<span class="dim">없습니다.</span>';
    $('mcbugs').querySelectorAll('button[data-id]').forEach((b) => b.onclick = () => markBug('mc', Number(b.dataset.id), b.dataset.s));
  } catch (e) { $('mcerr').textContent = '불러오지 못했습니다: ' + e.message; }
}
$('mckey').onclick = askBugKey;
$('mcreload').onclick = loadMc;
$('mcstatus').onchange = (e) => { mcStatus.status = e.target.value; loadMc(); };
$('fd2key').onclick = askBugKey;
$('fd2reload').onclick = loadFd2;
$('fd2status').onchange = (e) => { fd2Status.status = e.target.value; loadFd2(); };
$('bugreload').onclick = loadBugs;
$('bugstatus').onchange = (e) => { bugState.status = e.target.value; loadBugs(); };
loadBugs();

// 기간 — 오늘(한국 날짜) · 최근 N일 · 전체 · 직접 지정(시작 · 끝 날짜, 끝은 그날까지).
const kstToday = () => new Date(Date.now() + 9 * 3600000).toISOString().slice(0, 10);
function applyPeriod() {
  const v = $('period').value;
  $('customRange').hidden = v !== 'custom';
  if (v === 'today') { state.from = state.to = kstToday(); }
  else if (v === 'custom') { state.from = $('from').value; state.to = $('to').value; if (!state.from && !state.to) return; }
  else { state.from = state.to = ''; state.days = v; }
  load();
}
$('period').onchange = applyPeriod;
$('from').onchange = applyPeriod;
$('to').onchange = applyPeriod;
$('dayUnit').onchange = (e) => { dayUnit = e.target.value; if (data) renderDays(); };
$('version').onchange = (e) => { state.version = e.target.value; load(); };
$('tachyrion').onchange = (e) => { state.tachyrion = e.target.value; load(); };
load();
</script>
</body>
</html>`;
