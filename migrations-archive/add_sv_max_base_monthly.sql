-- ============================================================================
-- ALV/NBU-Höchstlohn (Walter-Vorgabe 20.05.2026)
-- ----------------------------------------------------------------------------
-- ALV und NBU (UVG) sind nur bis zum Höchstlohn beitragspflichtig:
--   CHF 148'200 / Jahr = CHF 12'350 / Monat.
-- Bisher hatte social_insurance_rate keine Obergrenze → Gutverdiener wurden
-- über dem Höchstlohn zu hoch belastet. Neue Spalte max_base_monthly: ist sie
-- gesetzt, deckelt der PayrollController die Beitragsbasis darauf
-- (basis = min(basis, max_base_monthly)). NULL = unbegrenzt (z.B. AHV/IV/EO).
--
-- Editierbar im UI: Systemeinstellungen → SV-Sätze → Feld „Höchstlohn/Mt.".
-- Ändert sich der Höchstlohn, dort anpassen (bzw. „Neu ab" für eine Folge-
-- Version mit neuem Gültig-ab-Datum).
--
-- Ausführung: in TablePlus.
-- ============================================================================

-- 1) Spalte anlegen (idempotent)
ALTER TABLE social_insurance_rate
    ADD COLUMN IF NOT EXISTS max_base_monthly numeric(10,2);

-- 2) Aktuellen Höchstlohn auf ALV + NBU setzen (12'350/Mt. = 148'200/Jahr)
UPDATE social_insurance_rate
SET    max_base_monthly = 12350.00
WHERE  code IN ('ALV', 'NBUV')
  AND  max_base_monthly IS NULL;

-- 3) Kontrolle
-- SELECT code, name, rate, basis_type, max_base_monthly, valid_from, valid_to, is_active
-- FROM social_insurance_rate ORDER BY code, valid_from;
