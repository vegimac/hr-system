-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 06.06.2026:
--   d.velop-XG-ID am Dokument-Record persistieren — eindeutige Identifikation,
--   damit der Metadaten-Backfill nicht mehr per Dateiname raten muss.
--
-- Hintergrund: d.velop erlaubt mehrere Dokumente mit gleichem Dateinamen
-- (z.B. „Neue Vertrag Parminder 580083.PDF" als XG00011124 UND XG00011143).
-- Beim Backfill via Dateiname-Match flippten wir beim Re-Run zwischen den
-- zwei Excel-Zeilen, niemals „Schon aktuell". Per XG-ID ist's eindeutig.
--
-- Lauf in TablePlus, dann ./deploy.sh
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE employee_dokument
    ADD COLUMN IF NOT EXISTS dvelop_dokument_id varchar(20);

CREATE INDEX IF NOT EXISTS ix_emp_dok_dvelop_id ON employee_dokument(dvelop_dokument_id)
    WHERE dvelop_dokument_id IS NOT NULL;

-- Sanity-Check
SELECT COUNT(*) AS dokumente_total,
       SUM(CASE WHEN dvelop_dokument_id IS NOT NULL THEN 1 ELSE 0 END) AS mit_xg_id
  FROM employee_dokument;
