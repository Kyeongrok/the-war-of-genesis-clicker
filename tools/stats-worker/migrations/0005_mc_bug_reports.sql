-- 마그나카르타 패치판(Kyeongrok/magnacarta-remake)의 버그 리포트 표 — 창세기전3 표와 같은 꼴, 번호는 따로 센다.
-- 게임이 POST /v1/bug?game=mc 로 보낸다(src/MagnaCarta/Scenes/BugReport.cs). 다시 돌려도 괜찮다(IF NOT EXISTS).
--   npx wrangler d1 execute war-of-genesis-stats --remote --file=migrations/0005_mc_bug_reports.sql
CREATE TABLE IF NOT EXISTS mc_bug_reports (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,
  version    TEXT NOT NULL,
  install    TEXT NOT NULL,
  text       TEXT NOT NULL,
  context    TEXT NOT NULL,
  status     TEXT NOT NULL DEFAULT 'new',
  note       TEXT,
  reply      TEXT
);

CREATE INDEX IF NOT EXISTS mc_bug_reports_install ON mc_bug_reports (install, created_at);
CREATE INDEX IF NOT EXISTS mc_bug_reports_status ON mc_bug_reports (status, id);
