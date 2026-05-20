-- Walter-Vorgabe 19.05.2026: Akonto bekommt ein eigenes Bank-Ausführungsdatum
-- (analog Definitivlauf: payroll_periode.auszahlungsdatum).
-- Wird im DTA-XML als ReqdExctnDt verwendet und ist der Cutoff für den
-- Admin-Reset (heute > akonto_auszahlungsdatum → 409 PAYOUT_DATE_REACHED).

ALTER TABLE payroll_periode
    ADD COLUMN IF NOT EXISTS akonto_auszahlungsdatum DATE;

-- Für historische Datensätze, die schon AUSBEZAHLT sind: fülle das neue
-- Feld aus dem Klick-Datum (akonto_ausbezahlt_at), damit die DTA-Re-Generierung
-- weiterhin funktioniert.
UPDATE payroll_periode
SET    akonto_auszahlungsdatum = akonto_ausbezahlt_at::date
WHERE  akonto_status = 'AUSBEZAHLT'
  AND  akonto_auszahlungsdatum IS NULL
  AND  akonto_ausbezahlt_at IS NOT NULL;

-- Kontrolle
SELECT akonto_status, COUNT(*) AS anzahl,
       MIN(akonto_auszahlungsdatum) AS frueh,
       MAX(akonto_auszahlungsdatum) AS spaet
FROM   payroll_periode
GROUP  BY akonto_status
ORDER  BY akonto_status;
