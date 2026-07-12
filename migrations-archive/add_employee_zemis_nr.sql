-- ZEMIS-Nr-Konsolidierung (Walter 12.07.2026, v2).
-- Ursprünglich legte diese Migration eine NEUE Spalte employee.zemis_nr an —
-- dabei existierte bereits employee.zemis_number (personenbezogenes Stammdatum).
-- v2 rettet allfällige OCR-Werte in das bestehende Feld und entfernt das Duplikat.
-- Läuft auch idempotent beim Server-Start (Program.cs). TablePlus-Block:

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name='employee' AND column_name='zemis_nr') THEN
        UPDATE employee SET zemis_number = zemis_nr
         WHERE zemis_number IS NULL AND zemis_nr IS NOT NULL;
        ALTER TABLE employee DROP COLUMN zemis_nr;
    END IF;
END $$;
