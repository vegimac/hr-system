-- Probezeit-Vorgabe pro Filiale (Walter-Vorgabe 29.06.2026)
-- Gespeichert als: 14 = 14 Tage, 1/2/3 = Monate. NULL = keine Vorgabe.
-- KEINE manuelle Verlängerung (verlängert sich später automatisch bei
-- Krankheit/Unfall/Absenz — eigener Schritt).
-- In TablePlus ausführen (nicht via psql-CLI).

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS probation_months integer;
