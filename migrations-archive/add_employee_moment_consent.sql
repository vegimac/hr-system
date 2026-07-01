-- OneCrew Moments — Freigabe pro Mitarbeitendem (Walter-Vorgabe 30.06.2026).
-- Eine Zeile pro MA. Standard AUS, bis der MA aktiv zustimmt. Ohne aktive
-- Freigabe darf kein Moment-Link erstellt werden.
-- Audit läuft zusätzlich über das zentrale audit_log (Interceptor).
-- In TablePlus ausführen (nicht via psql-CLI).

CREATE TABLE IF NOT EXISTS employee_moment_consent (
    id                          serial PRIMARY KEY,
    employee_id                 integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    moments_consent_enabled     boolean NOT NULL DEFAULT false,
    allow_birthday_anniversary  boolean NOT NULL DEFAULT false,
    allow_appreciation          boolean NOT NULL DEFAULT false,
    allow_care                  boolean NOT NULL DEFAULT false,
    consent_text_version        text,
    granted_at                  timestamp without time zone,
    revoked_at                  timestamp without time zone,
    last_changed_at             timestamp without time zone NOT NULL DEFAULT now(),
    last_changed_by             text,
    source                      text
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_employee_moment_consent_employee ON employee_moment_consent (employee_id);
