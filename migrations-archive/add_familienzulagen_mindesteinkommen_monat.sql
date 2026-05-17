-- ====================================================================
-- Migration: Mindesteinkommen pro MONAT zum Familienzulagen-Tarif
-- Ausführen mit:
--   psql -d <datenbank> -f add_familienzulagen_mindesteinkommen_monat.sql
-- ====================================================================
--
-- Zweck:
--   Bisher hat der Tarif nur ein Jahres-Mindesteinkommen (FamZG-Standard
--   7'350 CHF/Jahr). Die kantonalen FAK-Stellen (z.B. GastroSocial für LU)
--   prüfen aber das AHV-pflichtige Mindesteinkommen pro MONAT — bei LU:
--   CHF 630.00 pro Monat (höher als 7'350/12 = 612.50).
--
--   Wenn der MA in einem Monat unter dieser Schwelle bleibt, wird die
--   Familienzulage in diesem Monat NICHT ausbezahlt (siehe GastroSocial-
--   Bescheid: "Wenn das AHV-pflichtige Mindesteinkommen von CHF 630.– pro
--   Monat nicht erreicht wird, ist dies … umgehend mitzuteilen").
--
--   Der Lohnlauf prüft die Schwelle automatisch und unterdrückt die FAK-
--   Auszahlung bei Unterschreitung; Walter sieht im Lohnzettel einen
--   Hinweis, dass die FAK in diesem Monat zurückgehalten wurde.
-- ====================================================================

BEGIN;

ALTER TABLE familienzulagen_tarif
    ADD COLUMN IF NOT EXISTS mindesterwerbseinkommen_monat NUMERIC(10,2);

COMMENT ON COLUMN familienzulagen_tarif.mindesterwerbseinkommen_monat IS
    'AHV-pflichtiges Mindesteinkommen pro Monat. Wenn der MA in einem Monat unter diesem Wert bleibt, wird die FAK in diesem Monat nicht ausgezahlt. NULL = nur Jahres-Schwelle (mindesterwerbseinkommen_jahr / 12) anwenden.';

-- ── Korrekte Werte gemäss kantonaler FAK-Stellen ──
-- LU: 630 CHF/Monat (GastroSocial-Bescheid 2025/2026)
-- AG: 612.50 CHF/Monat (FamZG-Minimum, kann GS bestätigen)
-- BE: 612.50 CHF/Monat (FamZG-Minimum)
-- Bei NULL fällt der Lohnlauf auf jahres_wert/12 zurück.
UPDATE familienzulagen_tarif
   SET mindesterwerbseinkommen_monat = 630.00
 WHERE kanton_code = 'LU' AND mindesterwerbseinkommen_monat IS NULL;

UPDATE familienzulagen_tarif
   SET mindesterwerbseinkommen_monat = 612.50
 WHERE kanton_code IN ('AG', 'BE') AND mindesterwerbseinkommen_monat IS NULL;

-- ── Lohnpositionen 190.1/190.2: Null-Beträge SICHTBAR halten ──
-- Wenn das Mindesteinkommen unterschritten wird, erzeugt der Lohnlauf
-- die FAK-Zeile mit Betrag 0 und Hinweistext (Walter sieht den Anspruch
-- weiterhin auf dem Lohnzettel mit Erklärung). Damit der Renderer die
-- Zeile nicht wegfiltert, muss "nicht_drucken_wenn_null" auf false stehen.
UPDATE lohnposition
   SET nicht_drucken_wenn_null = FALSE
 WHERE code IN ('190.1', '190.2');

COMMIT;
