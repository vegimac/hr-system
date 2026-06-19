-- ════════════════════════════════════════════════════════════════════════
-- easy@work Auto-Sync: Ein/Aus-Schalter pro Filiale (Walter-Vorgabe 19.06.2026)
--
-- Steuert, ob der tägliche automatische Stempelzeiten-Sync diese Filiale
-- erfasst. Default true (alle bisher gemappten Filialen laufen weiter mit).
-- Schaltbar im Filial-Detail → Tab „Einstellungen".
--
-- In TablePlus ausführen (kein psql-Wrapper).
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE easyatwork_branch_mapping
    ADD COLUMN IF NOT EXISTS auto_sync_enabled boolean NOT NULL DEFAULT true;
