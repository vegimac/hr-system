-- Letzter Login pro User mitprotokollieren
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS last_login_at TIMESTAMP NULL;

COMMENT ON COLUMN app_user.last_login_at IS
    'UTC-Zeitstempel des letzten erfolgreichen Logins. NULL wenn nie eingeloggt.';
