-- ZEMIS-Nr am MA (Walter 12.07.2026) — von der Ausweis-Rückseite (MRZ).
-- Läuft auch idempotent beim Server-Start (Program.cs); TablePlus-Doku.
ALTER TABLE employee ADD COLUMN IF NOT EXISTS zemis_nr text;
