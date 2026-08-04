-- QST-Kanton-Mismatch-Wächter (Walter-Vorgabe 04.08.2026)
-- Neue Dashboard-Warn-Kategorie «qst_kanton_mismatch»: der Kanton der aktiven
-- QST-Erfassung (employee_quellensteuer.steuerkanton) weicht vom Wohnkanton
-- (employee.canton_code) ab — z.B. nach Adressänderung oder easy@work-Sync —
-- und der Lohnlauf würde mit dem falschen Tarif rechnen.
-- Idempotent (ON CONFLICT DO NOTHING); wird zusätzlich beim App-Start
-- in Program.cs geseedet. Zum Copy-Paste in TablePlus.

INSERT INTO dashboard_warning_config
    (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
VALUES
    ('qst_kanton_mismatch', 'QST-Kanton ≠ Wohnkanton', TRUE, NULL, NULL, 'critical', NULL, FALSE, 24, 16, 'red')
ON CONFLICT (category) DO NOTHING;
