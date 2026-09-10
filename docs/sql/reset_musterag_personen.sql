-- ============================================================================
-- Swissdec-Testmandant «Muster AG»: Personen-Daten zurücksetzen (NUR Test-DB!)
-- Löscht alle Mitarbeitenden, die einen Vertrag in einer Muster-AG-Filiale
-- haben (Hauptsitz UID CHE-999.999.996) samt ALLEN abhängigen Zeilen
-- (Verträge, Versicherungscodes, Familie, QST, Stempelzeiten, Zulagen,
-- Lohnbelege, Saldi …) sowie die Lohnperioden der Muster-AG-Filialen.
-- Firma/Filialen (Schritt 1), Versicherungen (2/3), Lohnarten (4b) bleiben.
-- Danach: Schritt 4a → 4b → 4c → 5a → 5b (pro Monat) neu ausführen.
--
-- Abhängige Tabellen werden über die Fremdschlüssel automatisch gefunden
-- (rekursiv) — keine handgepflegte Tabellenliste.
--
-- Vorschau: sudo -u postgres psql -d hr_system_test -v apply=0 -f /tmp/reset_musterag_personen.sql
-- Scharf:   sudo -u postgres psql -d hr_system_test -v apply=1 -f /tmp/reset_musterag_personen.sql
-- ============================================================================
\set ON_ERROR_STOP on
SELECT set_config('onecrew.apply', :'apply', false);

-- Hilfsfunktion (nur für diese Sitzung, pg_temp)
CREATE OR REPLACE FUNCTION pg_temp._onecrew_del_rek(p_tbl text, p_ids bigint[], p_pfad text[], p_apply boolean)
RETURNS void LANGUAGE plpgsql AS $f$
DECLARE
    fk  RECORD;
    kid bigint[];
    n   bigint;
BEGIN
    IF p_ids IS NULL OR cardinality(p_ids) = 0 THEN RETURN; END IF;
    FOR fk IN
        SELECT c.conrelid::regclass::text AS child, a.attname AS col
        FROM pg_constraint c
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
        WHERE c.contype = 'f' AND c.confrelid = ('public.' || quote_ident(p_tbl))::regclass
          AND array_length(c.conkey, 1) = 1
        ORDER BY 1, 2
    LOOP
        -- Zyklen vermeiden (z.B. employee ↔ employment): Tabelle im Pfad → nicht erneut absteigen
        IF fk.child = ANY (p_pfad) THEN CONTINUE; END IF;
        EXECUTE format('SELECT count(*) FROM %s WHERE %I = ANY($1)', fk.child, fk.col) INTO n USING p_ids;
        IF n = 0 THEN CONTINUE; END IF;
        -- Stammdaten-Tabellen NIE löschen (z.B. Filiale zeigt auf einen MA) → Verweis auf NULL
        IF fk.child IN ('company_profile', 'hauptsitz', 'app_user', 'users', 'user_account', 'lohn_zulag_typ', 'social_insurance_rate') THEN
            INSERT INTO _plan (tbl, col, n) VALUES (fk.child, fk.col || ' (auf NULL gesetzt)', n);
            IF p_apply THEN EXECUTE format('UPDATE %s SET %I = NULL WHERE %I = ANY($1)', fk.child, fk.col, fk.col) USING p_ids; END IF;
            CONTINUE;
        END IF;
        -- Kinder mit eigener id-Spalte weiter absteigen
        IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = fk.child::regclass AND attname = 'id' AND NOT attisdropped) THEN
            EXECUTE format('SELECT array_agg(id::bigint) FROM %s WHERE %I = ANY($1)', fk.child, fk.col) INTO kid USING p_ids;
            PERFORM pg_temp._onecrew_del_rek(fk.child, kid, p_pfad || fk.child, p_apply);
        END IF;
        INSERT INTO _plan (tbl, col, n) VALUES (fk.child, fk.col, n);
        IF p_apply THEN
            EXECUTE format('DELETE FROM %s WHERE %I = ANY($1)', fk.child, fk.col) USING p_ids;
        END IF;
    END LOOP;
END $f$;

DO $$
DECLARE
    v_apply boolean := current_setting('onecrew.apply', true) = '1';
    v_db    text := current_database();
    n_emp   int;
    n_cp    int;
BEGIN
    IF v_db = 'hrsystem' THEN
        RAISE EXCEPTION 'STOPP: Dieses Skript darf nur auf der Test-Datenbank laufen (aktuell: %).', v_db;
    END IF;

    -- Muster-AG-Filialen und -Personen
    CREATE TEMP TABLE _cp ON COMMIT DROP AS
        SELECT cp.id FROM company_profile cp JOIN hauptsitz h ON h.id = cp.hauptsitz_id WHERE h.uid = 'CHE-999.999.996';
    CREATE TEMP TABLE _emp ON COMMIT DROP AS
        SELECT DISTINCT em.employee_id AS id FROM employment em WHERE em.company_profile_id IN (SELECT id FROM _cp);
    SELECT count(*) INTO n_cp FROM _cp;  SELECT count(*) INTO n_emp FROM _emp;
    RAISE NOTICE '=== Muster AG: % Filiale(n), % Person(en) (apply=%) ===', n_cp, n_emp, v_apply;
    IF n_emp = 0 THEN RAISE NOTICE 'Nichts zu tun.'; RETURN; END IF;

    -- Protokoll der zu löschenden Zeilen je Tabelle
    CREATE TEMP TABLE _plan (ord serial, tbl text, col text, n bigint) ON COMMIT DROP;

    -- Rekursiv: alle Zeilen, die (direkt oder indirekt) auf die Personen zeigen.
    -- Wurzeln: employee(id) ∈ _emp  und  payroll_periode(company_profile_id) ∈ _cp.
    CREATE TEMP TABLE _root (tbl text, ids bigint[]) ON COMMIT DROP;
    INSERT INTO _root VALUES ('employee', (SELECT array_agg(id) FROM _emp));
    IF to_regclass('public.payroll_periode') IS NOT NULL THEN
        INSERT INTO _root
        SELECT 'payroll_periode', array_agg(id) FROM payroll_periode WHERE company_profile_id IN (SELECT id FROM _cp) HAVING count(*) > 0;
    END IF;

    PERFORM pg_temp._onecrew_del_rek(tbl, ids, ARRAY[tbl], v_apply) FROM _root;

    -- Wurzeln selbst
    INSERT INTO _plan (tbl, col, n) SELECT 'payroll_periode', '(Filiale)', count(*) FROM payroll_periode WHERE company_profile_id IN (SELECT id FROM _cp);
    INSERT INTO _plan (tbl, col, n) VALUES ('employee', '(Person)', n_emp);
    IF v_apply THEN
        DELETE FROM payroll_periode WHERE company_profile_id IN (SELECT id FROM _cp);
        DELETE FROM employee WHERE id IN (SELECT id FROM _emp);
    END IF;

    RAISE NOTICE '--- Zeilen je Tabelle (%): ---', CASE WHEN v_apply THEN 'GELOESCHT' ELSE 'Vorschau' END;
    FOR n_cp IN SELECT ord FROM _plan ORDER BY ord LOOP
        RAISE NOTICE '%', (SELECT format('%-45s %8s', tbl || '.' || col, n) FROM _plan WHERE ord = n_cp);
    END LOOP;
    RAISE NOTICE '=== fertig (apply=%) ===', v_apply;
END $$;

