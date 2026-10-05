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
