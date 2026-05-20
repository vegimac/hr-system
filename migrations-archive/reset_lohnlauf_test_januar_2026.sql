-- ============================================================================
-- TEST-RESET: Lohnlauf Januar 2026 KOMPLETT zurücksetzen (Akonto + Definitiv)
-- ----------------------------------------------------------------------------
-- Zweck: den gesamten Januar-Lohnlauf einer Filiale auf den jungfräulichen
--        Zustand "offen / OFFEN" zurücksetzen, damit der komplette Ablauf
--        (Akonto-Vorbereitung → GF → HR → DTA, dann Definitivlauf) erneut
--        getestet werden kann.
--
-- Ausführung: in TablePlus, ganzer Block. Filiale = restaurant_code '058'
--             (Oftringen). Für eine andere Filiale '058' ersetzen.
--
-- WICHTIG (Reihenfolge wegen Fremdschlüsseln):
--   payroll_lohn_abtretung_entry hängt an payroll_snapshot → MUSS zuerst weg,
--   sonst FK-Verletzung → ganze Transaktion rollt zurück (ROLLBACK).
--
-- WAS BLEIBT (bewusst NICHT gelöscht):
--   • lohn_zulage  → Saldo-Vortrag (Migrations-Saldi) + manuelle Zulagen/Abzüge.
--   • akonto_termin → Auszahlungstermin-Konfiguration.
--   • Verträge, Stempelzeiten, Absenzen, Stammdaten.
-- WAS GELÖSCHT WIRD (nur Monat 01/2026 der Filiale):
--   payroll_lohn_abtretung_entry, payroll_snapshot, akonto_zahlung,
--   payroll_saldo, Lohnzettel im MA-Postfach, Periode-Audit.
--   Periode-Status → offen / OFFEN.
-- ============================================================================

-- ── Schritt 0 (optional): Periode prüfen ────────────────────────────────────
SELECT pp.id AS periode_id, pp.company_profile_id, cp.restaurant_code,
       pp.status, pp.akonto_status
FROM   payroll_periode pp
JOIN   company_profile cp ON cp.id = pp.company_profile_id
WHERE  cp.restaurant_code = '058' AND pp.year = 2026 AND pp.month = 1;

-- ── Schritt 1: Reset ────────────────────────────────────────────────────────
BEGIN;

-- a) Lohnzettel aus den MA-Postfächern entfernen
DELETE FROM mailbox_document
 WHERE target_type = 'EMPLOYEE'
   AND company_profile_id = (SELECT id FROM company_profile WHERE restaurant_code = '058')
   AND original_filename LIKE '%Lohnzettel_2026-01%';

-- b) Lohnabtretungen (Kind von payroll_snapshot) — ZUERST!
DELETE FROM payroll_lohn_abtretung_entry
 WHERE payroll_snapshot_id IN (
   SELECT s.id FROM payroll_snapshot s
   JOIN payroll_periode p ON p.id = s.payroll_periode_id
   WHERE p.company_profile_id = (SELECT id FROM company_profile WHERE restaurant_code = '058')
     AND p.year = 2026 AND p.month = 1);

-- c) Definitiv-Snapshots löschen
DELETE FROM payroll_snapshot
 WHERE payroll_periode_id IN (
   SELECT id FROM payroll_periode
    WHERE company_profile_id = (SELECT id FROM company_profile WHERE restaurant_code = '058')
      AND year = 2026 AND month = 1);

-- d) Periode-Audit löschen
DELETE FROM payroll_periode_audit
 WHERE payroll_periode_id IN (
   SELECT id FROM payroll_periode
    WHERE company_profile_id = (SELECT id FROM company_profile WHERE restaurant_code = '058')
      AND year = 2026 AND month = 1);

-- e) Akonto-Zahlungen löschen
DELETE FROM akonto_zahlung
 WHERE company_profile_id = (SELECT id FROM company_profile WHERE restaurant_code = '058')
   AND period_year = 2026 AND period_month = 1;

-- f) Monats-Saldi löschen (NICHT die Saldo-Vortrag-Werte in lohn_zulage!)
DELETE FROM payroll_saldo
 WHERE company_profile_id = (SELECT id FROM company_profile WHERE restaurant_code = '058')
   AND period_year = 2026 AND period_month = 1;

-- g) Periode auf jungfräulich "offen / OFFEN" zurücksetzen
UPDATE payroll_periode SET
    status                          = 'offen',
    akonto_status                   = 'OFFEN',
    abgeschlossen_am                = NULL,
    abgeschlossen_von               = NULL,
    provisorisch_abgeschlossen_am   = NULL,
    provisorisch_abgeschlossen_von  = NULL,
    auszahlungsdatum                = NULL,
    akonto_gf_started_at            = NULL,
    akonto_gf_started_by            = NULL,
    akonto_gf_sent_at               = NULL,
    akonto_gf_sent_by               = NULL,
    akonto_hr_freigegeben_at        = NULL,
    akonto_hr_freigegeben_by        = NULL,
    akonto_ausbezahlt_at            = NULL,
    akonto_ausbezahlt_by            = NULL,
    akonto_auszahlungsdatum         = NULL,
    akonto_dta_run_id               = NULL
 WHERE company_profile_id = (SELECT id FROM company_profile WHERE restaurant_code = '058')
   AND year = 2026 AND month = 1;

COMMIT;
