-- ====================================================================
-- Migration: KTG/UVG-Tagessatz Override pro Mitarbeiter
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_employee_ktg_overrides.sql
-- ====================================================================
--
-- Walter-Anforderung (10.05.2026): Beim Wechsel vom alten Lohnsystem hat
-- ein MA u.U. schon einen vom Versicherer berechneten Tagessatz (z.B.
-- CHF 36.25). Unsere Auto-Berechnung würde diesen Wert übersteuern.
-- Plus: war im alten System die Karenzfrist schon abgelaufen, wird im
-- neuen System direkt 80% (Meldebetrag) gezahlt — nicht 88% (Karenz).
--
-- Felder:
--   ktg_tagessatz_manuell        decimal NULL  – manueller Tagessatz 100%.
--                                                 NULL = Auto-Berechnung.
--   ktg_karenz_abgeschlossen     bool DEFAULT false
--                                                 true = direkt 80% (kein
--                                                 88%-Schritt mehr).
-- ====================================================================

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS ktg_tagessatz_manuell numeric(10,2) NULL,
    ADD COLUMN IF NOT EXISTS ktg_karenz_abgeschlossen boolean NOT NULL DEFAULT false;

-- Verifikation
SELECT id, employee_number, first_name, last_name,
       ktg_tagessatz_manuell, ktg_karenz_abgeschlossen
FROM employee
WHERE ktg_tagessatz_manuell IS NOT NULL OR ktg_karenz_abgeschlossen = true
ORDER BY first_name;
