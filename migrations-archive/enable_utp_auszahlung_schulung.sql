-- ====================================================================
-- Migration: UTP-Auszahlung für Schulung aktivieren
-- Ausführen mit:
--   psql -d hr_system -U postgres -f enable_utp_auszahlung_schulung.sql
-- ====================================================================
--
-- Zweck:
--   Walter-Vorgabe (10.05.2026): Bei UTP-Mitarbeitern soll die Absenz
--   "Schulung / andere Absenz" automatisch Stunden gutschreiben
--   (1/5 der Betriebs-Wochenstunden pro Schulungstag, also z.B. 8.4h
--   bei 42h-Woche). Die Stunden werden — analog zu NACHT_KOMP —
--   als Stundenlohn ausbezahlt (utpAuszahlungStunden im PayrollController).
--
--   Bei FIX und MTP funktioniert die Schulung-Zeitgutschrift bereits
--   korrekt (über Zeitgutschrift=true, Modus 1/5).
--
-- Logik im Code (PayrollController.cs ~Zeile 478):
--   if (isUTP && typCfg.UtpAuszahlung)
--       utpAuszahlungStunden += hours;
--
-- Anpassbar via Admin-UI: "Absenz-Typen" → SCHULUNG → bearbeiten →
-- Checkbox "UTP — als Stundenlohn auszahlen".
-- ====================================================================

UPDATE absenz_typ
SET utp_auszahlung = true
WHERE code = 'SCHULUNG';

-- Verifikation (optional)
SELECT code, bezeichnung, zeitgutschrift, gutschrift_modus, utp_auszahlung
FROM absenz_typ
WHERE aktiv = true
ORDER BY sort_order;
