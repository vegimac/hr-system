-- Persönliche Startseite pro Benutzer (Walter-Vorgabe 14.08.2026).
-- dashboard | todos | mitarbeiter | lohn | manager-dienstplan; NULL = Dashboard.
-- Läuft auch idempotent beim Server-Start (Program.cs).
ALTER TABLE app_user ADD COLUMN IF NOT EXISTS start_page text;
