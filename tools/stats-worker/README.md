# 전투 통계 받는 곳 (Cloudflare Worker + D1)

게임이 전투가 끝날 때 보내는 요약(어느 어빌리티를 몇 번 썼고 피해를 얼마 넣었나)을 받아 D1 에 쌓는다.
보내는 쪽은 `duel-dx/System/BattleStats.cs`.

## 처음 한 번 올리기

```
cd tools/stats-worker
npx wrangler login
npx wrangler d1 create war-of-genesis-stats          # 찍히는 database_id 를 wrangler.toml 에 넣는다
npx wrangler d1 execute war-of-genesis-stats --remote --file=schema.sql
npx wrangler deploy                          # https://war-of-genesis-stats.<계정>.workers.dev 주소가 찍힌다
```

찍힌 주소를 저장소 뿌리의 `stats-url.txt` 에 한 줄로 적어야 게임이 보내기 시작한다(비어 있으면 안 보낸다).

## 보기

- 대시보드: `https://…workers.dev/dashboard` — 어빌리티 순위(사용 · 피해 · 1회당 피해 · 처치로 줄 세우기) · 인물별 · 전투별 승률 · 날짜/버전별(전투는 챕터로 묶음). 캐릭터 에디터를 쓴 판도 넣는다. 지금은 누구나 볼 수 있다.
  이름 표(`src/names.js`)는 `python tools/stats-worker/make_names.py` 로 다시 뽑는다.

- 집계: `https://…workers.dev/v1/top` — 어빌리티별 쓴 횟수 · 피해 · 처치 합계
- 마음대로 묻기:

```
npx wrangler d1 execute war-of-genesis-stats --remote --command "SELECT ability, SUM(uses) u, SUM(damage) d FROM skill_stats GROUP BY ability ORDER BY d DESC LIMIT 20"
```

캐릭터 에디터를 쓴 판은 `battles.flags` 에 표시가 있다 — 빼고 보려면 `battles` 와 묶어 `flags NOT LIKE '%"charEdit":true%'`.

`flags` 에는 화면 설정과 기기 갈래도 실린다(이 칸을 넣은 판부터): `res`(설정의 해상도 「가로x세로」) · `zoom`(배율 %, 0 = 자동) · `battleZoom` ·
`fullscreen` · `hiResUi`(새 UI) · `font`(글자 크기 %) · `platform`(`desktop` · `laptop` · `umpc` · `deck` · `wine` — 기종 이름은 안 보낸다) ·
`touch`(터치 화면) · `screen`(모니터 크기). 안드로이드(Winlator 따위)는 `wine` 으로 잡히니 `touch` 와 `screen` 으로 짐작한다.

```
npx wrangler d1 execute war-of-genesis-stats --remote --command "SELECT json_extract(flags,'$.platform') platform, COUNT(DISTINCT install) installs, COUNT(*) battles, SUM(json_extract(flags,'$.fullscreen')) fullscreen, SUM(json_extract(flags,'$.hiResUi')) new_ui FROM battles WHERE json_extract(flags,'$.platform') IS NOT NULL GROUP BY platform"
npx wrangler d1 execute war-of-genesis-stats --remote --command "SELECT json_extract(flags,'$.res') res, COUNT(DISTINCT install) installs, COUNT(*) battles FROM battles WHERE json_extract(flags,'$.res') IS NOT NULL GROUP BY res ORDER BY battles DESC"
```

`flags` 에는 조작 방식과 타이밍 공격도 실린다(v0.31.1 부터 — 그 전 판은 이 칸이 없다):
`pad` · `keys` = 그 판에서 내 인물이 기술을 쓴 횟수(패드로 / 키보드 · 마우스로), `timedHit` = 타이밍 공격을 켰나,
`timed` = [띠가 뜬 횟수, 박자 둘 다 맞춤, 하나만 맞춤].

```
# 패드로 한 판 · 키보드로 한 판 · 타이밍 공격을 켠 판 (버전별)
npx wrangler d1 execute war-of-genesis-stats --remote --command "SELECT version, COUNT(*) battles, SUM(json_extract(flags,'$.pad') > json_extract(flags,'$.keys')) pad_battles, SUM(json_extract(flags,'$.keys') >= json_extract(flags,'$.pad')) key_battles, SUM(json_extract(flags,'$.timedHit')) timed_on, SUM(json_extract(flags,'$.timed[0]')) bars, SUM(json_extract(flags,'$.timed[1]')) both, SUM(json_extract(flags,'$.timed[2]')) one FROM battles WHERE json_extract(flags,'$.pad') IS NOT NULL GROUP BY version"
# 패드를 한 번이라도 쓴 설치 수
npx wrangler d1 execute war-of-genesis-stats --remote --command "SELECT COUNT(DISTINCT install) FROM battles WHERE json_extract(flags,'$.pad') > 0"
```

`tachyrion` = 모드 > 타키리온 개편을 켠 판인가(그 칸이 없는 옛 판은 끈 판으로 센다). 대시보드의 「타키리온 개편」 고르기가 이것으로 거른다(`/v1/dashboard?tachyrion=1` · `0`).

```
# 켠 판 · 끈 판의 어빌리티 쓰임
npx wrangler d1 execute war-of-genesis-stats --remote --command "SELECT COALESCE(json_extract(b.flags,'$.tachyrion'),0) rework, s.ability, SUM(s.uses) uses FROM skill_stats s JOIN battles b ON b.report = s.report GROUP BY rework, s.ability ORDER BY rework, uses DESC"
```

## 적히는 것

`battles`(판 하나에 한 줄: 전투 번호 · 판 번호 · 난이도 · 승패 · 턴 수 · 설치 번호)와 `skill_stats`(인물 × 어빌리티마다 한 줄).
이름 · 계정 · IP · 세이브 내용은 적지 않는다.

## 버그 리포트

게임의 「버그 리포트」(파트2 타이틀 오른쪽 아래 · 파트1 타이틀 PART 2 위 · 시스템 메뉴)가 글 쓰는 창을 띄우고, 쓴 글을 여기로 보낸다
(`duel-dx/System/BugReport.cs`). 「익명 전투 통계 보내기」를 꺼 두어도 보낸다(사용자가 보내기를 눌렀으니). 못 보내면 `%APPDATA%\DuelDx\bug-queue` 에 두었다가 다음에 켤 때 다시 보낸다.

- `POST /v1/bug` — `{ v: 1, install, version, text, context }`. 글 8000자 · context 32KB · 몸 48KB 까지. 설치 하나가 한 시간에 10 건(모두 합쳐 300 건)을 넘으면 429. `{ ok, id }` 를 돌려준다.
- `GET /v1/bugs?since=<id>&status=new&limit=100` — 관리자만(`Authorization: Bearer <BUG_ADMIN_KEY>`, 아니면 401). `status=all` 이면 모두.
- `POST /v1/bugs/<id>/status` `{ status, note }` — 관리자만. status 는 소문자 낱말(new · seen · fixed · wontfix · dup …), note 는 2000자까지(안 주면 그대로).

`context` 에 게임이 붙이는 것: 판 번호 · OS · 기기 갈래 · 화면 크기 · 지금 자리(장면 꼬리표 「Btl 0045 · 턴 3」) · 전투 · 필드 · 챕터 · 난이도 · 켠 모드 ·
읽기 오류 · `%TEMP%\dueldx_crash.log` 의 끝(200줄 · 20KB). 경로의 사용자 폴더 · 사용자 이름은 `%USERPROFILE%` · `<user>` 로 바꿔 보낸다.

### 올리기(처음 한 번)

```
cd tools/stats-worker
npx wrangler d1 execute war-of-genesis-stats --remote --file=migrations/0002_bug_reports.sql   # bug_reports 표
npx wrangler secret put BUG_ADMIN_KEY                                                         # 관리자 열쇠(16자 넘게) — 물으면 붙여 넣는다
npx wrangler deploy
```

열쇠는 아무 긴 무작위 글이면 된다 — PowerShell: `-join ((48..57)+(97..122) | Get-Random -Count 40 | % {[char]$_})`.
같은 값을 `~\.dueldx-bug-admin-key` 에 한 줄로 적어 두면 `bugs.ps1` 이 읽는다.

### 보기 · 고치기 — `bugs.ps1`

```
./bugs.ps1                                        # 새(new) 리포트: 번호 · 때 · 판 · 자리 · 환경 · 모드 · 오류 기록 첫 줄 · 글
./bugs.ps1 -Id 42                                 # 하나를 자세히(오류 기록 전부)
./bugs.ps1 -Status all -Since 40                  # 40 번 뒤 모두   (-Json 이면 JSON 그대로)
./bugs.ps1 -Mark 42 -Status fixed -Note "v1.0.43 에서 고침"
```

로컬 시험: `npx wrangler d1 execute war-of-genesis-stats --local --file=schema.sql` 뒤
`npx wrangler dev --local --port 8833 --var BUG_ADMIN_KEY:<열쇠>`, 게임은 `DUELDX_STATS_URL=http://127.0.0.1:8833` 으로 그쪽에 보낸다
(화면 밖 시험은 `DUELDX_OFFSCREEN=1 DUELDX_BUGREPORT_TEXT=<글>` — 창 없이 켜자마자 그 글을 보낸다). `./bugs.ps1 -Url http://127.0.0.1:8833 -KeyFile <열쇠 파일>`.
