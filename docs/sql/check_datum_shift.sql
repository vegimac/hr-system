-- Prüfung Datums-Verschiebung (Bug 09./10.09.2026): welche Tabellen mit date-Spalten
-- haben seit dem Deploy vom 09.09.2026 neue/geänderte Zeilen?
-- Nur LESEN, ändert nichts. Aufruf: sudo -u postgres psql -d hrsystem -f check_datum_shift.sql
\echo '=== Tabellen mit date-Spalten und Änderungen seit 09.09.2026 08:00 ==='
DO $$
DECLARE
    r RECORD; n bigint; tscol text;
BEGIN
    FOR r IN
        SELECT DISTINCT c.table_name
        FROM information_schema.columns c
        WHERE c.table_schema = 'public' AND c.data_type = 'date'
        ORDER BY 1
    LOOP
        SELECT column_name INTO tscol
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = r.table_name
          AND column_name IN ('updated_at','created_at','geaendert_am','erstellt_am','modified_at')
        ORDER BY CASE column_name WHEN 'updated_at' THEN 1 WHEN 'geaendert_am' THEN 2 WHEN 'modified_at' THEN 3 ELSE 4 END
        LIMIT 1;
        IF tscol IS NULL THEN
            RAISE NOTICE '%: (kein Zeitstempel — nicht prüfbar)', r.table_name;
            CONTINUE;
        END IF;
        EXECUTE format('SELECT count(*) FROM %I WHERE %I >= timestamp ''2026-09-09 08:00''', r.table_name, tscol) INTO n;
        IF n > 0 THEN
            RAISE NOTICE '%: % Zeile(n) seit Deploy (Spalte %)', r.table_name, n, tscol;
        END IF;
    END LOOP;
END $$;
\echo '=== Ende ==='
