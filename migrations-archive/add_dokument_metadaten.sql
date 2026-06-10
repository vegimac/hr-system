-- ============================================================================
-- Dokument-Metadaten: Zeitstempel + Akteure (Walter-Vorgabe 24.05.2026)
--
-- Übernimmt die d.velop-Felder zu jedem Dokument:
--   erstellt_am         = "Erstellt am"
--   geaendert_am        = "Geändert am"        (Eintrag in d.velop zuletzt geändert)
--   datei_geaendert_am  = "Datei geändert am"  (Datei selbst geändert, z.B. PDF gedreht)
--   zugriff_am          = "Zugriffsdatum"      (zuletzt angeschaut)
--   geaendert_von       = wer zuletzt geändert hat (Anzeigename)
--   zugriff_von         = wer zuletzt angeschaut hat (Anzeigename)
--
-- In TablePlus ausführen (kein psql-Wrapper nötig). Idempotent.
-- ============================================================================
ALTER TABLE employee_dokument
    ADD COLUMN IF NOT EXISTS erstellt_am         timestamp NULL,
    ADD COLUMN IF NOT EXISTS geaendert_am        timestamp NULL,
    ADD COLUMN IF NOT EXISTS datei_geaendert_am  timestamp NULL,
    ADD COLUMN IF NOT EXISTS zugriff_am          timestamp NULL,
    ADD COLUMN IF NOT EXISTS geaendert_von       text NULL,
    ADD COLUMN IF NOT EXISTS zugriff_von         text NULL;
