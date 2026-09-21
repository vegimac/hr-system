-- Schema-Stand 17 (21.09.2026): QST periodisch/einmalig als Flag an der Lohnart.
-- Läuft idempotent beim Start (Program.cs); hier nur als Doku für TablePlus.
ALTER TABLE lohnposition ADD COLUMN IF NOT EXISTS qst_periodisch BOOLEAN NOT NULL DEFAULT true;
-- Seed (einmalig): Swissdec 12xx/13xx/14xx/15xx/196x/197x/1980, 1067, 1168, 3001, 3034 und Kategorie «Bonus»
-- → qst_periodisch = false. Danach Pflege im UI: Systemeinstellungen → Lohnpositionen → «QST per.».
