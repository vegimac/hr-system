-- Abacus-Export: MWST-Konfiguration pro Kontoplan-Zeile + Buchungsnummer
-- pro Lohnperiode (Treuhänder-Vorgabe Simone 05.08.2026).
--
-- 1) lohn_konto_mapping: MWST-Konto + MWST-Code (wie Mirus-Fibukonto-Dialog
--    «Mehrwertsteuer»). NULL = Buchung ohne Steuerfelder im AbaConnect-XML.
--    Seed: alle Personalaufwand-Zeilen (Soll 4xxx / Gegen 1920) bekommen
--    1067 / Code 200 (0% MWST) — deckungsgleich mit dem Mirus-Export.
-- 2) payroll_periode: Abacus-Buchungsnummer des Lohnlaufs (DocumentNumber
--    im XML, z.B. 50006), pro Periode persistiert; UI schlägt +1 vor.
--
-- Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE lohn_konto_mapping
ADD COLUMN IF NOT EXISTS mwst_konto varchar(10),
ADD COLUMN IF NOT EXISTS mwst_code  varchar(10);

UPDATE lohn_konto_mapping
SET mwst_konto = '1067', mwst_code = '200'
WHERE mwst_konto IS NULL AND mwst_code IS NULL
  AND fibukonto LIKE '4%' AND gegenkonto = '1920';

ALTER TABLE payroll_periode
ADD COLUMN IF NOT EXISTS fibu_buchungsnummer varchar(20);
