-- i18n Phase 1: User-Präferenz-Sprache
-- Default 'de', alle bestehenden User starten auf Deutsch.
-- Frontend liest beim Login, persistiert via /api/users/me/language.
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS preferred_language VARCHAR(5) NOT NULL DEFAULT 'de';
