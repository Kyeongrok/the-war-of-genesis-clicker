# 전투 통계 받는 곳 (Cloudflare Worker + D1)

게임이 전투가 끝날 때 보내는 요약(어느 어빌리티를 몇 번 썼고 피해를 얼마 넣었나)을 받아 D1 에 쌓는다.
보내는 쪽은 `duel-dx/System/BattleStats.cs`.

## 처음 한 번 올리기

```
cd tools/stats-worker
npx wrangler login
npx wrangler d1 create dueldx-stats          # 찍히는 database_id 를 wrangler.toml 에 넣는다
npx wrangler d1 execute dueldx-stats --remote --file=schema.sql
npx wrangler deploy                          # https://dueldx-stats.<계정>.workers.dev 주소가 찍힌다
```

찍힌 주소를 `duel-dx/System/BattleStats.cs` 의 `UploadUrl` 에 넣어야 게임이 보내기 시작한다(비어 있으면 안 보낸다).

## 보기

- 집계: `https://…workers.dev/v1/top` — 어빌리티별 쓴 횟수 · 피해 · 처치 합계
- 마음대로 묻기:

```
npx wrangler d1 execute dueldx-stats --remote --command "SELECT ability, SUM(uses) u, SUM(damage) d FROM skill_stats GROUP BY ability ORDER BY d DESC LIMIT 20"
```

캐릭터 에디터를 쓴 판은 `battles.flags` 에 표시가 있다 — 빼고 보려면 `battles` 와 묶어 `flags NOT LIKE '%"charEdit":true%'`.

## 적히는 것

`battles`(판 하나에 한 줄: 전투 번호 · 판 번호 · 난이도 · 승패 · 턴 수 · 설치 번호)와 `skill_stats`(인물 × 어빌리티마다 한 줄).
이름 · 계정 · IP · 세이브 내용은 적지 않는다.
