-- Willkommenstag-SMS an den KANDIDATEN (Walter 11.08.2026).
-- Neuer Ablauf: Die Einladung zum Willkommenstag geht DIREKT an den
-- Kandidaten (vor der easy@work-Erfassung) über /willkommen/{token}
-- mit Annehmen/Absagen. Buchungen hängen via kandidat_id am Kandidaten
-- und werden beim Verknüpfen an den MA (employee_id) übergeben.
-- Läuft auch idempotent beim Server-Start (Program.cs) — Ausführung in
-- TablePlus ist optional/dokumentarisch.

ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS kandidat_id integer;
ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS willkommen_token_hash text;
ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS willkommen_gesendet_am timestamp without time zone;
