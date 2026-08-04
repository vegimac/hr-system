-- ============================================================
-- payroll_snapshot + payroll_saldo: Zeitstempel → timestamp without time zone
-- Walter 04.08.2026 — Vereinheitlichung auf die System-Regel
-- (Lokalzeit / DateTime.Now + timestamp without time zone, «ACHTUNG TIME»)
--
-- Ursache: die beiden Tabellen waren als timestamptz-Ausreisser (03.08.2026)
-- unterwegs; ConfirmPayroll schreibt DateTime.Now (Kind=Local) u.a. in
-- gf_freigegeben_at/hr_bestaetigt_at → Npgsql 8: «Cannot write DateTime with
-- Kind=Local to timestamp with time zone» → HTTP 500 beim Lohn bestätigen.
--
-- Betroffen:
--   payroll_snapshot: created_at, updated_at, gf_freigegeben_at, hr_bestaetigt_at
--   payroll_saldo:    created_at, updated_at
-- (akonto_zahlung / payroll_periode haben KEINE expliziten timestamptz-Mappings
--  im AppDbContext und bleiben unangetastet.)
--
-- NULL-Werte (gf_freigegeben_at etc.) bleiben mit USING einfach NULL.
-- Läuft auch idempotent beim Server-Start (Program.cs).
-- In TablePlus ausführen falls Deploy noch nicht durch ist.
-- ============================================================

DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND udt_name = 'timestamptz'
          AND (
                (table_name = 'payroll_snapshot'
                 AND column_name IN ('created_at','updated_at','gf_freigegeben_at','hr_bestaetigt_at'))
             OR (table_name = 'payroll_saldo'
                 AND column_name IN ('created_at','updated_at'))
             -- Geburt-eintragen-Crash 04.08.2026: Familienmitglieder
             OR (table_name = 'employee_family_member'
                 AND column_name IN ('created_at','updated_at'))
          )
    LOOP
        EXECUTE format(
            'ALTER TABLE public.%I ALTER COLUMN %I TYPE timestamp without time zone USING (%I AT TIME ZONE %L)',
            r.table_name, r.column_name, r.column_name, 'Europe/Zurich');
    END LOOP;
END $$;
