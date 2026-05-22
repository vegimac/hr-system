-- SV-Sätze: Fibu-Position (Mirus-Lohnart-Code) für Fibu-Journal / Abacus
-- (Walter-Vorgabe 22.05.2026). Verlinkt jeden SV-Satz STABIL mit dem Kontoplan
-- (lohn_konto_mapping.position) — kein Text-Matching mehr.
-- In TablePlus ausführen.

ALTER TABLE social_insurance_rate
    ADD COLUMN IF NOT EXISTS fibu_position INT NULL;

-- Vorbelegung nach Code (Mirus-Konvention):
--   AHV→500, ALV→510, KTG→530, NBU/NBUV→540, BVG→550.
UPDATE social_insurance_rate SET fibu_position = 500 WHERE upper(code) IN ('AHV','AHV/IV/EO','AHVIVEO') AND fibu_position IS NULL;
UPDATE social_insurance_rate SET fibu_position = 510 WHERE upper(code) = 'ALV'                          AND fibu_position IS NULL;
UPDATE social_insurance_rate SET fibu_position = 530 WHERE upper(code) IN ('KTG','KTGV')                AND fibu_position IS NULL;
UPDATE social_insurance_rate SET fibu_position = 540 WHERE upper(code) IN ('NBU','NBUV')                AND fibu_position IS NULL;
UPDATE social_insurance_rate SET fibu_position = 550 WHERE upper(code) IN ('BVG','BVGV')                AND fibu_position IS NULL;
UPDATE social_insurance_rate SET fibu_position = 590 WHERE upper(code) IN ('BVG_ZUSATZ','BVGZUSATZ')    AND fibu_position IS NULL;

-- Kontrolle: welche aktiven SV-Sätze haben noch KEINE Fibu-Position?
-- SELECT code, name FROM social_insurance_rate WHERE is_active AND fibu_position IS NULL;
