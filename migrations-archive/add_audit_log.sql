-- Walter-Vorgabe 27.05.2026: zentrales Audit-Log fuer ALLE CRUD-Writes
-- (Voll-Audit, nur Writes). Wird vom AuditSaveChangesInterceptor pro
-- SaveChanges befuellt. Eine Zeile pro geaenderter Entitaet (nicht
-- pro Feld — Felder stehen in changes_json).
--
-- In TablePlus ausfuehren.

CREATE TABLE IF NOT EXISTS audit_log (
    id              bigserial PRIMARY KEY,
    created_at      timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC'),
    user_id         integer NULL,
    user_name       text    NULL,
    user_role       text    NULL,
    entity_type     text    NOT NULL,
    entity_id       text    NULL,
    action          text    NOT NULL CHECK (action IN ('CREATE','UPDATE','DELETE')),
    changes_json    text    NULL,
    route           text    NULL,
    ip_address      text    NULL
);

CREATE INDEX IF NOT EXISTS ix_audit_log_created_at ON audit_log (created_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_log_user_id    ON audit_log (user_id);
CREATE INDEX IF NOT EXISTS ix_audit_log_entity     ON audit_log (entity_type, entity_id);
CREATE INDEX IF NOT EXISTS ix_audit_log_action     ON audit_log (action);
