-- Feiertage + Schulferien 2026/2027 für den Manager-Dienstplan (Walter 09.08.2026).
-- Quellen: Dienststelle Volksschulbildung LU (Ferienplan SJ25/26–30/31),
-- Schule Oftringen / Regionalschule Lenzburg (AG-Ferienpläne),
-- Volksschule Langenthal (BE). Ferienbänder inkl. angrenzender Wochenenden.
-- Wiederholbar (NOT-EXISTS-Guards). Zuordnung: AG-Set → alle AG-Filialen
-- (Oftringen, Lenzburg, Hendschiken, Reinach), LU-Set → Sursee,
-- BE-Set → Langenthal (beide Filialen).

-- ── Feiertage: in AG+LU+BE identische → NATIONAL ────────────────────────
INSERT INTO dienstplan_feiertag (datum, bezeichnung, scope, kanton_code)
SELECT v.datum::date, v.bez, 'NATIONAL', NULL
FROM (VALUES
    ('2026-01-01','Neujahr'), ('2026-01-02','Berchtoldstag'),
    ('2026-04-03','Karfreitag'), ('2026-04-06','Ostermontag'),
    ('2026-05-14','Auffahrt'), ('2026-05-25','Pfingstmontag'),
    ('2026-08-01','Bundesfeier'), ('2026-12-25','Weihnachten'), ('2026-12-26','Stephanstag'),
    ('2027-01-01','Neujahr'), ('2027-01-02','Berchtoldstag'),
    ('2027-03-26','Karfreitag'), ('2027-03-29','Ostermontag'),
    ('2027-05-06','Auffahrt'), ('2027-05-17','Pfingstmontag'),
    ('2027-08-01','Bundesfeier'), ('2027-12-25','Weihnachten'), ('2027-12-26','Stephanstag')
) AS v(datum, bez)
WHERE NOT EXISTS (SELECT 1 FROM dienstplan_feiertag f
                  WHERE f.datum = v.datum::date AND f.bezeichnung = v.bez AND f.scope = 'NATIONAL');

-- ── Zusätzliche LU-Feiertage (nur Sursee) ───────────────────────────────
INSERT INTO dienstplan_feiertag (datum, bezeichnung, scope, kanton_code)
SELECT v.datum::date, v.bez, 'KANTON', 'LU'
FROM (VALUES
    ('2026-06-04','Fronleichnam'), ('2026-08-15','Mariä Himmelfahrt'),
    ('2026-11-01','Allerheiligen'), ('2026-12-08','Mariä Empfängnis'),
    ('2027-05-27','Fronleichnam'), ('2027-08-15','Mariä Himmelfahrt'),
    ('2027-11-01','Allerheiligen'), ('2027-12-08','Mariä Empfängnis')
) AS v(datum, bez)
WHERE NOT EXISTS (SELECT 1 FROM dienstplan_feiertag f
                  WHERE f.datum = v.datum::date AND f.bezeichnung = v.bez AND f.scope = 'KANTON' AND f.kanton_code = 'LU');

-- ── Schulferien AG-Filialen (Oftringen, Lenzburg, Hendschiken, Reinach) ─
INSERT INTO branch_schulferien (company_profile_id, bezeichnung, von, bis)
SELECT cp.id, v.bez, v.von::date, v.bis::date
FROM company_profile cp
JOIN (VALUES
    ('Sportferien',      '2026-01-24','2026-02-08'),
    ('Frühlingsferien',  '2026-04-04','2026-04-19'),
    ('Sommerferien',     '2026-07-04','2026-08-09'),
    ('Herbstferien',     '2026-09-26','2026-10-11'),
    ('Weihnachtsferien', '2026-12-19','2027-01-03'),
    ('Sportferien',      '2027-01-30','2027-02-14'),
    ('Frühlingsferien',  '2027-04-10','2027-04-25'),
    ('Sommerferien',     '2027-07-03','2027-08-08'),
    ('Herbstferien',     '2027-10-02','2027-10-17'),
    ('Weihnachtsferien', '2027-12-24','2028-01-09')
) AS v(bez, von, bis) ON TRUE
WHERE (cp.kanton_code = 'AG'
       OR cp.city ILIKE 'Oftringen%' OR cp.city ILIKE 'Lenzburg%'
       OR cp.city ILIKE 'Hendschiken%' OR cp.city ILIKE 'Reinach%')
  AND NOT EXISTS (SELECT 1 FROM branch_schulferien b
                  WHERE b.company_profile_id = cp.id AND b.bezeichnung = v.bez AND b.von = v.von::date);

-- ── Schulferien Sursee (LU, kantonaler Ferienplan) ──────────────────────
INSERT INTO branch_schulferien (company_profile_id, bezeichnung, von, bis)
SELECT cp.id, v.bez, v.von::date, v.bis::date
FROM company_profile cp
JOIN (VALUES
    ('Fasnachtsferien',  '2026-02-07','2026-02-22'),
    ('Osterferien',      '2026-04-03','2026-04-19'),
    ('Sommerferien',     '2026-07-04','2026-08-16'),
    ('Herbstferien',     '2026-09-26','2026-10-11'),
    ('Weihnachtsferien', '2026-12-19','2027-01-03'),
    ('Fasnachtsferien',  '2027-01-30','2027-02-14'),
    ('Osterferien',      '2027-03-26','2027-04-11'),
    ('Sommerferien',     '2027-07-03','2027-08-15'),
    ('Herbstferien',     '2027-09-25','2027-10-10'),
    ('Weihnachtsferien', '2027-12-18','2028-01-02')
) AS v(bez, von, bis) ON TRUE
WHERE (cp.kanton_code = 'LU' OR cp.city ILIKE 'Sursee%')
  AND NOT EXISTS (SELECT 1 FROM branch_schulferien b
                  WHERE b.company_profile_id = cp.id AND b.bezeichnung = v.bez AND b.von = v.von::date);

-- ── Schulferien Langenthal (BE, Volksschule Langenthal — beide Filialen) ─
INSERT INTO branch_schulferien (company_profile_id, bezeichnung, von, bis)
SELECT cp.id, v.bez, v.von::date, v.bis::date
FROM company_profile cp
JOIN (VALUES
    ('Sportwoche',       '2026-01-24','2026-02-01'),
    ('Frühlingsferien',  '2026-04-03','2026-04-19'),
    ('Sommerferien',     '2026-07-04','2026-08-09'),
    ('Herbstferien',     '2026-09-19','2026-10-11'),
    ('Weihnachtsferien', '2026-12-24','2027-01-10'),
    ('Sportwoche',       '2027-01-30','2027-02-07'),
    ('Frühlingsferien',  '2027-04-10','2027-04-25'),
    ('Sommerferien',     '2027-07-03','2027-08-15'),
    ('Herbstferien',     '2027-09-25','2027-10-17'),
    ('Weihnachtsferien', '2027-12-24','2028-01-02')
) AS v(bez, von, bis) ON TRUE
WHERE (cp.kanton_code = 'BE' OR cp.city ILIKE '%Langenthal%')
  AND NOT EXISTS (SELECT 1 FROM branch_schulferien b
                  WHERE b.company_profile_id = cp.id AND b.bezeichnung = v.bez AND b.von = v.von::date);
