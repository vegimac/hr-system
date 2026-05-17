-- fix_akonto_zahlung_status_check.sql
-- ---------------------------------------------------------------------------
-- Bug-Fix Walter 16.05.2026 — 4-Augen-Workflow Etappe 2:
--
-- Die Phase-1-Migration (add_akonto_lohn_phase1.sql) hat die CHECK-Constraint
-- `akonto_zahlung_status_check` auf nur ('BERECHNET','AUSBEZAHLT','STORNIERT')
-- gelegt. Phase 2 hat das Status-Enum logisch um 'FREIGEGEBEN_GF' erweitert
-- (zwischen BERECHNET und AUSBEZAHLT), aber die CHECK-Constraint nicht
-- mitgezogen → der GF-Freigabe-Endpoint (POST /api/akonto/workflow/freigeben/{id})
-- wirft beim SaveChangesAsync HTTP 500 (Postgres-CHECK-violation), MA bleibt
-- visuell auf "berechnet" hängen.
--
-- Fix: alte Constraint droppen, neue mit allen vier gültigen Status-Werten
-- anlegen. Reine Schema-Anpassung — keine Daten-Migration nötig (die DB
-- enthält bisher noch keine 'FREIGEGEBEN_GF'-Zeilen, weil der UPDATE ja
-- gerade gescheitert war).
--
-- In TablePlus ausführen. Idempotent.
-- ---------------------------------------------------------------------------

-- 1) Alte Constraint droppen (existiert nur wenn Phase 1 gelaufen ist)
ALTER TABLE akonto_zahlung
    DROP CONSTRAINT IF EXISTS akonto_zahlung_status_check;

-- 2) Neue Constraint mit FREIGEGEBEN_GF
ALTER TABLE akonto_zahlung
    ADD CONSTRAINT akonto_zahlung_status_check
    CHECK (status IN ('BERECHNET', 'FREIGEGEBEN_GF', 'AUSBEZAHLT', 'STORNIERT'));

-- 3) Kontrolle: Constraint-Definition anzeigen
SELECT conname, pg_get_constraintdef(oid) AS def
FROM   pg_constraint
WHERE  conname = 'akonto_zahlung_status_check';
