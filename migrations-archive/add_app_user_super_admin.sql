-- add_app_user_super_admin.sql
-- ---------------------------------------------------------------------------
-- Super-Admin-Schutz (Walter-Vorgabe 15.05.2026).
--
-- Ein Super-Admin-Account:
--   • Kann NIEMALS gelöscht werden (auch nicht von einem anderen Super-Admin
--     oder vom User selbst).
--   • Nur Super-Admins dürfen Administrator-Accounts löschen — normale
--     Administratoren können sich nicht gegenseitig entfernen.
--   • Profil-Änderungen (Email, Passwort, Role, IsActive) am Super-Admin-
--     Datensatz sind nur durch einen anderen Super-Admin möglich.
--
-- Das Flag wird AUSSCHLIESSLICH per SQL gesetzt — kein API-Pfad ändert es.
--
-- In TablePlus ausführen. Idempotent.
-- ---------------------------------------------------------------------------

ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS is_super_admin BOOLEAN NOT NULL DEFAULT false;

-- Walter Schaub als Super-Admin markieren (idempotent — UPDATE wirkt nur wenn
-- noch nicht gesetzt).
UPDATE app_user
   SET is_super_admin = true
 WHERE LOWER(email) = 'walter.schaub@gmail.com'
   AND is_super_admin = false;

-- Kontrolle
SELECT id, email, role, is_super_admin
FROM   app_user
WHERE  is_super_admin = true OR role = 'admin'
ORDER  BY is_super_admin DESC, role, email;
