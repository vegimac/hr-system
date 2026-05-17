ALTER TABLE employee_bank_account
  ADD COLUMN is_hauptbank    BOOLEAN NOT NULL DEFAULT true,
  ADD COLUMN aufteilung_typ  VARCHAR(20) NOT NULL DEFAULT 'VOLL',
  ADD COLUMN aufteilung_wert NUMERIC(10,2);