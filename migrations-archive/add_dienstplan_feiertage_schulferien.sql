-- Manager-Dienstplan: Feiertage + Schulferien (Walter-Vorgabe 09.08.2026).
-- Feiertage dreistufig: NATIONAL (alle Filialen) / KANTON (Filialen mit dem
-- Kanton-Code) / FILIALE (genau eine Filiale — Gemeinde-Feiertage).
-- Schulferien pro Filiale als Datumsband (wie «Sportferien» in der alten Excel).
-- Reine Planungs-Marker im Manager-DP, KEINE Lohn-Wirkung.
-- Läuft auch idempotent beim Server-Start (Program.cs).

CREATE TABLE IF NOT EXISTS dienstplan_feiertag (
    id                 serial PRIMARY KEY,
    datum              date NOT NULL,
    bezeichnung        text NOT NULL,
    scope              text NOT NULL DEFAULT 'NATIONAL'
                       CHECK (scope IN ('NATIONAL','KANTON','FILIALE')),
    kanton_code        text,
    company_profile_id integer REFERENCES company_profile(id) ON DELETE CASCADE,
    created_at         timestamp without time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_dienstplan_feiertag_datum ON dienstplan_feiertag (datum);

CREATE TABLE IF NOT EXISTS branch_schulferien (
    id                 serial PRIMARY KEY,
    company_profile_id integer NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
    bezeichnung        text NOT NULL,
    von                date NOT NULL,
    bis                date NOT NULL,
    created_at         timestamp without time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_branch_schulferien_cp ON branch_schulferien (company_profile_id);
