-- Austritts-Fragebogen Frage 2 «besser werden» (Walter 26.07.2026)
-- TablePlus: diesen Block ausführen.

ALTER TABLE exit_survey_response
    ADD COLUMN IF NOT EXISTS improve_answer text,
    ADD COLUMN IF NOT EXISTS improve_themes_json text NOT NULL DEFAULT '[]';
