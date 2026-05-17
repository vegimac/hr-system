-- ─────────────────────────────────────────────────────────────────────────
-- Filial-Bankverbindung (Auftraggeber-Konto für DTA / pain.001)
-- ─────────────────────────────────────────────────────────────────────────
-- Jede Filiale hat ein eigenes Lohnkonto bei einer Schweizer Bank. Beim
-- Lohnlauf-DTA wird dieses Konto als Cdtr (Auftraggeber) im pain.001-XML
-- übermittelt — von hier geht der Sammelauftrag an alle MA-Banken.
-- Auch im Lohnzettel-Footer kann diese Info erscheinen (z.B. "Schaub
-- Restaurants GmbH · Filiale Sursee · Konto: CH... bei PostFinance").
-- ─────────────────────────────────────────────────────────────────────────

ALTER TABLE company_profile
  ADD COLUMN IF NOT EXISTS iban      varchar(34),
  ADD COLUMN IF NOT EXISTS bic       varchar(15),
  ADD COLUMN IF NOT EXISTS bank_name varchar(200);
