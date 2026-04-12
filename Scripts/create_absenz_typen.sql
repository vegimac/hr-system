-- ============================================================
-- Absenz-Typen: konfigurierbare Zeitgutschrift-Regeln
-- ============================================================

CREATE TABLE IF NOT EXISTS absenz_typ (
    id               SERIAL PRIMARY KEY,
    code             VARCHAR(20)  NOT NULL UNIQUE,
    bezeichnung      VARCHAR(100) NOT NULL,
    zeitgutschrift   BOOLEAN      NOT NULL DEFAULT TRUE,
    gutschrift_modus VARCHAR(5),                           -- '1/5' | '1/7' | NULL
    sort_order       INT          NOT NULL DEFAULT 99,
    aktiv            BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- Seed: aktuelle 7 Typen mit ihren Gutschrift-Regeln
INSERT INTO absenz_typ (code, bezeichnung, zeitgutschrift, gutschrift_modus, sort_order) VALUES
    ('KRANK',      'Krankheit',                    TRUE,  '1/5', 10),
    ('UNFALL',     'Unfall',                       TRUE,  '1/5', 20),
    ('SCHULUNG',   'Schulung / andere Absenz',     TRUE,  '1/5', 30),
    ('FERIEN',     'Ferien',                       TRUE,  '1/7', 40),
    ('MILITAER',   'Militär / Zivilschutz',        TRUE,  '1/5', 50),
    ('FEIERTAG',   'Feiertag (ausbezahlt)',        FALSE, NULL,  60),
    ('NACHT_KOMP', 'Nacht-Kompensation (Ruhetag)', TRUE,  '1/5', 70)
ON CONFLICT (code) DO NOTHING;
