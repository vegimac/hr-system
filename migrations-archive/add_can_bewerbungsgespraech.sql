-- Walter 07.09.2026: Wer führt in dieser Filiale Bewerbungsgespräche?
-- Nur diese Personen sind im Gesprächsmodus unter «Gespräch geführt von» wählbar.
-- Wird beim Start automatisch ausgeführt (Program.cs), hier nur zur Doku —
-- bei manueller Ausführung in BEIDE Datenbanken (hr_system_test und hrsystem).
ALTER TABLE user_branch_access
    ADD COLUMN IF NOT EXISTS can_bewerbungsgespraech boolean NOT NULL DEFAULT false;
