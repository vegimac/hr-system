-- Walter-Vorgabe 30.07.2026: Handy-Nummer an Behörde (Kontakt).
-- Optional — Program.cs legt die Spalte beim Start idempotent an.

ALTER TABLE behoerde
    ADD COLUMN IF NOT EXISTS handy VARCHAR(30);
