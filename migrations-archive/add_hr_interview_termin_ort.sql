-- Durchführungs-Ort des Willkommenstags (Walter-Vorgabe 12.08.2026):
-- frei editierbar pro Termin (Welcome-Day-Verwaltung), erscheint auf der
-- Kandidaten-Link-Seite (📍-Zeile) und im Kalender-Eintrag (.ics).
-- NULL = Default «Schulungsraum, Luzernerstr. 2, Zofingen».
-- Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE hr_interview_termin ADD COLUMN IF NOT EXISTS ort text;
