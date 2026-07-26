-- Austritts-Fragebogen: Filiale ohne MA-Bezug (Walter 26.07.2026)
-- TablePlus: diesen Block ausführen.

ALTER TABLE exit_survey_response
ADD COLUMN IF NOT EXISTS company_profile_id integer;
