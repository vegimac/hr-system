-- ====================================================================
-- BVG Min. pflichtige Basis + Eintrittsschwelle (Walter-Vorgabe 22.05.2026)
-- ====================================================================
-- Zwei weitere BVG-Grenzen aus der Mirus-Konfig:
--   • min_base_monthly      = 315.00   → Min. koordinierte Basis/Mt. Versicherte
--                                         zahlen mind. darauf (auch wenn Brutto −
--                                         Koordinationsabzug kleiner/0 ist).
--   • entry_threshold_yearly = 22'680  → Eintrittsschwelle. Liegt der hochgerechnete
--                                         Jahreslohn (BVG-Brutto × 12) darunter, ist
--                                         der MA NICHT BVG-versichert → Basis 0.
--
-- Reihenfolge in der Engine (BuildResult): Schwelle → Min → Max (5'355).
-- Gilt für die Haupt-BVG (Uno Basis, code 'BVG'). Der Zusatz (BVG_ZUSATZ) hat in
-- Mirus weder Schwelle noch Min → bleibt NULL.
--
-- TablePlus: reinen Block ausführen. Idempotent.
-- ====================================================================

ALTER TABLE social_insurance_rate
    ADD COLUMN IF NOT EXISTS min_base_monthly       numeric(10,2);
ALTER TABLE social_insurance_rate
    ADD COLUMN IF NOT EXISTS entry_threshold_yearly numeric(10,2);

UPDATE social_insurance_rate
SET    min_base_monthly       = 315.00,
       entry_threshold_yearly = 22680.00
WHERE  code = 'BVG'
  AND  min_base_monthly IS NULL
  AND  entry_threshold_yearly IS NULL;
