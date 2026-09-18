-- FamZ: erfahren_am + famz_korrektur (Walter 18.09.2026, SchemaStand 16)
-- Wirkung = valid_from, Wissen = erfahren_am → Nachzahlung/Rückforderung.

ALTER TABLE family_member_allowance ADD COLUMN IF NOT EXISTS erfahren_am date;

CREATE TABLE IF NOT EXISTS famz_korrektur (
    id                      serial PRIMARY KEY,
    employee_id             int NOT NULL,
    company_profile_id      int NOT NULL,
    family_member_id        int NOT NULL,
    allowance_id            int NOT NULL,
    jahr                    int NOT NULL,
    monat                   int NOT NULL,
    alter_betrag            numeric(10,2) NOT NULL DEFAULT 0,
    neuer_betrag            numeric(10,2) NOT NULL DEFAULT 0,
    betrag                  numeric(10,2) NOT NULL DEFAULT 0,
    allowance_type          varchar(20),
    child_name              varchar(200),
    status                  varchar(20) NOT NULL DEFAULT 'OFFEN',
    grund                   text NOT NULL DEFAULT '',
    verrechnet_periode_id   int,
    verrechnet_at           timestamp without time zone,
    created_at              timestamp without time zone NOT NULL DEFAULT NOW(),
    created_by              varchar(150)
);
CREATE INDEX IF NOT EXISTS ix_famz_korrektur_emp ON famz_korrektur (employee_id, jahr, monat);
CREATE INDEX IF NOT EXISTS ix_famz_korrektur_allowance ON famz_korrektur (allowance_id);
