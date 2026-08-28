-- K1 QST-Korrektur (Walter 29.08.2026, docs/qst-korrektur-konzept.md)
-- Ein Posten pro MA + abgeschlossenem Monat bei rückwirkender QST-Version.
-- Läuft auch idempotent beim App-Start (Program.cs); TablePlus-Kopie.

CREATE TABLE IF NOT EXISTS qst_korrektur (
    id                    serial PRIMARY KEY,
    employee_id           integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    company_profile_id    integer NOT NULL,
    jahr                  integer NOT NULL,
    monat                 integer NOT NULL,
    alte_version_id       integer,
    neue_version_id       integer NOT NULL,
    alter_code            varchar(10),
    neuer_code            varchar(10),
    alter_betrag          numeric(10,2) NOT NULL DEFAULT 0,
    neuer_betrag          numeric(10,2) NOT NULL DEFAULT 0,
    differenz             numeric(10,2) NOT NULL DEFAULT 0,
    basis                 numeric(10,2) NOT NULL DEFAULT 0,
    satz_basis            numeric(10,2) NOT NULL DEFAULT 0,
    status                varchar(20) NOT NULL DEFAULT 'OFFEN',
    grund                 text NOT NULL DEFAULT '',
    verrechnet_periode_id integer,
    verrechnet_at         timestamp without time zone,
    created_at            timestamp without time zone NOT NULL DEFAULT now(),
    created_by            varchar(150)
);
CREATE INDEX IF NOT EXISTS ix_qst_korrektur_emp ON qst_korrektur (employee_id, jahr, monat);
