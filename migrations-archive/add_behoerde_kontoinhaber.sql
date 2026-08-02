-- Kontoinhaber an Behörden-IBAN (Walter 02.08.2026)
-- 1) Legacy-Freitext (kurz genutzt)
-- 2) FK auf andere Behörde als Kontoinhaber (ORS Burgdorf → ORS Zürich)
--    → DTA Cdtr.Nm + Adresse/PLZ/Ort von der gewählten Behörde.
-- Startup in Program.cs legt beides idempotent ebenfalls an.

ALTER TABLE behoerde
    ADD COLUMN IF NOT EXISTS kontoinhaber VARCHAR(200);

ALTER TABLE behoerde
    ADD COLUMN IF NOT EXISTS kontoinhaber_behoerde_id INTEGER
        REFERENCES behoerde(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_behoerde_kontoinhaber_behoerde
    ON behoerde(kontoinhaber_behoerde_id);
