-- ============================================================================
-- QST-Flag-Fix (Walter 17.08.2026, Erkenntnis aus der ersten Basen-Kontrolle):
-- Die aus dem ELM-Lohnraster übernommenen Lohnarten wurden mit
-- qst_pflichtig = false angelegt, weil das Raster-Attribut «qstpfl» im
-- Export durchgängig leer ist («Periodische Lohnart (QST)» beschreibt nur
-- die Periodizität, nicht die Pflicht). Korrekt gilt: QST-Pflicht folgt der
-- AHV-Pflicht. Betroffen sind nur die NEU übernommenen Positionen — die
-- gewachsenen OneCrew-Positionen (10, 2, 3, 20, 60, 70 …) waren korrekt.
-- Idempotent; Positionen, die es (noch) nicht gibt, werden übersprungen.
-- Ausführung: TablePlus, reiner Copy-Paste-Block.
-- ============================================================================
UPDATE lohnposition
SET qst_pflichtig = true
WHERE code IN ('180.1', '195.1', '195.2', '40.1', '30.1',
               '55.10', '55.11', '55.12', '110.1')
  AND is_active = true
  AND ahv_alv_pflichtig = true
  AND qst_pflichtig = false;
