-- Öffentlicher Vertrags-Link-Token (Walter 07.07.2026)
-- HR erzeugt im MA-Detail einen Token-Link, über den der MA sein
-- Arbeitsvertrag-PDF OHNE Login öffnen kann. Klartext-Token nur im Link,
-- in der DB ausschliesslich der SHA-256-Hash. Gültigkeit 14 Tage.
--
-- In TablePlus direkt ausführen. Program.cs legt dieselbe Tabelle beim
-- Start idempotent an (CREATE TABLE IF NOT EXISTS) — diese Datei ist die
-- manuelle Referenz.

CREATE TABLE IF NOT EXISTS contract_share_token (
    id            serial PRIMARY KEY,
    employee_id   integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    employment_id integer NOT NULL,
    token_hash    text NOT NULL,
    expires_at    timestamp without time zone NOT NULL,
    used_at       timestamp without time zone,
    created_at    timestamp without time zone NOT NULL DEFAULT now(),
    created_by    integer
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_contract_share_token_hash ON contract_share_token (token_hash);
CREATE INDEX IF NOT EXISTS ix_contract_share_token_employee ON contract_share_token (employee_id);
