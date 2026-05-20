-- =====================================================================
-- Akonto-Workflow: neuer per-MA-Status HR_BESTAETIGT
-- Walter-Vorgabe 17.05.2026
--
-- Pro-MA-HR-Bestätigung (Symmetrie zum GF-Workflow): HR bestätigt jeden
-- Lohnzettel einzeln. Status pro MA wechselt FREIGEGEBEN_GF → HR_BESTAETIGT.
-- Sobald alle MA durch sind, transitioniert die Periode automatisch
-- BEI_HR → HR_FREIGEGEBEN. Auszahlen (DTA) akzeptiert HR_BESTAETIGT
-- und (für Legacy-Daten) auch FREIGEGEBEN_GF.
--
-- Idempotent: DROP + ADD CONSTRAINT. Funktioniert auch wenn die Migration
-- aus add_akonto_lohn_phase1.sql / fix_akonto_zahlung_status_check.sql
-- bereits gelaufen ist.
-- =====================================================================

ALTER TABLE akonto_zahlung
    DROP CONSTRAINT IF EXISTS akonto_zahlung_status_check;

ALTER TABLE akonto_zahlung
    ADD CONSTRAINT akonto_zahlung_status_check
    CHECK (status IN ('BERECHNET', 'FREIGEGEBEN_GF', 'HR_BESTAETIGT', 'AUSBEZAHLT', 'STORNIERT'));

-- Kontrolle
SELECT conname, pg_get_constraintdef(oid) AS def
FROM   pg_constraint
WHERE  conname = 'akonto_zahlung_status_check';
