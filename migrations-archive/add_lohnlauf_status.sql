-- ─────────────────────────────────────────────────────────────────────────
-- Lohnlauf-Status-Erweiterung + Audit-Log
-- ─────────────────────────────────────────────────────────────────────────
-- Status-Flow:  offen → provisorisch_abgeschlossen → abgeschlossen
--   • offen                       — GF kontrolliert MA-Lohnzettel
--   • provisorisch_abgeschlossen  — GF hat alle MA bestätigt, Lohnzettel
--                                    eingefroren; HR liest Vorab-PDF, kommuniziert
--                                    mit GF via Posteingang. Nur HR/Admin können
--                                    zurück auf offen.
--   • abgeschlossen               — Definitiver Lohnabschluss durch HR: DTA
--                                    generiert, Periode dicht. Nur Admin kann
--                                    wieder öffnen.
--
-- Auszahlungsdatum: vom HR beim definitiven Abschluss erfasst (Default = Tag
-- nach Abschluss). Wird ins DTA als RequestedExecutionDate geschrieben.
-- ─────────────────────────────────────────────────────────────────────────

ALTER TABLE payroll_periode
  ADD COLUMN IF NOT EXISTS provisorisch_abgeschlossen_am  TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS provisorisch_abgeschlossen_von INTEGER,
  ADD COLUMN IF NOT EXISTS auszahlungsdatum               DATE;

-- Audit-Log: jeder Status-Übergang einer Lohnperiode wird festgehalten.
-- Macht den Lohnlauf-Prozess revisionssicher.
CREATE TABLE IF NOT EXISTS payroll_periode_audit (
    id                  SERIAL PRIMARY KEY,
    payroll_periode_id  INTEGER NOT NULL REFERENCES payroll_periode(id) ON DELETE CASCADE,
    user_id             INTEGER,                      -- NULL erlaubt falls User später gelöscht
    user_name           VARCHAR(200) NOT NULL,        -- denormalisiert für Historie
    action              VARCHAR(40) NOT NULL,         -- PROVISORISCH_ABGESCHLOSSEN | AN_GF_GESENDET | ZURUECK_AN_GF | DEFINITIV_ABGESCHLOSSEN | WIEDER_GEOEFFNET
    bemerkung           TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_ppa_periode_time
    ON payroll_periode_audit (payroll_periode_id, created_at);
