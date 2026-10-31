-- 이미 만든 DB 에 seconds 칸을 더할 때: ALTER TABLE battles ADD COLUMN seconds INTEGER;
-- 전투 통계 D1 표 — wrangler d1 execute war-of-genesis-stats --remote --file=schema.sql
CREATE TABLE IF NOT EXISTS battles (
  report      TEXT PRIMARY KEY,   -- 게임이 판마다 만든 번호(다시 보내도 한 번만 들어가게)
  received_at TEXT NOT NULL,
  install     TEXT NOT NULL,      -- 설치할 때 만든 무작위 번호
  version     TEXT NOT NULL,
  battle      INTEGER NOT NULL,   -- Btl 번호
  difficulty  INTEGER NOT NULL,
  outcome     TEXT NOT NULL,      -- win · lose · quit
  turns       INTEGER NOT NULL,
  seconds     INTEGER,            -- 걸린 시간(초, 실제 시계) — 이 칸이 생기기 전 판은 NULL
  flags       TEXT NOT NULL       -- 수치를 흔드는 것들(JSON: 캐릭터 에디터를 썼나, 소울 기여도 …)
);

-- 대시보드는 기간(최근 1주 · 4주)만 읽는다 — 받은 때 색인으로 그 앞 줄만 훑는다.
CREATE INDEX IF NOT EXISTS battles_received ON battles (received_at);

CREATE TABLE IF NOT EXISTS skill_stats (
  report  TEXT NOT NULL,
  chr     INTEGER NOT NULL,       -- 쓴 인물(Chr 번호)
  ability INTEGER NOT NULL,       -- 어빌리티 번호(일반 공격은 0)
  work    INTEGER NOT NULL,       -- work 번호
  level   INTEGER NOT NULL,
  uses    INTEGER NOT NULL,
  hits    INTEGER NOT NULL,
  damage  INTEGER NOT NULL,
  kills   INTEGER NOT NULL,
  heal    INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS skill_stats_report ON skill_stats (report);
CREATE INDEX IF NOT EXISTS skill_stats_ability ON skill_stats (ability, work);

-- 집계 결과를 잠깐 들고 있는 표(src/index.js 의 cached) — 없으면 worker 가 처음 쓸 때 만든다.
CREATE TABLE IF NOT EXISTS cache (
  key  TEXT PRIMARY KEY,
  at   INTEGER NOT NULL,          -- 만든 때(ms)
  body TEXT NOT NULL              -- 집계 JSON
);

-- 버그 리포트(게임의 「버그 리포트」 창) — 이미 있는 DB 에는 migrations/0002_bug_reports.sql 을 돌린다.
CREATE TABLE IF NOT EXISTS bug_reports (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,              -- 받은 때(ISO, UTC)
  version    TEXT NOT NULL,              -- 게임 판 번호
  install    TEXT NOT NULL,              -- 설치할 때 만든 무작위 번호(전투 통계와 같은 것)
  text       TEXT NOT NULL,              -- 사용자가 쓴 글(8000자까지)
  context    TEXT NOT NULL,              -- 게임이 붙인 것(JSON: 판 · OS · 화면 · 전투 · 난이도 · 모드 · 오류 기록 끝)
  status     TEXT NOT NULL DEFAULT 'new',-- new · seen · fixed · wontfix · dup …
  note       TEXT                        -- 고친 사람의 메모
);

CREATE INDEX IF NOT EXISTS bug_reports_install ON bug_reports (install, created_at);
CREATE INDEX IF NOT EXISTS bug_reports_status ON bug_reports (status, id);
