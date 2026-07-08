-- Vertrags-Link-Härtung + SMS-Protokoll (Walter 07.07.2026)
--
-- In TablePlus direkt ausführen. Program.cs legt dieselben Strukturen beim
-- Start idempotent an — diese Datei ist die manuelle Referenz.

-- Vertrags-Link: Öffnungs-Log (Landing-Page) + manueller Widerruf.
ALTER TABLE contract_share_token ADD COLUMN IF NOT EXISTS opened_at  timestamp without time zone;
ALTER TABLE contract_share_token ADD COLUMN IF NOT EXISTS revoked_at timestamp without time zone;

-- SMS-Versand-Protokoll (Stufe 1: gesendet/fehlgeschlagen; Zustell-Status via
-- message_id ist Stufe 2, offen).
CREATE TABLE IF NOT EXISTS sms_log (
    id            serial PRIMARY KEY,
    created_at    timestamp without time zone NOT NULL DEFAULT now(),
    purpose       text,
    employee_id   integer,
    to_phone      text,
    redirected_to text,
    ok            boolean NOT NULL DEFAULT false,
    message_id    text,
    error         text
);
CREATE INDEX IF NOT EXISTS ix_sms_log_employee_purpose ON sms_log (employee_id, purpose);
