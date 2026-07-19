-- ============================================================================
-- Neue Warnungs-Kategorien (Walter 19.07.2026):
--   permit_expired              = Bewilligung ist abgelaufen
--   night_work_untersuch_fehlt  = Nacht Untersuch fehlt
-- (getrennt von «läuft ab» bzw. «Nachweise fehlen»)
--
-- Ausführung: direkt in TablePlus.
-- ============================================================================

ALTER TABLE dashboard_warning_config
    ADD COLUMN IF NOT EXISTS todo_priority INT  NOT NULL DEFAULT 100,
    ADD COLUMN IF NOT EXISTS warn_color    TEXT NOT NULL DEFAULT 'none';

INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated,
     is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('permit_expired', 'Bewilligung ist abgelaufen', TRUE, NULL, NULL, 'critical', NULL,
     FALSE, 18, 20, 'red'),
    ('night_work_untersuch_fehlt', 'Nacht Untersuch fehlt', TRUE, NULL, NULL, 'critical', NULL,
     FALSE, 19, 30, 'red')
ON CONFLICT (category) DO NOTHING;
