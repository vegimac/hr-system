-- ============================================================================
-- Warnungsverwaltung (Walter-Vorgabe 06.07.2026)
-- Globale Konfiguration der Dashboard-/ToDo-Warnungen:
--   pro Kategorie: an/aus, Vorlauf (Tage), Eskalations-Schwelle (Tage),
--   Schweregrad (Basis + eskaliert).
--
-- Ausführung: direkt in TablePlus (reiner SQL-Block, kein psql-Wrapper).
-- Idempotent: CREATE TABLE IF NOT EXISTS + UNIQUE(category)
--             + INSERT ... ON CONFLICT (category) DO NOTHING.
-- Der Seed bildet das heutige DashboardService-Verhalten 1:1 ab.
-- ============================================================================

CREATE TABLE IF NOT EXISTS dashboard_warning_config (
    id                 SERIAL PRIMARY KEY,
    category           TEXT    NOT NULL UNIQUE,
    label              TEXT    NOT NULL,
    enabled            BOOLEAN NOT NULL DEFAULT TRUE,
    warn_days          INT,
    escalate_days      INT,
    severity_base      TEXT    NOT NULL DEFAULT 'warning',
    severity_escalated TEXT,
    is_date_based      BOOLEAN NOT NULL DEFAULT FALSE,
    sort_order         INT     NOT NULL DEFAULT 0
);

INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order)
VALUES
    ('minimum_wage_violation', 'Mindestlohn unterschritten',            TRUE, NULL, NULL, 'critical', NULL,       FALSE,  1),
    ('permit_expiring',        'Bewilligung läuft ab',                  TRUE,   60,   30, 'warning',  'critical', TRUE,   2),
    ('probation_end',          'Probezeit endet',                       TRUE,   14,    7, 'info',     'warning',  TRUE,   3),
    ('contract_end',           'Befristeter Vertrag endet',             TRUE,   30,   14, 'info',     'warning',  TRUE,   4),
    ('exit_pending_active',    'Austritt erfasst, MA noch aktiv',       TRUE, NULL,   30, 'warning',  'critical', FALSE,  5),
    ('qst_pflicht_offen',      'QST-Pflicht offen (Lohnlauf gesperrt)', TRUE, NULL, NULL, 'critical', NULL,       FALSE,  6),
    ('spouse_doku_fehlt',      'Ausweis Ehepartner fehlt (QST)',        TRUE, NULL, NULL, 'critical', NULL,       FALSE,  7),
    ('employee_doku_fehlt',    'Ausweis Mitarbeiter fehlt (QST)',       TRUE, NULL, NULL, 'critical', NULL,       FALSE,  8),
    ('schwangerschaft',        'Mutterschaft / Schwangerschaft',        TRUE,   30, NULL, 'info',     'warning',  TRUE,   9),
    ('lohn_provisorisch',      'Lohn wartet auf Definitiv-Abschluss',   TRUE, NULL, NULL, 'warning',  NULL,       FALSE, 10),
    ('birthday',               'Geburtstage',                           TRUE,    7, NULL, 'info',     NULL,       TRUE,  11),
    ('anniversary',            'Dienstjubiläen',                        TRUE,   30, NULL, 'info',     NULL,       TRUE,  12),
    ('night_work_exam_expiring','Nachtarbeit-Bewilligung läuft ab',     TRUE,   30,    7, 'warning',  'critical', TRUE,  13),
    ('night_work_exam_fehlt',  'Nachtarbeit-Nachweise fehlen',          TRUE, NULL, NULL, 'critical', NULL,       FALSE, 14),
    ('night_work_exam_mismatch','Nachtarbeit-Enddatum in easy@work falsch', TRUE, NULL, NULL, 'critical', NULL,   FALSE, 15)
ON CONFLICT (category) DO NOTHING;
