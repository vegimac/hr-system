-- OneCrew Moments: zwei getrennte Kommunikationswege (Walter 30.06.2026).
-- 1) Postfach (MailboxDocument) bleibt für administrative/sensible HR-Themen.
-- 2) MomentPage = persönliche, freiwillige Mini-Mitteilung über Einmal-Token-Link
--    (kein Login), nur für MA mit aktivem Opt-in.
-- In TablePlus ausführen (nicht via psql-CLI).

-- ── Opt-in-Schalter pro MA ──────────────────────────────────────────────
ALTER TABLE employee ADD COLUMN IF NOT EXISTS moments_allowed              boolean NOT NULL DEFAULT false;
ALTER TABLE employee ADD COLUMN IF NOT EXISTS moments_allow_wertschaetzung boolean NOT NULL DEFAULT true;
ALTER TABLE employee ADD COLUMN IF NOT EXISTS moments_allow_geburtstag     boolean NOT NULL DEFAULT true;
ALTER TABLE employee ADD COLUMN IF NOT EXISTS moments_allow_freiwillig     boolean NOT NULL DEFAULT true;
ALTER TABLE employee ADD COLUMN IF NOT EXISTS moments_allow_sms            boolean NOT NULL DEFAULT true;

-- ── MomentPage (Token-Link-Moments) ─────────────────────────────────────
CREATE TABLE IF NOT EXISTS moment_page (
    id            serial PRIMARY KEY,
    employee_id   integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    sender_id     integer,
    moment_type   text    NOT NULL DEFAULT '',
    title         text,
    message_html  text,
    token_hash    text    NOT NULL,
    expires_at    timestamp without time zone,
    opened_at     timestamp without time zone,
    responded_at  timestamp without time zone,
    response_value text,
    status        text    NOT NULL DEFAULT 'erstellt',
    created_at    timestamp without time zone NOT NULL DEFAULT now(),
    sms_text      text,
    antwortart    text    NOT NULL DEFAULT 'lesen'
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_moment_page_token_hash ON moment_page (token_hash);
CREATE INDEX IF NOT EXISTS ix_moment_page_employee ON moment_page (employee_id);
