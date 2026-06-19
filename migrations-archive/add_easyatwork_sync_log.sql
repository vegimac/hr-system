-- ════════════════════════════════════════════════════════════════════════
-- easy@work Auto-Sync: Protokoll-Tabelle (Walter-Vorgabe 19.06.2026)
--
-- Pro Filiale + Lauf eine Zeile, damit der Admin den automatischen Sync in der
-- App nachvollziehen kann (statt auf dem Server ins journalctl zu schauen).
-- status: OK / BLOCKED / ERROR / SKIPPED. Alte Einträge werden vom Job nach
-- 90 Tagen automatisch entfernt.
--
-- In TablePlus ausführen (kein psql-Wrapper).
-- ════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS easyatwork_sync_log (
    id                 SERIAL PRIMARY KEY,
    company_profile_id INTEGER NOT NULL,
    run_at             TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT now(),
    status             TEXT NOT NULL,             -- OK / BLOCKED / ERROR / SKIPPED
    period_from        DATE,
    period_to          DATE,
    used_updates_feed  BOOLEAN NOT NULL DEFAULT false,
    inserted           INTEGER NOT NULL DEFAULT 0,
    updated            INTEGER NOT NULL DEFAULT 0,
    deleted            INTEGER NOT NULL DEFAULT 0,
    locked_skipped     INTEGER NOT NULL DEFAULT 0,
    skipped            INTEGER NOT NULL DEFAULT 0,
    missing_count      INTEGER NOT NULL DEFAULT 0,
    message            TEXT
);

CREATE INDEX IF NOT EXISTS ix_easyatwork_sync_log_runat
    ON easyatwork_sync_log(run_at DESC);
CREATE INDEX IF NOT EXISTS ix_easyatwork_sync_log_branch
    ON easyatwork_sync_log(company_profile_id, run_at DESC);
