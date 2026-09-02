-- ═══════════════════════════════════════════════════════════════════════
-- Verteiler-Freigabe Mail/SMS + Mail-Protokoll (Walter-Vorgabe 01.09.2026)
--
-- Ersetzt den Alles-oder-nichts-Schalter «test_redirect_to gefüllt».
-- Ab jetzt entscheidet pro Kategorie und Kanal ein Haken:
--   mail_scharf/sms_scharf = true  → geht an den echten Empfänger
--   false                          → geht an die Test-Adresse/-Nummer
--
-- Der Seed unten bildet exakt den bisherigen Zustand ab: nur interne
-- Benutzer-Mails liefen scharf. Nach dem Deploy ändert sich also nichts
-- von selbst — bis Walter die Haken setzt.
-- ═══════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS versand_kategorie (
    code                VARCHAR(40)  PRIMARY KEY,
    mail_scharf         BOOLEAN      NOT NULL DEFAULT FALSE,
    sms_scharf          BOOLEAN      NOT NULL DEFAULT FALSE,
    updated_at          TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_by_user_id  INTEGER      NULL
);

-- Seed = bisheriger Stand. ON CONFLICT DO NOTHING, damit ein erneuter
-- Lauf gesetzte Haken NICHT zurücksetzt.
INSERT INTO versand_kategorie (code, mail_scharf, sms_scharf) VALUES
    ('INTERN',       TRUE,  FALSE),
    ('POSTFACH',     FALSE, FALSE),
    ('LOHN',         FALSE, FALSE),
    ('VERTRAG',      FALSE, FALSE),
    ('MOMENT',       FALSE, FALSE),
    ('BEWILLIGUNG',  FALSE, FALSE),
    ('GRUPPEN_MAIL', FALSE, FALSE),
    ('KANDIDAT',     FALSE, FALSE),
    ('DRITTE',       FALSE, FALSE)
ON CONFLICT (code) DO NOTHING;

-- ── Mail-Protokoll, Gegenstück zu sms_log ──────────────────────────────
CREATE TABLE IF NOT EXISTS mail_log (
    id                SERIAL       PRIMARY KEY,
    created_at        TIMESTAMP    NOT NULL DEFAULT NOW(),
    kategorie         VARCHAR(40)  NULL,
    employee_id       INTEGER      NULL,
    to_email          VARCHAR(300) NULL,
    redirected_to     VARCHAR(300) NULL,
    subject           VARCHAR(500) NULL,
    attachment_count  INTEGER      NOT NULL DEFAULT 0,
    ok                BOOLEAN      NOT NULL DEFAULT FALSE,
    error             TEXT         NULL
);

CREATE INDEX IF NOT EXISTS ix_mail_log_employee_kategorie ON mail_log (employee_id, kategorie);
CREATE INDEX IF NOT EXISTS ix_mail_log_created_at        ON mail_log (created_at);

-- ═══════════════════════════════════════════════════════════════════════
-- Fehlerprotokoll MA-Stammdaten-Sync (Walter-Vorgabe 01.09.2026)
--
-- Der Nachtlauf blockiert ab jetzt Verträge mit Erfassungsfehlern in
-- easy@work (FIX mit Stunden statt Prozent, Pensum ausserhalb
-- 50/60/70/80/90/100, FLEX ≠ 17 Std/Woche, MTP ausserhalb 18–40).
-- Ohne dieses Protokoll würden solche Verträge lautlos fehlen.
-- ═══════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS easyatwork_ma_sync_log (
    id                    SERIAL      PRIMARY KEY,
    run_at                TIMESTAMP   NOT NULL DEFAULT NOW(),
    company_profile_id    INTEGER     NOT NULL,
    employee_number       VARCHAR(50) NULL,
    employee_id           INTEGER     NULL,
    kind                  VARCHAR(20) NOT NULL DEFAULT 'VERTRAG',
    reason                TEXT        NOT NULL,
    erledigt              BOOLEAN     NOT NULL DEFAULT FALSE,
    erledigt_am           TIMESTAMP   NULL,
    erledigt_von_user_id  INTEGER     NULL
);

CREATE INDEX IF NOT EXISTS ix_ma_sync_log_cp_runat ON easyatwork_ma_sync_log (company_profile_id, run_at);
CREATE INDEX IF NOT EXISTS ix_ma_sync_log_erledigt ON easyatwork_ma_sync_log (erledigt);

-- ═══════════════════════════════════════════════════════════════════════
-- Austritts-Abgleich easy@work ↔ OneCrew (Walter-Vorgabe 01.09.2026)
--
-- Program.cs seedet diese Zeilen beim Start ebenfalls (idempotent). Hier
-- nur, damit die Warnungsverwaltung sie auch ohne Neustart kennt.
-- ═══════════════════════════════════════════════════════════════════════

INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('austritt_unvollstaendig',       'Austritt ohne Kündigungsangaben',     TRUE, NULL, NULL, 'warning',  NULL, FALSE, 25, 57, 'none'),
    ('austritt_datum_mismatch',       'Austrittsdatum stimmt nicht überein', TRUE, NULL, NULL, 'warning',  NULL, FALSE, 26, 58, 'none'),
    ('contract_end_weitergearbeitet', 'Nach Vertragsende weitergearbeitet',  TRUE, NULL, NULL, 'critical', NULL, FALSE, 27, 22, 'red')
ON CONFLICT (category) DO NOTHING;

-- «Vertragsende wegen Kündigung» neu sofort statt erst 14 Tage vorher.
-- Guard auf dem alten Seed-Wert: eine eigene Einstellung bleibt unangetastet.
UPDATE dashboard_warning_config SET warn_days = 3650
    WHERE category = 'kuendigung_ablauf' AND warn_days = 14;
