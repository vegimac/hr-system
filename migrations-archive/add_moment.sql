-- Moments: Kurznachricht an MA mit Link zur Landing-Page (Walter 30.06.2026).
-- In TablePlus ausführen (nicht via psql-CLI).

CREATE TABLE IF NOT EXISTS moment (
    id                    serial PRIMARY KEY,
    token                 text    NOT NULL,
    employee_id           integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    typ                   text,
    sms_text              text,
    full_text             text,
    antwortart            text    NOT NULL DEFAULT 'lesen',
    status                text    NOT NULL DEFAULT 'erstellt',
    created_at            timestamp without time zone NOT NULL DEFAULT now(),
    created_by_id         integer,
    opened_at             timestamp without time zone,
    responded_at          timestamp without time zone,
    response_value        text,
    response_text         text,
    response_dokument_id  integer
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_moment_token ON moment (token);
CREATE INDEX IF NOT EXISTS ix_moment_employee   ON moment (employee_id);

-- Erweiterung Dokument-Flow (Walter 30.06.2026) — auch für bereits angelegte Tabelle:
ALTER TABLE moment ADD COLUMN IF NOT EXISTS absender      text;
ALTER TABLE moment ADD COLUMN IF NOT EXISTS dokument_name text;
ALTER TABLE moment ADD COLUMN IF NOT EXISTS verified_at   timestamp without time zone;
ALTER TABLE moment ADD COLUMN IF NOT EXISTS zustellung    text NOT NULL DEFAULT 'postfach';
ALTER TABLE moment ADD COLUMN IF NOT EXISTS mailbox_document_id integer;
