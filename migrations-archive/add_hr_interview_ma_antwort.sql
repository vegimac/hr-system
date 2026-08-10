-- Termin-Antwort des MA über den Vertrags-Link (Walter 10.08.2026).
-- Der MA kann seinen Onboarding-Termin auf der Landing-Page annehmen/absagen;
-- HR bekommt eine Mitteilung ins HR-Postfach. employee_id verankert die
-- Buchung fest am MA (bisher nur Name/Telefon).
-- Läuft auch idempotent beim Server-Start (Program.cs) — Ausführung in
-- TablePlus ist optional/dokumentarisch.

ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS employee_id integer;
ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS ma_antwort text;
ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS ma_antwort_am timestamp without time zone;
