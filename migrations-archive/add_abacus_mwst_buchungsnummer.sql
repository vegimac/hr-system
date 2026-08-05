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

-- Verifiziert gegen Mirus export.xls (05.08.2026): dort tragen EXAKT die
-- 4xxx→1920-Zeilen Code 200 / 0% / Konto 1067 (122 Zeilen); zusätzlich
-- Position 600 (Naturallohn Verpflegung / Privatanteil Geschäftswagen,
-- Soll 1920) Code 311 / 8.1% / Konto 2065 — vom Journal heute nicht
-- gebucht, Konfiguration aber vollständig übernommen.

ALTER TABLE lohn_konto_mapping
ADD COLUMN IF NOT EXISTS mwst_konto   varchar(10),
ADD COLUMN IF NOT EXISTS mwst_code    varchar(10),
ADD COLUMN IF NOT EXISTS mwst_prozent numeric(5,2);

UPDATE lohn_konto_mapping
SET mwst_konto = '1067', mwst_code = '200', mwst_prozent = 0
WHERE mwst_konto IS NULL AND mwst_code IS NULL
  AND fibukonto LIKE '4%' AND gegenkonto = '1920';

UPDATE lohn_konto_mapping
SET mwst_konto = '2065', mwst_code = '311', mwst_prozent = 8.1
WHERE mwst_konto IS NULL AND mwst_code IS NULL
  AND position = 600 AND fibukonto = '1920';

ALTER TABLE payroll_periode
ADD COLUMN IF NOT EXISTS fibu_buchungsnummer varchar(20);
