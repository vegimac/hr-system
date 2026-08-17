-- ELM-Lohnraster-Referenzkatalog (Walter 17.08.2026): 309 Positionen des
-- Lohnrasters als dauerhaftes Archiv («PickList»). Seed der Daten laeuft beim
-- Serverstart aus Assets/Swissdec/ElmLohnraster.json (nur in leere Tabelle).
CREATE TABLE IF NOT EXISTS elm_lohnraster (
    id              SERIAL PRIMARY KEY,
    code            TEXT NOT NULL UNIQUE,
    pos             TEXT NOT NULL,
    sub             TEXT,
    bezeichnung     TEXT NOT NULL,
    gruppe          TEXT,
    typ             TEXT NOT NULL,
    text            TEXT,
    uebersetzung_it TEXT,
    uebersetzung_fr TEXT,
    lohnausweisfeld TEXT,
    statistik_code  TEXT,
    steuerung       TEXT,
    betrag_prozent  TEXT,
    inaktiv         BOOLEAN NOT NULL DEFAULT FALSE,
    ahv             BOOLEAN, qst BOOLEAN, qst_periodisch BOOLEAN,
    bvg             BOOLEAN, uvg BOOLEAN, uvgz BOOLEAN, ktg BOOLEAN, ml13 BOOLEAN,
    attrs_json      TEXT NOT NULL,
    verwendet_lohnposition_id INTEGER REFERENCES lohnposition(id) ON DELETE SET NULL
);
