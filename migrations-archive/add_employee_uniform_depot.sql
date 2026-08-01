-- Uniformen-Depot CHF 50 (Walter Aug 2026)
-- Beim 1. Lohn Abzug → Depot EINBEHALTEN; bei Austritt Rückgabe → Refund;
-- sonst VERFALLEN. Fibu: Lohnart 600.32 → 1920/2021 (bereits im Kontoplan).

CREATE TABLE IF NOT EXISTS employee_uniform_depot (
    id                   serial PRIMARY KEY,
    employee_id          integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    balance              numeric(10,2) NOT NULL DEFAULT 50,
    status               varchar(20) NOT NULL DEFAULT 'EINBEHALTEN',
    charged_periode      varchar(20) NULL,
    refund_periode       varchar(20) NULL,
    return_confirmed     boolean NULL,
    return_confirmed_at  timestamp without time zone NULL,
    return_confirmed_by  integer NULL,
    bemerkung            text NULL,
    created_at           timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_employee_uniform_depot_emp
    ON employee_uniform_depot (employee_id);

-- Lohnart 600.32 Uniformen-Depot (ABZUG, nicht SV-pflichtig)
INSERT INTO lohnposition
    (code, bezeichnung, kategorie, typ,
     ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
     lohnausweis_code, sort_order, is_active,
     nicht_drucken_wenn_null, nicht_im_vertrag_drucken)
SELECT
    '600.32', 'Uniformen-Depot', 'Abzüge', 'ABZUG',
    false, false, false, false, false,
    NULL, 632, true,
    true, true
WHERE NOT EXISTS (SELECT 1 FROM lohnposition lp WHERE lp.code = '600.32');

UPDATE lohnposition
   SET bezeichnung = 'Uniformen-Depot',
       kategorie   = 'Abzüge',
       typ         = 'ABZUG',
       is_active   = true
 WHERE code = '600.32';

-- Fibu-Text angleichen (Mapping 600/32 existiert schon als «Kleiderdepot»)
UPDATE lohn_konto_mapping
   SET bezeichnung = 'Uniformen-Depot'
 WHERE position = 600 AND sub_position = 32
   AND bezeichnung IS DISTINCT FROM 'Uniformen-Depot';

-- Backfill: Eintritt vor 01.07.2026 → Depot 50 ohne Lohn-Abzug
INSERT INTO employee_uniform_depot
    (employee_id, balance, status, charged_periode, bemerkung, created_at, updated_at)
SELECT e.id, 50, 'EINBEHALTEN', 'BACKFILL',
       'Backfill: Eintritt vor 01.07.2026',
       CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
  FROM employee e
 WHERE e.is_hidden IS NOT TRUE
   AND e.is_payroll_excluded IS NOT TRUE
   AND e.entry_date IS NOT NULL
   AND e.entry_date < DATE '2026-07-01'
   AND (e.is_active IS TRUE OR e.exit_date IS NULL OR e.exit_date >= DATE '2026-07-01')
   AND NOT EXISTS (
       SELECT 1 FROM employee_uniform_depot d WHERE d.employee_id = e.id
   );
