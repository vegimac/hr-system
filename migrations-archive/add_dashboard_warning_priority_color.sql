-- ============================================================================
-- Warnungsverwaltung: Priorität + Warnfarbe (Walter-Vorgabe 19.07.2026)
-- todo_priority: kleinere Zahl = weiter oben in der ToDo-Liste
-- warn_color: none | red | red_overdue
--
-- HINWEIS (22.07.2026): AUTORITATIV ist der idempotente Seed in Program.cs
-- (läuft bei jedem Server-Start). Diese Datei wurde an Program.cs angeglichen —
-- die frühere Fassung wich ab (night_work_exam_fehlt 30/red_overdue statt
-- 50/none, minimum_wage_violation 50 statt 15; probezeit_gespraech_offen +
-- kuendigung_ablauf + night_work_untersuch_fehlt fehlten ganz).
-- Manuelles Ausführen in TablePlus ist nur nötig, wenn man die DB VOR einem
-- Deploy vorbereiten will; sonst erledigt es der Startup-Seed.
--
-- Ausführung: direkt in TablePlus (reiner SQL-Block).
-- ============================================================================

ALTER TABLE dashboard_warning_config
    ADD COLUMN IF NOT EXISTS todo_priority INT  NOT NULL DEFAULT 100,
    ADD COLUMN IF NOT EXISTS warn_color    TEXT NOT NULL DEFAULT 'none';

-- Neuere Kategorien (falls noch nicht vorhanden; identisch zu Program.cs).
DELETE FROM dashboard_warning_config WHERE category = 'permit_expired';
INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('night_work_untersuch_fehlt', 'Nacht Untersuch fehlt', TRUE, NULL, NULL, 'critical', NULL, FALSE, 18, 30, 'red'),
    ('probezeit_gespraech_offen', 'Probezeitgespräch offen', TRUE, NULL, 14, 'warning', 'critical', TRUE, 19, 45, 'none'),
    ('kuendigung_ablauf', 'Vertragsende wegen Kündigung', TRUE, 14, 0, 'warning', 'critical', TRUE, 20, 55, 'red_overdue')
ON CONFLICT (category) DO NOTHING;

-- Defaults gemäss vereinbarter Reihenfolge (nur wenn noch Default 100/none) —
-- Werte 1:1 aus Program.cs.
UPDATE dashboard_warning_config SET todo_priority = 10,  warn_color = 'red'
 WHERE category = 'permit_missing' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 20,  warn_color = 'red_overdue'
 WHERE category = 'permit_expiring' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET label = 'Aufenthaltsbewilligung läuft ab'
 WHERE category = 'permit_expiring' AND label = 'Bewilligung läuft ab';
UPDATE dashboard_warning_config SET todo_priority = 30,  warn_color = 'red'
 WHERE category = 'night_work_untersuch_fehlt' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 40,  warn_color = 'red_overdue'
 WHERE category = 'night_work_exam_expiring' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 50,  warn_color = 'none'
 WHERE category = 'night_work_exam_fehlt' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 15,  warn_color = 'red'
 WHERE category = 'minimum_wage_violation' AND todo_priority = 100 AND warn_color = 'none';
UPDATE dashboard_warning_config SET todo_priority = 45,  warn_color = 'none'
 WHERE category = 'probezeit_gespraech_offen' AND todo_priority = 100;
UPDATE dashboard_warning_config SET todo_priority = 55,  warn_color = 'red_overdue'
 WHERE category = 'kuendigung_ablauf' AND todo_priority = 100;
