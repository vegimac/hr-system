-- ============================================================================
-- Reparatur Datums-Verschiebung (Bug 09.–10.09.2026, OneCrew)
-- Rekonstruiert aus audit_log den WAHREN Wert jeder date-Spalte von
-- Employee / Employment / EmployeeFamilyMember, die seit dem Deploy am
-- 09.09.2026 08:00 geschrieben wurde.
--
-- Regel pro Feld (chronologisch über alle Audit-Einträge):
--   • Startwert = erster «old»-Wert (Stand vor dem Bug).
--   • Jede Speicherung hat einen «gemeinten» Wert = new (UTC) → Europe/Zurich → Datum.
--     Ist der gemeinte Wert ≠ dem vorher gespeicherten (old), war es eine echte
--     Änderung (Benutzer oder easy@work) → wahrer Wert = gemeinter Wert.
--     Ist er gleich, war es nur ein Re-Save, der um einen Tag verschoben hat → ignorieren.
--   • Am Ende: wahrer Wert ≠ aktueller DB-Wert → korrigieren.
--
-- Vorschau (ändert nichts):  sudo -u postgres psql -d hrsystem -v apply=0 -f /tmp/repair_datum_shift.sql
-- Scharf:                    sudo -u postgres psql -d hrsystem -v apply=1 -f /tmp/repair_datum_shift.sql
-- ============================================================================
\set ON_ERROR_STOP on
SELECT set_config('onecrew.apply', :'apply', false);

DO $$
DECLARE
    v_apply    boolean := current_setting('onecrew.apply', true) = '1';
    r          RECORD;
    k          text;
    v          jsonb;
    v_tbl      text;
    v_col      text;
    v_stored   date;
    v_intended date;
    v_cur      date;
    n_fix      int := 0;
    n_ok       int := 0;
BEGIN
    CREATE TEMP TABLE _truth (
        tbl text, id int, col text, truth date, first_old date,
        PRIMARY KEY (tbl, id, col)
    ) ON COMMIT DROP;

    FOR r IN
        SELECT created_at, entity_type, entity_id, action, changes_json::jsonb AS ch
        FROM audit_log
        WHERE created_at >= timestamp '2026-09-09 08:00'
          AND entity_type IN ('Employee','Employment','EmployeeFamilyMember')
          AND action IN ('UPDATE','CREATE')
          AND changes_json IS NOT NULL AND changes_json <> ''
        ORDER BY created_at, id
    LOOP
        v_tbl := CASE r.entity_type WHEN 'Employee' THEN 'employee' WHEN 'Employment' THEN 'employment' ELSE 'employee_family_member' END;
        IF r.entity_id !~ '^[0-9]+$' THEN CONTINUE; END IF;   -- CREATE mit temporärer Id: nicht zuordenbar

        FOR k, v IN SELECT * FROM jsonb_each(r.ch) LOOP
            v_col := lower(regexp_replace(k, '([a-z0-9])([A-Z])', '\1_\2', 'g'));
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = 'public' AND table_name = v_tbl AND column_name = v_col AND data_type = 'date') THEN
                CONTINUE;
            END IF;

            IF r.action = 'UPDATE' THEN
                IF jsonb_typeof(v) <> 'object' OR NOT (v ? 'new') THEN CONTINUE; END IF;
                IF v->>'old' IS NULL THEN
                    v_stored := NULL;
                ELSIF (v->>'old') ~ 'Z$' THEN
                    v_stored := ((v->>'old')::timestamptz AT TIME ZONE 'UTC')::date;
                ELSE
                    v_stored := left(v->>'old', 10)::date;
                END IF;
                IF v->>'new' IS NULL THEN
                    v_intended := NULL;
                ELSIF (v->>'new') ~ '(Z|[+-][0-9]{2}:[0-9]{2})$' THEN
                    v_intended := ((v->>'new')::timestamptz AT TIME ZONE 'Europe/Zurich')::date;
                ELSE
                    v_intended := left(v->>'new', 10)::date;
                END IF;
            ELSE
                v_stored := NULL;
                IF v IS NULL OR jsonb_typeof(v) = 'null' THEN v_intended := NULL;
                ELSIF (v #>> '{}') ~ '(Z|[+-][0-9]{2}:[0-9]{2})$' THEN v_intended := ((v #>> '{}')::timestamptz AT TIME ZONE 'Europe/Zurich')::date;
                ELSE v_intended := left(v #>> '{}', 10)::date; END IF;
            END IF;

            INSERT INTO _truth (tbl, id, col, truth, first_old)
            VALUES (v_tbl, r.entity_id::int, v_col, v_stored, v_stored)
            ON CONFLICT (tbl, id, col) DO NOTHING;

            IF v_intended IS DISTINCT FROM v_stored THEN
                UPDATE _truth t SET truth = v_intended WHERE t.tbl = v_tbl AND t.id = r.entity_id::int AND t.col = v_col;
            END IF;
        END LOOP;
    END LOOP;

    RAISE NOTICE '=== Wahrer Wert vs. aktueller DB-Wert (apply=%) ===', v_apply;
    FOR r IN SELECT * FROM _truth ORDER BY tbl, id, col LOOP
        EXECUTE format('SELECT %I FROM %I WHERE id = $1', r.col, r.tbl) INTO v_cur USING r.id;
        IF v_cur IS DISTINCT FROM r.truth THEN
            n_fix := n_fix + 1;
            RAISE NOTICE 'FIX  %.% id=%: % -> %', r.tbl, r.col, r.id, v_cur, r.truth;
            IF v_apply THEN
                EXECUTE format('UPDATE %I SET %I = $1 WHERE id = $2', r.tbl, r.col) USING r.truth, r.id;
            END IF;
        ELSE
            n_ok := n_ok + 1;
        END IF;
    END LOOP;
    RAISE NOTICE '=== % Felder bereits korrekt, % Felder %', n_ok, n_fix,
        CASE WHEN v_apply THEN 'KORRIGIERT' ELSE 'zu korrigieren (Vorschau, nichts geaendert)' END;
END $$;
