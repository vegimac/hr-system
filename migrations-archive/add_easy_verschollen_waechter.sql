-- Verschollen-Wächter (Walter 05.08.2026): easy@work ist McDonald's-weit —
-- wechselt ein MA zu einem FREMDEN Franchise, verschwindet sein Datensatz aus
-- unseren Aktiv-Listen, und der Nacht-Sync (holt nur heute Aktive) würde einen
-- vergessenen Austritt NIE bemerken. Der Wächter (Stufe 3 im Auto-Sync)
-- markiert solche MA; das Dashboard zeigt in ihrer Filiale eine kritische
-- Warnung «Austritt prüfen». Kein Auto-Austritt — Entscheidung bleibt bei HR.
--
-- Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE employee
ADD COLUMN IF NOT EXISTS easy_missing_since date;

INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('easy_verschollen', 'MA in easy@work verschollen', TRUE, NULL, NULL, 'critical', NULL, FALSE, 25, 15, 'red')
ON CONFLICT (category) DO NOTHING;
