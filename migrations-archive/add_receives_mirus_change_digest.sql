-- add_receives_mirus_change_digest.sql
-- ---------------------------------------------------------------------------
-- Walter 23.07.2026: Flag am Benutzer für den täglichen Mirus-Änderungsdigest
-- (06:00 Europe/Zurich). Idempotent. In TablePlus ausführen.
-- ---------------------------------------------------------------------------

ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS receives_mirus_change_digest BOOLEAN NOT NULL DEFAULT false;

COMMENT ON COLUMN app_user.receives_mirus_change_digest IS
    'Empfänger des täglichen OneCrew→Mirus Änderungsdigests (lohnkritische Änderungen der letzten 24h).';
