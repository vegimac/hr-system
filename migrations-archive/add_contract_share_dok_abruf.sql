-- Lese-Tracking der Onboarding-Dokumente am Vertrags-Link (Walter 10.08.2026):
-- pro Token + Dokument wird das ERSTE Öffnen festgehalten. Basis für die
-- Auswertung «Onboarding-Dokumente gelesen» (HR-Hub → Kachel ONBOARDING).
-- Läuft auch idempotent beim Server-Start (Program.cs).

CREATE TABLE IF NOT EXISTS contract_share_dok_abruf (
    id           serial PRIMARY KEY,
    token_id     integer NOT NULL REFERENCES contract_share_token(id) ON DELETE CASCADE,
    dok_id       bigint NOT NULL,
    abgerufen_am timestamp without time zone NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cs_dok_abruf ON contract_share_dok_abruf (token_id, dok_id);
