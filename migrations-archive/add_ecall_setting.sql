-- eCall-SMS-Konfiguration (F24 Schweiz, REST-API) — Singleton, Id=1 (Walter 07.07.2026)
-- Auth via HTTP Basic; Passwort AES-verschlüsselt (SimpleAesService, wie SMTP).
--
-- In TablePlus direkt ausführen. Program.cs legt dieselbe Tabelle beim
-- Start idempotent an (CREATE TABLE IF NOT EXISTS) — diese Datei ist die
-- manuelle Referenz.
--
-- Kein Seed-INSERT nötig: PUT /api/ecall/settings legt Row Id=1 beim
-- ersten Speichern an.

CREATE TABLE IF NOT EXISTS ecall_setting (
    id                 integer PRIMARY KEY,
    enabled            boolean NOT NULL DEFAULT false,
    username           text,
    password_encrypted text,
    sender             text,
    test_redirect_to   text,
    updated_at         timestamp without time zone NOT NULL DEFAULT now()
);

-- Nachtrag 07.07.2026: SMS-Test-Umleitung (analog SMTP-Test-Umleitung).
-- Für Installationen, die die Tabelle schon ohne die Spalte haben:
ALTER TABLE ecall_setting ADD COLUMN IF NOT EXISTS test_redirect_to text;
