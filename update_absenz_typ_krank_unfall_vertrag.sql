-- ====================================================================
-- Migration: basis_stunden für KRANK und UNFALL auf 'VERTRAG' setzen
-- Ausführen mit:
--   psql -d hr_system -U postgres -f update_absenz_typ_krank_unfall_vertrag.sql
-- ====================================================================
--
-- Regel-Präzisierung:
--   Bei Krankheit und Unfall wird pro ausgewähltem Tag 1/5 der
--   Wochenarbeitszeit gutgeschrieben. Die Basis hängt vom
--   Beschäftigungsmodell ab:
--     MTP          → GuaranteedHoursPerWeek (vertraglich garantierte Std.)
--     FIX / FIX-M  → NormalWeeklyHours der Filiale (Betrieb)
--     UTP          → keine automatische Gutschrift (unverändert)
--
-- Die Frontend-Logik (calcAbsHoursPreview) greift den Fall 'VERTRAG'
-- schon korrekt ab: bei MTP wird GuaranteedHoursPerWeek verwendet, für
-- andere Modelle fällt sie auf Betrieb zurück. Wir müssen also nur die
-- AbsenzTyp-Konfiguration für KRANK und UNFALL umstellen.
--
-- SCHULUNG und MILITAER bleiben vorerst auf BETRIEB (alte Regel).
-- ====================================================================

BEGIN;

UPDATE absenz_typ SET basis_stunden = 'VERTRAG'
 WHERE code IN ('KRANK', 'UNFALL');

-- Kontrolle (auskommentiert):
-- SELECT code, bezeichnung, gutschrift_modus, basis_stunden
--   FROM absenz_typ ORDER BY sort_order, code;

COMMIT;
