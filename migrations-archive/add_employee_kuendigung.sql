-- Kündigungs-Daten am MA (Walter-Vorgabe 16.07.2026).
-- «Kündigung ausgesprochen am» + «Kündigung per» (letzter Arbeitstag gemäss
-- Kündigungsschreiben). Gesetzt beim Erstellen des Kündigungsschreibens,
-- gelöscht beim Kündigungsrückzug. NICHT das Austrittsdatum.
-- (Läuft auch idempotent beim Server-Start in Program.cs.)

ALTER TABLE employee
ADD COLUMN IF NOT EXISTS kuendigung_ausgesprochen_am date,
ADD COLUMN IF NOT EXISTS kuendigung_per date;
