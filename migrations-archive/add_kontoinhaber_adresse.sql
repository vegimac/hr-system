-- ─────────────────────────────────────────────────────────────────────────
-- Abweichende Empfänger-Adresse pro Bankverbindung
-- ─────────────────────────────────────────────────────────────────────────
-- Praxisfall: Revolut/Wise/N26 — das Konto läuft formal auf den MA, aber für
-- den SEPA-Zahlungsverkehr muss die Empfänger-Bank (z.B. "Revolut Bank UAB"
-- in Vilnius) als Cdtr (Creditor) übermittelt werden, sonst lehnt die
-- Schweizer Auftraggeber-Bank die Zahlung ab. Der MA-Name wandert in die
-- Zahlungsreferenz.
--
-- Felder können NULL bleiben — bei Inland-Konten (CH-Postfinance, Raiffeisen,
-- UBS etc.) ist der MA selbst der Empfänger und es muss nichts erfasst werden.
-- ─────────────────────────────────────────────────────────────────────────

ALTER TABLE employee_bank_account
  ADD COLUMN IF NOT EXISTS kontoinhaber_strasse varchar(200),
  ADD COLUMN IF NOT EXISTS kontoinhaber_plz     varchar(20),
  ADD COLUMN IF NOT EXISTS kontoinhaber_ort     varchar(120),
  ADD COLUMN IF NOT EXISTS kontoinhaber_land    varchar(2);
