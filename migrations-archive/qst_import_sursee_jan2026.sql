-- ─────────────────────────────────────────────────────────────────────────
-- QST-Import Sursee Januar 2026 — aus Mirus-Abrechnung übernommen
-- ─────────────────────────────────────────────────────────────────────────
-- Quelle: 11884367151134.pdf.csv (Abrechnung über die Quellensteuern,
--         Filiale Sursee, Periode 01.01.2026 – 31.01.2026)
--
-- Match: über (first_name, last_name) — Case-insensitive. Wenn nicht gefunden,
-- wird auch die VERTAUSCHTE Variante probiert (für MA wo Vor-/Nachname im
-- System anders herum gespeichert sind, was bei südasiatischen Namen häufig
-- vorkommt).
--
-- Pro MA werden zwei Dinge gemacht:
--   1) employee.social_security_number wird mit der AHV-Nummer gefüllt
--      (überschreibt nicht falls bereits gesetzt — nur wenn NULL/leer).
--   2) employee_quellensteuer wird upserted: alter offener Eintrag
--      bekommt valid_to = 31.12.2025, neuer Eintrag startet 01.01.2026.
-- ─────────────────────────────────────────────────────────────────────────

DO $$
DECLARE
    v_emp_id INTEGER;
    v_data RECORD;
    v_qst_code TEXT;
BEGIN
    FOR v_data IN
        SELECT * FROM (VALUES
            -- (vorname, nachname, ahv, gemeinde, tarif, kinder, kirchensteuer)
            ('Sara',         'Mundruc',       '756.5642.9226.51', 'Büron',     'A', 0, FALSE),
            ('Lile',         'Dimov',         '756.8274.6794.12', 'Emmen',     'C', 0, FALSE),
            ('Marija',       'Koceva',        '756.6740.7646.55', 'Emmen',     'C', 0, FALSE),
            ('Francesca',    'Vivianis',      '756.4379.4191.75', 'Emmen',     'A', 0, FALSE),
            ('Leonita',      'Hadergjonaj',   '756.6640.2056.47', 'Geuensee',  'C', 0, FALSE),
            ('Martin',       'Mitev',         '756.6418.9250.67', 'Hochdorf',  'A', 0, FALSE),
            ('Dragana',      'Dimitrova',     '756.7856.8916.62', 'Nebikon',   'C', 1, FALSE),
            ('Novel',        'Amanuel',       '756.6092.3262.42', 'Oberkirch', 'A', 0, TRUE),
            ('Uresa',        'Krasniqi',      '756.3655.2574.70', 'Oberkirch', 'C', 3, FALSE),
            ('Josif',        'Cvetanov',      '756.4635.0272.81', 'Reiden',    'C', 0, FALSE),
            ('Alban',        'Salioski',      '756.2635.4692.47', 'Reiden',    'A', 0, FALSE),
            ('Aleksandra',   'Stojkovska',    '756.1229.6938.39', 'Reiden',    'C', 1, FALSE),
            ('Tibosika',     'Ananthakumar',  '756.6050.6212.80', 'Sursee',    'B', 0, TRUE),
            ('Aylin',        'Hamzic',        '756.6373.4338.05', 'Sursee',    'A', 0, FALSE),
            ('Agneza',       'Laci',          '756.6434.9575.92', 'Sursee',    'H', 1, TRUE),
            ('Oxana',        'Mocate',        '756.4471.9787.61', 'Sursee',    'C', 2, FALSE),
            ('Atibe',        'Ponik',         '756.5647.5593.88', 'Sursee',    'A', 0, FALSE),
            ('Azbije',       'Ponik',         '756.0665.7876.90', 'Sursee',    'C', 4, FALSE),
            ('Aneta',        'Tanevska',      '756.3675.8294.04', 'Sursee',    'C', 2, FALSE),
            ('Andreja',      'Angjelkoska',   '756.9141.0248.23', 'Triengen',  'C', 2, FALSE),
            ('Angela',       'Atanasovski',   '756.2948.9100.30', 'Triengen',  'C', 2, FALSE),
            ('Natasha',      'Hulaj',         '756.2420.6626.62', 'Triengen',  'C', 2, FALSE),
            ('Gylgjan',      'Korllak',       '756.6789.0717.90', 'Triengen',  'C', 2, FALSE),
            ('Luka',         'Papic',         '756.4171.1813.24', 'Triengen',  'A', 0, TRUE)
        ) AS data(vorname, nachname, ahv, gemeinde, tarif, kinder, kirchensteuer)
    LOOP
        -- 1) MA finden — exakter Match Vor-/Nachname (case-insensitive).
        SELECT id INTO v_emp_id FROM employee
        WHERE LOWER(first_name) = LOWER(v_data.vorname)
          AND LOWER(last_name)  = LOWER(v_data.nachname)
        LIMIT 1;

        -- 2) Falls nicht gefunden: vertauschte Variante (Vor- und Nachname swap)
        IF v_emp_id IS NULL THEN
            SELECT id INTO v_emp_id FROM employee
            WHERE LOWER(first_name) = LOWER(v_data.nachname)
              AND LOWER(last_name)  = LOWER(v_data.vorname)
            LIMIT 1;
            IF v_emp_id IS NOT NULL THEN
                RAISE NOTICE 'Vor-/Nachname vertauscht im System für %, %', v_data.vorname, v_data.nachname;
            END IF;
        END IF;

        IF v_emp_id IS NULL THEN
            RAISE NOTICE 'MA nicht gefunden: % %', v_data.vorname, v_data.nachname;
            CONTINUE;
        END IF;

        -- 3) AHV-Nummer setzen — nur wenn noch leer, um manuelle Eingaben nicht zu überschreiben
        UPDATE employee
        SET social_security_number = v_data.ahv
        WHERE id = v_emp_id
          AND (social_security_number IS NULL OR social_security_number = '');

        -- 4) Bestehenden offenen QST-Eintrag schliessen
        UPDATE employee_quellensteuer
        SET valid_to = '2025-12-31'::date
        WHERE employee_id = v_emp_id
          AND valid_to IS NULL
          AND valid_from < '2026-01-01'::date;

        v_qst_code := v_data.tarif || v_data.kinder::text || (CASE WHEN v_data.kirchensteuer THEN 'Y' ELSE 'N' END);

        -- 5) Falls bereits ein Eintrag mit valid_from = 2026-01-01 existiert: UPDATE
        IF EXISTS (SELECT 1 FROM employee_quellensteuer
                   WHERE employee_id = v_emp_id
                     AND valid_from = '2026-01-01'::date) THEN
            UPDATE employee_quellensteuer
            SET steuerkanton       = 'LU',
                steuerkanton_name  = 'Luzern',
                qst_gemeinde       = v_data.gemeinde,
                tarif_code         = v_data.tarif,
                anzahl_kinder      = v_data.kinder,
                kirchensteuer      = v_data.kirchensteuer,
                qst_code           = v_qst_code,
                tarifvorschlag_qst = TRUE
            WHERE employee_id = v_emp_id
              AND valid_from = '2026-01-01'::date;
        ELSE
            INSERT INTO employee_quellensteuer (
                employee_id, valid_from, valid_to,
                steuerkanton, steuerkanton_name, qst_gemeinde,
                tarif_code, anzahl_kinder, kirchensteuer, qst_code,
                tarifvorschlag_qst
            ) VALUES (
                v_emp_id, '2026-01-01'::date, NULL,
                'LU', 'Luzern', v_data.gemeinde,
                v_data.tarif, v_data.kinder, v_data.kirchensteuer,
                v_qst_code,
                TRUE
            );
        END IF;
    END LOOP;
END $$;

-- Kontrolle: was wurde importiert
SELECT e.first_name || ' ' || e.last_name AS name,
       e.social_security_number AS ahv,
       q.steuerkanton,
       q.qst_gemeinde,
       q.qst_code,
       q.valid_from
FROM employee_quellensteuer q
JOIN employee e ON e.id = q.employee_id
WHERE q.valid_from = '2026-01-01'::date
ORDER BY e.first_name;
