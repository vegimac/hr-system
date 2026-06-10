-- Walter-Vorgabe 28.05.2026: ineligible MA (krank, kein Vertrag, falsche
-- Filiale, Probezeit, Austritt mitten in Periode) sollen im Akonto-Workflow
-- SICHTBAR sein — nicht stillschweigend ausgeschlossen. Dafür: neue Spalte
-- error_reason. Beim Start-Endpoint wird auch für ineligible MA ein Datensatz
-- angelegt, mit Status='BERECHNET', NettoAkonto=0 und error_reason gefüllt.
-- Das Frontend rendert solche Zeilen rot mit Fehlertext statt grüner Akonto-
-- Zeile.
--
-- In TablePlus ausführen.

ALTER TABLE akonto_zahlung
    ADD COLUMN IF NOT EXISTS error_reason TEXT;

ALTER TABLE akonto_zahlung
    ADD COLUMN IF NOT EXISTS force_payout BOOLEAN NOT NULL DEFAULT FALSE;

COMMENT ON COLUMN akonto_zahlung.error_reason IS
    'Ausschluss-Grund wenn IsEligible=false (z.B. „Krank am Stichtag (07.01.–18.03.)"). NULL = normale Akonto-Zahlung.';
COMMENT ON COLUMN akonto_zahlung.force_payout IS
    'GF-Override: trotz Ausschluss-Grund Akonto auszahlen (Default FALSE). Beim Re-Berechnen wird der Eligibility-Check ignoriert wenn TRUE.';

-- Kontrolle
SELECT COUNT(*) AS total,
       COUNT(error_reason) AS with_error,
       COUNT(*) FILTER (WHERE force_payout) AS force_payout_count
FROM akonto_zahlung;
