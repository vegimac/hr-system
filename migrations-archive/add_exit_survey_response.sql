-- Austritts-Fragebogen (Walter 26.07.2026) — anonyme Antworten
-- TablePlus: diesen Block ausführen (vor Deploy).

CREATE TABLE IF NOT EXISTS exit_survey_response (
    id                bigserial PRIMARY KEY,
    created_at        timestamp without time zone NOT NULL DEFAULT now(),
    company_profile_id integer,
    reasons_json      text NOT NULL DEFAULT '[]',
    reason_other      text,
    atmosphere_detail text,
    rating            integer,
    comment           text,
    ip_hash           text
);

CREATE INDEX IF NOT EXISTS ix_exit_survey_response_created_at
    ON exit_survey_response (created_at DESC);

-- Nachzieh-Migration falls Tabelle schon ohne Filiale existiert:
ALTER TABLE exit_survey_response
ADD COLUMN IF NOT EXISTS company_profile_id integer;
