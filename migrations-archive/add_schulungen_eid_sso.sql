-- eID/SSO für alle MA + Manager-Schulungen Nothelfer/Peak-Verifizierung/Seco
-- (Walter-Vorgabe 14.08.2026). Gültigkeitsdauer je Schulung in app_setting
-- (Schulung.NothelferMonate / Schulung.PeakMonate / Schulung.SecoMonate).
-- Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS eid text,
    ADD COLUMN IF NOT EXISTS sso text,
    ADD COLUMN IF NOT EXISTS schulung_nothelfer_am date,
    ADD COLUMN IF NOT EXISTS schulung_peak_am date,
    ADD COLUMN IF NOT EXISTS schulung_seco_am date;

INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('schulung_nothelfer', 'Schulung Nothelfer läuft ab',          TRUE, 60, 14, 'warning', 'critical', TRUE, 24, 60, 'red_overdue'),
    ('schulung_peak',      'Schulung Peak-Verifizierung läuft ab', TRUE, 60, 14, 'warning', 'critical', TRUE, 25, 61, 'red_overdue'),
    ('schulung_seco',      'Schulung Seco läuft ab',               TRUE, 60, 14, 'warning', 'critical', TRUE, 26, 62, 'red_overdue')
ON CONFLICT (category) DO NOTHING;
