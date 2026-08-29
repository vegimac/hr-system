-- K3 MA-Darlehen / Vorschüsse (Walter 29.08.2026, Konzept Kap. 4 + Bauplan 2.3)
-- Generisches zinsloses Darlehen (QST-Nachzahlung ODER freier Vorschuss,
-- z.B. «Vorschuss Hochzeit 2'000»). Rückzahlung als Abzug nach Netto im
-- Definitivlauf; letzte Rate = Rest; bei Austritt Restsaldo fällig.
-- Läuft auch idempotent beim App-Start (Program.cs); TablePlus-Kopie.

CREATE TABLE IF NOT EXISTS employee_darlehen (
    id                 serial PRIMARY KEY,
    employee_id        integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    company_profile_id integer NOT NULL,
    zweck              varchar(200) NOT NULL DEFAULT '',
    betrag             numeric(10,2) NOT NULL DEFAULT 0,
    auszahlung_datum   date,
    rate_betrag        numeric(10,2) NOT NULL DEFAULT 0,
    start_jahr         integer NOT NULL,
    start_monat        integer NOT NULL,
    status             varchar(20) NOT NULL DEFAULT 'OFFEN',
    bemerkung          text,
    created_at         timestamp without time zone NOT NULL DEFAULT now(),
    created_by         varchar(150)
);

CREATE TABLE IF NOT EXISTS employee_darlehen_rate (
    id            serial PRIMARY KEY,
    darlehen_id   integer NOT NULL REFERENCES employee_darlehen(id) ON DELETE CASCADE,
    employee_id   integer NOT NULL,
    period_year   integer NOT NULL,
    period_month  integer NOT NULL,
    betrag        numeric(10,2) NOT NULL DEFAULT 0,
    saldo_nachher numeric(10,2) NOT NULL DEFAULT 0,
    created_at    timestamp without time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_darlehen_rate_period ON employee_darlehen_rate (employee_id, period_year, period_month);

-- Fibu-Mapping Rate: Soll 2050 / Gegen 1140 (im Kontoplan-UI anpassbar)
INSERT INTO lohn_konto_mapping (position, sub_position, fibukonto, gegenkonto, bezeichnung, is_vormonat)
SELECT 1090, NULL, '2050', '1140', 'Rückzahlung MA-Darlehen/Vorschuss', false
WHERE NOT EXISTS (SELECT 1 FROM lohn_konto_mapping WHERE position = 1090);
