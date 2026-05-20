-- Walter-Vorgabe 19.05.2026: 4-Augen-Workflow für Definitivlauf, analog
-- AkontoZahlung. Per-MA-Status auf PayrollSnapshot, damit HR jeden Lohnzettel
-- einzeln bestätigen / zurückziehen / korrigieren kann (statt nur per-Periode).
--
-- Status-Werte:
--   BERECHNET       — Slip berechnet, GF noch nicht freigegeben
--   FREIGEGEBEN_GF  — GF hat "Lohn bestätigen" geklickt
--   HR_BESTAETIGT   — HR hat per-MA bestätigt
--   ABGESCHLOSSEN   — Periode definitiv abgeschlossen (immutable)
--   STORNIERT       — nach Abschluss rückgerollt (mit Audit)

ALTER TABLE payroll_snapshot
    ADD COLUMN IF NOT EXISTS status            VARCHAR(20) NOT NULL DEFAULT 'FREIGEGEBEN_GF',
    ADD COLUMN IF NOT EXISTS gf_freigegeben_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS gf_freigegeben_by INTEGER,
    ADD COLUMN IF NOT EXISTS hr_bestaetigt_at  TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS hr_bestaetigt_by  INTEGER,
    ADD COLUMN IF NOT EXISTS kommentar_gf      TEXT,
    ADD COLUMN IF NOT EXISTS kommentar_hr      TEXT;

-- CHECK-Constraint für Status-Werte (idempotent)
ALTER TABLE payroll_snapshot
    DROP CONSTRAINT IF EXISTS payroll_snapshot_status_check;
ALTER TABLE payroll_snapshot
    ADD  CONSTRAINT payroll_snapshot_status_check
         CHECK (status IN ('BERECHNET','FREIGEGEBEN_GF','HR_BESTAETIGT','ABGESCHLOSSEN','STORNIERT'));

-- Bestehende Snapshots auf passenden Status migrieren:
--  • is_final=true (Periode abgeschlossen) → ABGESCHLOSSEN
--  • Periode in 'provisorisch_abgeschlossen' → HR_BESTAETIGT (HR-Phase aktiv)
--  • Sonst → FREIGEGEBEN_GF (Default — Snapshot existiert nur wenn GF bestätigt hat)
UPDATE payroll_snapshot s
SET    status = CASE
                  WHEN s.is_final = TRUE                                  THEN 'ABGESCHLOSSEN'
                  WHEN p.status = 'provisorisch_abgeschlossen'            THEN 'HR_BESTAETIGT'
                  ELSE 'FREIGEGEBEN_GF'
                END,
       gf_freigegeben_at = COALESCE(s.gf_freigegeben_at, s.created_at)
FROM   payroll_periode p
WHERE  p.id = s.payroll_periode_id;

-- Kontrolle: Status-Verteilung
SELECT status, COUNT(*) AS anzahl
FROM   payroll_snapshot
GROUP  BY status
ORDER  BY status;
