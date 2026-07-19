-- ============================================================================
-- Warnungs-Kategorien (Walter 19.07.2026, final):
--   night_work_untersuch_fehlt = Nacht Untersuch fehlt (eigene Zeile)
--   Bewilligung/Nacht «läuft ab» decken auch Abgelaufen ab (Titel + red_overdue)
--   permit_expired entfällt wieder (konsolidiert in permit_expiring)
--
-- Ausführung: direkt in TablePlus.
-- ============================================================================

ALTER TABLE dashboard_warning_config
    ADD COLUMN IF NOT EXISTS todo_priority INT  NOT NULL DEFAULT 100,
    ADD COLUMN IF NOT EXISTS warn_color    TEXT NOT NULL DEFAULT 'none';

DELETE FROM dashboard_warning_config WHERE category = 'permit_expired';

INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated,
     is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('night_work_untersuch_fehlt', 'Nacht Untersuch fehlt', TRUE, NULL, NULL, 'critical', NULL,
     FALSE, 18, 30, 'red')
ON CONFLICT (category) DO NOTHING;

-- Empfohlene Warnfarbe für Ablauf-Kategorien (nur wenn noch Default)
UPDATE dashboard_warning_config SET warn_color = 'red_overdue'
 WHERE category IN ('permit_expiring', 'night_work_exam_expiring')
   AND warn_color = 'none';
