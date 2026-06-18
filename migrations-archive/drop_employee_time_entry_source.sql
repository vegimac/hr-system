-- ════════════════════════════════════════════════════════════════════════
-- employee_time_entry.source entfernen
-- Walter-Vorgabe 17.06.2026
--
-- Stempelzeiten kommen ab sofort ausschliesslich aus easy@work via API-Sync.
-- Die `source`-Spalte (Werte: 'manual', 'import', 'easywork-api') ist damit
-- konzeptionell konstant „easy@work" und wird nicht mehr gepflegt.
--
-- Reihenfolge: ZUERST diese Migration in TablePlus laufen lassen, DANN den
-- aktualisierten Code deployen. (Der neue Code projiziert die Spalte schon
-- nicht mehr — wenn der Deploy zuerst läuft, EF beschwert sich nicht, weil
-- es das Property nicht mehr hat. Beide Reihenfolgen sind also safe.)
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE employee_time_entry DROP COLUMN IF EXISTS source;
