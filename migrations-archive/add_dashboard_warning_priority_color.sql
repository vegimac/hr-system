-- ============================================================================
-- Warnungsverwaltung: Priorität + Warnfarbe (Walter-Vorgabe 19.07.2026)
-- todo_priority: kleinere Zahl = weiter oben in der ToDo-Liste
-- warn_color: none | red | red_overdue
--
-- Ausführung: direkt in TablePlus (reiner SQL-Block).
-- ============================================================================

ALTER TABLE dashboard_warning_config
    ADD COLUMN IF NOT EXISTS todo_priority INT  NOT NULL DEFAULT 100,
    ADD COLUMN IF NOT EXISTS warn_color    TEXT NOT NULL DEFAULT 'none';

-- Defaults gemäss vereinbarter Reihenfolge (nur wenn noch Default 100/none).
UPDATE dashboard_warning_config SET todo_priority = 10,  warn_color = 'red'
 WHERE category = 'permit_missing' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 20,  warn_color = 'red_overdue'
 WHERE category = 'permit_expiring' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 30,  warn_color = 'red_overdue'
 WHERE category = 'night_work_exam_fehlt' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 40,  warn_color = 'red_overdue'
 WHERE category = 'night_work_exam_expiring' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 50,  warn_color = 'red'
 WHERE category = 'minimum_wage_violation' AND todo_priority = 100 AND warn_color = 'none';
