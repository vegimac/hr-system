-- ====================================================================
-- Migration: Feiertag-Tage-Saldo auf payroll_saldo
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_feiertag_tage_saldo.sql
-- ====================================================================
--
-- Zweck:
--   Nur für FIX / FIX-M: monatliche Gutschrift +0.5 Tage, Abzug bei
--   FEIERTAG-Absenzen (anteilig nach Prozent: 100% = 1 Tag, 50% = 0.5).
--
--   Für MTP/UTP bleibt das Feld 0. Der Saldo wird nur im Lohnzettel unten
--   angezeigt (kein Einfluss auf Lohn, keine Lohnposition).
-- ====================================================================

BEGIN;

ALTER TABLE payroll_saldo
    ADD COLUMN IF NOT EXISTS feiertag_tage_saldo NUMERIC(8,4) NOT NULL DEFAULT 0;

COMMENT ON COLUMN payroll_saldo.feiertag_tage_saldo IS
    'Feiertag-Tage-Saldo (nur FIX/FIX-M). +0.5/Monat, −Tage × Prozent bei FEIERTAG-Absenz.';

COMMIT;
