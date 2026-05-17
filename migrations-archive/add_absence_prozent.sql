-- ====================================================================
-- Migration: Prozent-Feld für Absenzen (teilweise Krankheit/Unfall etc.)
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_absence_prozent.sql
-- ====================================================================
--
-- Zweck:
--   Krankheit, Unfall und andere Absenzen können teilweise sein
--   (z.B. 50% krank, 20% krank). Das Prozent gilt sowohl für die
--   Zeitgutschrift als auch für die Lohnfortzahlungs-/KTG-Rechnung.
--
--   Beispiel: MA ist 50% krank an 4 Tagen bei 42h/Woche
--     Stundengutschrift = 4 × (42 / 5) × (50/100) = 16.80 h
--     Karenz-Tage       = 4 × 0.5                 = 2.0 Tage
--     Tagessatz-Anwendung: 50% vom Tagessatz pro Tag
--
-- Default 100: für alle bestehenden Einträge ist das Ergebnis identisch
-- zur bisherigen Berechnung.
-- ====================================================================

BEGIN;

ALTER TABLE absence
    ADD COLUMN IF NOT EXISTS prozent NUMERIC(5,2) NOT NULL DEFAULT 100;

ALTER TABLE absence
    DROP CONSTRAINT IF EXISTS absence_prozent_check;
ALTER TABLE absence
    ADD CONSTRAINT absence_prozent_check
        CHECK (prozent > 0 AND prozent <= 100);

COMMENT ON COLUMN absence.prozent IS
    'Ausfall-Prozent: 100 = voll krank/abwesend, 50 = halb krank, etc. Wird auf Stunden- und Lohn-Berechnung multiplikativ angewendet.';

COMMIT;
