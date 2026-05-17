-- Singleton-Tabelle für SMTP-Konfiguration (vorher in appsettings.json:Smtp).
-- Es gibt immer nur eine Row mit Id=1. Passwort ist AES-verschlüsselt
-- (Schlüssel = Jwt:Secret aus appsettings.json), damit ein DB-Dump
-- alleine nicht ausreicht um das SMTP-Passwort offenzulegen.
--
-- Beim ersten Aufruf von /api/admin/smtp/GET legt der Controller die Row
-- automatisch mit Default-Werten an (oder seedet aus appsettings.json,
-- falls dort noch was steht — Übergangs-Pattern).

CREATE TABLE IF NOT EXISTS smtp_setting (
    id                   INTEGER PRIMARY KEY,
    host                 VARCHAR(200)  NOT NULL DEFAULT '',
    port                 INTEGER       NOT NULL DEFAULT 587,
    username             VARCHAR(200)  NOT NULL DEFAULT '',
    password_encrypted   TEXT          NOT NULL DEFAULT '',
    from_name            VARCHAR(200)  NOT NULL DEFAULT 'Schaub HR',
    from_address         VARCHAR(200)  NOT NULL DEFAULT '',
    test_redirect_to     VARCHAR(200),
    site_url             VARCHAR(300)  NOT NULL DEFAULT 'https://test.hr-srgmbh.ch/',
    updated_at           TIMESTAMP     NOT NULL DEFAULT NOW(),
    updated_by_user_id   INTEGER,
    CONSTRAINT smtp_setting_singleton CHECK (id = 1)
);
