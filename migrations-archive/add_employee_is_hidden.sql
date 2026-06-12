-- ──────────────────────────────────────────────────────────────────────
-- Walter-Vorgabe 12.06.2026: Soft-Delete-Flag für Mitarbeiter.
--
-- Wenn der Admin im MA-Modul auf "Löschen" klickt:
--   • MA hat Lohn-Daten (PayrollSnapshot/Saldo/Akonto) → IsHidden = true
--     (Datensatz bleibt für Audit + Jahresauswertungen, ist aber in ALLEN
--      MA-Listen, Pickern und im Lohnlauf ausgeblendet)
--   • MA hat KEINE Lohn-Daten → DELETE über alle abhängigen Tabellen
--     (Verträge, Bewilligungen, Dokumente, Bank, etc. werden mitgelöscht)
--
-- Default false, damit bestehende MA-Zeilen unverändert bleiben.
-- ──────────────────────────────────────────────────────────────────────

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS is_hidden BOOLEAN NOT NULL DEFAULT false;

-- Optionaler Index, falls die Filter-Queries deutlich häufiger werden
-- (Standard-Listen filtern is_hidden=false; aktuell ohne Index ausreichend
-- bei ~ein paar tausend MA, kann nachgereicht werden wenn nötig).
-- CREATE INDEX IF NOT EXISTS idx_employee_is_hidden ON employee(is_hidden);
