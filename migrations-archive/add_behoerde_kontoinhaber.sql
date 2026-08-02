-- Kontoinhaber an Behörden-IBAN (Walter 02.08.2026)
-- Für pain.001 Cdtr.Nm wenn der Kontoinhaber vom Behörden-Namen abweicht
-- (z.B. ORS SERVICE AG Burgdorf → Kontoinhaber «ORS Service AG Zürich»).
-- Startup in Program.cs legt die Spalte idempotent ebenfalls an.

ALTER TABLE behoerde
    ADD COLUMN IF NOT EXISTS kontoinhaber VARCHAR(200);
