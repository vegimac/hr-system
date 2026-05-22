-- ====================================================================
-- BVG Max-Obergrenze: flacher Monats-Cap auf die koordinierte Basis
-- (Walter-Vorgabe 22.05.2026)
-- ====================================================================
-- BVG ist nur bis zum „Max. pflichtigen Betrag" (koordinierter Lohn) versichert:
-- 5'355/Mt. (= obere BVG-Grenze − Koordinationsabzug). Neue Spalte
-- `max_base_flat_monthly` deckelt die Basis FLACH in jedem Monat — anders als
-- `max_base_monthly` (ALV/NBU) löst dieser Cap KEIN Dezember-Aufrollverfahren aus
-- (BVG wird nicht aufgerollt). basis = min(basis, max_base_flat_monthly).
--
-- Gilt für die Haupt-BVG (Uno Basis, code 'BVG'). Der Zusatz (BVG_ZUSATZ) rechnet
-- auf dem Koordinationsabzug selbst (2'205 fix) → braucht keinen zusätzlichen Cap.
--
-- TablePlus: reinen Block ausführen. Idempotent.
-- ====================================================================

ALTER TABLE social_insurance_rate
    ADD COLUMN IF NOT EXISTS max_base_flat_monthly numeric(10,2);

UPDATE social_insurance_rate
SET    max_base_flat_monthly = 5355.00
WHERE  code = 'BVG'
  AND  max_base_flat_monthly IS NULL;
