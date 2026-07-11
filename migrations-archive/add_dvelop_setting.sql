-- d.velop-API-Konfiguration (Walter 10.07.2026) — Singleton-Tabelle Id=1.
-- (Läuft auch idempotent beim Server-Start in Program.cs — TablePlus-Doku.)
CREATE TABLE IF NOT EXISTS dvelop_setting (
    id                 integer PRIMARY KEY,
    base_url           text,
    api_key_encrypted  text,
    updated_at         timestamp without time zone NOT NULL DEFAULT now()
);
