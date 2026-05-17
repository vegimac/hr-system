-- add_akonto_workflow_phase2.sql
-- ---------------------------------------------------------------------------
-- Akonto-Workflow Etappe 1 (Walter-Vorgabe 16.05.2026)
-- Siehe AKONTO-LOHN-PLAN.md, Abschnitt 6/7.
--
-- Erweitert das Datenmodell um den 4-Augen-Workflow: der GF startet die
-- Akonto-Vorbereitung, bestätigt pro MA-Lohnblatt, schickt an HR. HR
-- kontrolliert, gibt frei und löst DTA aus.
--
-- ZWEI neue Status-Spuren parallel auf payroll_periode:
--   • Akonto-Strang (Mitte Monat) — eigene Status-/Audit-Spalten (neu)
--   • Definitiv-Strang (Ende Monat) — nutzt die bestehenden `status` +
--     `abgeschlossen_*` / `provisorisch_abgeschlossen_*` weiter. Umstellung
--     auf die einheitliche Workflow-Logik kommt in Etappe 2.
--
-- Diese Migration ist rein additiv — keine bestehenden Spalten werden
-- gedroppt oder verändert. Bestehender Definitivlauf läuft unverändert.
--
-- In TablePlus ausführen. Idempotent.
-- ---------------------------------------------------------------------------

-- 1) payroll_periode: Akonto-Status + Audit-Felder
ALTER TABLE payroll_periode
    ADD COLUMN IF NOT EXISTS akonto_status              VARCHAR(30)  NOT NULL DEFAULT 'OFFEN',
    ADD COLUMN IF NOT EXISTS akonto_gf_started_at       TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS akonto_gf_started_by       INTEGER REFERENCES app_user(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS akonto_gf_sent_at          TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS akonto_gf_sent_by          INTEGER REFERENCES app_user(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS akonto_hr_freigegeben_at   TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS akonto_hr_freigegeben_by   INTEGER REFERENCES app_user(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS akonto_ausbezahlt_at       TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS akonto_ausbezahlt_by       INTEGER REFERENCES app_user(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS akonto_dta_run_id          INTEGER;

COMMENT ON COLUMN payroll_periode.akonto_status IS
    'Status-Flow des Akonto-Strangs: OFFEN → IN_BEARBEITUNG_GF → BEI_HR → HR_FREIGEGEBEN → AUSBEZAHLT';

-- 2) akonto_zahlung: GF-Freigabe-Felder + Korrektur-Kommentare
-- Status-Enum erweitert um FREIGEGEBEN_GF (zwischen BERECHNET und AUSBEZAHLT)
-- und STORNIERT. Datentyp bleibt VARCHAR — aber die CHECK-Constraint aus
-- Phase 1 listet nur die alten Werte und muss erweitert werden, sonst
-- scheitert jedes UPDATE auf FREIGEGEBEN_GF (Bug-Fix Walter 16.05.2026).
ALTER TABLE akonto_zahlung
    ADD COLUMN IF NOT EXISTS gf_freigegeben_at  TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS gf_freigegeben_by  INTEGER REFERENCES app_user(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS kommentar_gf       TEXT,
    ADD COLUMN IF NOT EXISTS kommentar_hr       TEXT;

ALTER TABLE akonto_zahlung
    DROP CONSTRAINT IF EXISTS akonto_zahlung_status_check;
ALTER TABLE akonto_zahlung
    ADD CONSTRAINT akonto_zahlung_status_check
    CHECK (status IN ('BERECHNET', 'FREIGEGEBEN_GF', 'AUSBEZAHLT', 'STORNIERT'));

COMMENT ON COLUMN akonto_zahlung.status IS
    'Status pro MA-Lohnblatt: BERECHNET → FREIGEGEBEN_GF → AUSBEZAHLT. Bei Storno: STORNIERT.';

-- 3) Kontrolle: neue Struktur anzeigen
SELECT column_name, data_type, is_nullable, column_default
FROM   information_schema.columns
WHERE  table_name = 'payroll_periode'
  AND  column_name LIKE 'akonto%'
ORDER  BY column_name;

SELECT column_name, data_type, is_nullable
FROM   information_schema.columns
WHERE  table_name = 'akonto_zahlung'
  AND  (column_name LIKE 'gf_%' OR column_name LIKE 'kommentar%' OR column_name = 'status')
ORDER  BY column_name;
