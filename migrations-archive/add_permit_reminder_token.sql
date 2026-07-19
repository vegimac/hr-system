-- Bewilligungs-Erinnerung: Kurz-SMS + öffentlicher Link (Walter 19.07.2026)
-- TablePlus: diesen Block ausführen.

CREATE TABLE IF NOT EXISTS permit_reminder_token (
    id                 serial PRIMARY KEY,
    employee_id        integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    permit_history_id  integer NOT NULL,
    token_hash         text NOT NULL,
    message_html       text NOT NULL,
    title              text,
    expires_at         timestamp without time zone NOT NULL,
    opened_at          timestamp without time zone,
    revoked_at         timestamp without time zone,
    created_at         timestamp without time zone NOT NULL DEFAULT now(),
    created_by         integer
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_permit_reminder_token_hash ON permit_reminder_token (token_hash);
CREATE INDEX IF NOT EXISTS ix_permit_reminder_token_employee ON permit_reminder_token (employee_id);

-- Zu lange SMS-Vorlage kürzen (Kurztext + Link-Seite); Mitteilung in BodyText.
UPDATE moment_text mt
SET sms_text = 'Hallo {Vorname}, deine Bewilligung ist abgelaufen. Tippe auf den Link:',
    body_text = '{Briefanrede}

deine Bewilligung ({PermitCode}) ist am {GueltigBis} abgelaufen. Kannst du bitte die neue Bewilligung so bald wie möglich bei HR nachreichen?

Danke und freundliche Grüsse
{SenderName}',
    titel = COALESCE(NULLIF(mt.titel, ''), 'Bewilligung abgelaufen')
FROM moment_type t
WHERE mt.moment_type_id = t.id
  AND t.code = 'BEWILLIGUNG_ABGELAUFEN'
  AND mt.sms_text IS NOT NULL
  AND length(mt.sms_text) > 160;
