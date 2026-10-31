-- 대시보드가 기간(최근 1주 · 4주)만 읽도록 받은 때 색인을 단다 — 없으면 기간으로 걸러도 표 전체를 훑어 읽기 한도를 먹는다.
--   npx wrangler d1 execute war-of-genesis-stats --remote --command "CREATE INDEX IF NOT EXISTS battles_received ON battles (received_at)"
CREATE INDEX IF NOT EXISTS battles_received ON battles (received_at);
