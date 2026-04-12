-- ============================================================
-- Zulagen & Abzuge: Stammdaten und Erfassungs-Tabellen
-- ============================================================

-- Tabelle 1: Typen (Stammdaten, einmalig konfiguriert)
CREATE TABLE IF NOT EXISTS lohn_zulag_typ (
    id            SERIAL PRIMARY KEY,
    bezeichnung   VARCHAR(100)  NOT NULL,
    typ           VARCHAR(10)   NOT NULL DEFAULT 'ZULAGE',
    sv_pflichtig  BOOLEAN       NOT NULL DEFAULT FALSE,
    qst_pflichtig BOOLEAN       NOT NULL DEFAULT FALSE,
    sort_order    INT           NOT NULL DEFAULT 99,
    aktiv         BOOLEAN       NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

-- Tabelle 2: Eintraege pro Mitarbeiter & Periode
CREATE TABLE IF NOT EXISTS lohn_zulage (
    id           SERIAL PRIMARY KEY,
    employee_id  INT           NOT NULL REFERENCES employee(id),
    periode      VARCHAR(7)    NOT NULL,
    typ_id       INT           NOT NULL REFERENCES lohn_zulag_typ(id),
    betrag       NUMERIC(10,2) NOT NULL,
    bemerkung    TEXT,
    created_at   TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_lohn_zulage_emp_periode
    ON lohn_zulage (employee_id, periode);

-- Seed: Vordefinierte Typen
INSERT INTO lohn_zulag_typ (bezeichnung, typ, sv_pflichtig, qst_pflichtig, sort_order)
SELECT 'Km-Entschaedigung', 'ZULAGE', FALSE, FALSE, 10
WHERE NOT EXISTS (SELECT 1 FROM lohn_zulag_typ WHERE bezeichnung = 'Km-Entschaedigung');

INSERT INTO lohn_zulag_typ (bezeichnung, typ, sv_pflichtig, qst_pflichtig, sort_order)
SELECT 'Spesen pauschal', 'ZULAGE', FALSE, FALSE, 20
WHERE NOT EXISTS (SELECT 1 FROM lohn_zulag_typ WHERE bezeichnung = 'Spesen pauschal');

INSERT INTO lohn_zulag_typ (bezeichnung, typ, sv_pflichtig, qst_pflichtig, sort_order)
SELECT 'Praemie / Bonus', 'ZULAGE', TRUE, TRUE, 30
WHERE NOT EXISTS (SELECT 1 FROM lohn_zulag_typ WHERE bezeichnung = 'Praemie / Bonus');

INSERT INTO lohn_zulag_typ (bezeichnung, typ, sv_pflichtig, qst_pflichtig, sort_order)
SELECT 'Sonstige Zulage', 'ZULAGE', FALSE, FALSE, 90
WHERE NOT EXISTS (SELECT 1 FROM lohn_zulag_typ WHERE bezeichnung = 'Sonstige Zulage');

INSERT INTO lohn_zulag_typ (bezeichnung, typ, sv_pflichtig, qst_pflichtig, sort_order)
SELECT 'Vorschuss-Rueckzahlung', 'ABZUG', FALSE, FALSE, 10
WHERE NOT EXISTS (SELECT 1 FROM lohn_zulag_typ WHERE bezeichnung = 'Vorschuss-Rueckzahlung');

INSERT INTO lohn_zulag_typ (bezeichnung, typ, sv_pflichtig, qst_pflichtig, sort_order)
SELECT 'Lohnpaendung', 'ABZUG', FALSE, FALSE, 20
WHERE NOT EXISTS (SELECT 1 FROM lohn_zulag_typ WHERE bezeichnung = 'Lohnpaendung');

INSERT INTO lohn_zulag_typ (bezeichnung, typ, sv_pflichtig, qst_pflichtig, sort_order)
SELECT 'Sonstiger Abzug', 'ABZUG', FALSE, FALSE, 90
WHERE NOT EXISTS (SELECT 1 FROM lohn_zulag_typ WHERE bezeichnung = 'Sonstiger Abzug');
