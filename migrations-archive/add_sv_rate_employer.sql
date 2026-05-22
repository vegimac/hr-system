-- ====================================================================
-- AG-Sozialbeiträge: Arbeitgeber-Satz pro SV-Satz (Walter 22.05.2026)
-- ====================================================================
-- Bisher kennt social_insurance_rate nur den AN-Anteil (Spalte `rate`).
-- Fürs Fibu-Journal (AG-Beiträge → 4060/4061/4062) braucht es den
-- Arbeitgeber-Satz. Neue Spalte `rate_employer` (NULL = kein AG-Anteil /
-- noch nicht gepflegt → wird im Journal NICHT gebucht).
--
-- AG-Beitrag im Journal = rate_employer × dieselbe Basis wie der AN-Abzug.
-- Berührt Konto 1920 NICHT (Aufwand 406x ↔ Verbindlichkeit 207x/208x/209x).
--
-- Firm-Werte (CH-Standard): AHV/IV/EO und ALV haben AG = AN → gespiegelt.
-- KTG, BVG, NBU(→BU), BVG_ZUSATZ sind firmen-/kassenspezifisch → bleiben
-- NULL; Walter pflegt sie in Systemeinstellungen → SV-Sätze → „AG-Satz %".
-- FAK (nur AG, kantonsspezifisch) folgt als eigener Schritt (Phase 2).
--
-- TablePlus: reinen Block ausführen.
-- ====================================================================

ALTER TABLE social_insurance_rate
    ADD COLUMN IF NOT EXISTS rate_employer numeric(6,3);

-- AHV/IV/EO + ALV: Arbeitgeber-Anteil = Arbeitnehmer-Anteil (gespiegelt).
UPDATE social_insurance_rate
SET    rate_employer = rate
WHERE  code IN ('AHV', 'ALV')
  AND  rate_employer IS NULL;
