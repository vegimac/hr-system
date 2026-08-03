-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 31.07.2026: Nachtarbeit-Todos trennen
--   • night_work_exam_fehlt     → nur Arztzeugnis (Kritisch)
--   • night_work_ausnahme_fehlt → nur Ausnahmeregelung (Kritisch, 03.08.2026)
--
-- Program.cs führt dasselbe idempotent beim Startup aus.
-- Lauf in TablePlus optional; dann ./deploy.sh
-- ════════════════════════════════════════════════════════════════════════

UPDATE dashboard_warning_config
   SET label = 'Nachtarbeit-Arztzeugnis fehlt',
       severity_base = 'critical'
 WHERE category = 'night_work_exam_fehlt'
   AND label = 'Nachtarbeit-Nachweise fehlen';

INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('night_work_ausnahme_fehlt', 'Nachtarbeit-Ausnahmeregelung fehlt', TRUE, NULL, NULL, 'critical', NULL, FALSE, 23, 52, 'none')
ON CONFLICT (category) DO NOTHING;

UPDATE dashboard_warning_config
   SET label = 'Nachtarbeit-Ausnahmeregelung fehlt',
       severity_base = 'critical'
 WHERE category = 'night_work_ausnahme_fehlt'
   AND (severity_base IS DISTINCT FROM 'critical'
        OR label IS DISTINCT FROM 'Nachtarbeit-Ausnahmeregelung fehlt');
