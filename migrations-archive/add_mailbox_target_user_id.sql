-- Persönliches Benutzer-Postfach (User→User Mitteilungen)
-- Walter 24.07.2026 — TablePlus: diesen Block ausführen.

ALTER TABLE mailbox_document
    ADD COLUMN IF NOT EXISTS target_user_id integer NULL
        REFERENCES app_user(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS ix_mailbox_document_target_user
    ON mailbox_document (target_type, target_user_id)
    WHERE target_user_id IS NOT NULL;
