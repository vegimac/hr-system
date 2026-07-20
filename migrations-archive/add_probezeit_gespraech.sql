-- Probezeitgespräch am MA (Walter 20.07.2026)
-- 1. und 2. Gespräch: Datum der Durchführung + verknüpftes Protokoll
-- (Dokumenttyp «Probezeitgespräch» unter Mitarbeiterentwicklung).
-- TablePlus: diesen Block ausführen, danach deployen.

ALTER TABLE employee
  ADD COLUMN IF NOT EXISTS probezeit_gespraech1_am DATE;

ALTER TABLE employee
  ADD COLUMN IF NOT EXISTS probezeit_gespraech1_dokument_id INTEGER
    REFERENCES employee_dokument(id) ON DELETE SET NULL;

ALTER TABLE employee
  ADD COLUMN IF NOT EXISTS probezeit_gespraech2_am DATE;

ALTER TABLE employee
  ADD COLUMN IF NOT EXISTS probezeit_gespraech2_dokument_id INTEGER
    REFERENCES employee_dokument(id) ON DELETE SET NULL;
