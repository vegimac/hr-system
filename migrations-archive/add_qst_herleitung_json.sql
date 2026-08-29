-- K4.1 Herleitungs-Snapshot (Walter 29.08.2026, QST-Bauplan 2.3 Punkt 1)
-- Server-seitig eingefrorene Herleitungsbasis pro QST-Version (JSON):
-- Zivilstand + seit, Konfession, Partner, Kinder-Detail, Wohnsituation,
-- Konkubinat, Resultat. Quelle für History-DIFF + Auto-Änderungsgrund.
-- Läuft idempotent auch beim App-Start (Program.cs).

ALTER TABLE employee_quellensteuer ADD COLUMN IF NOT EXISTS herleitung_json jsonb;
