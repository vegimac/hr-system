-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 06.06.2026 (Stufe 1b):
--   Ferien %, Feiertag %, 13. ML % komplett aus dem Vertrag entfernen.
--   Werte kommen ab jetzt AUSSCHLIESSLICH aus CompanyProfile.Default* +
--   altersaware Engine-Logik (`vacation_six_weeks_from_age`).
--
-- Reihenfolge:
--   1) ZUERST Sanity-Check (siehe unten) — gibt es Sonderverträge?
--   2) ./deploy.sh  (Code referenziert die Spalten nicht mehr)
--   3) Dieses SQL in TablePlus  (DROPt die Spalten)
-- ════════════════════════════════════════════════════════════════════════

-- ─── SANITY-CHECK vor dem DROP ──────────────────────────────────────────
-- Findet Verträge, deren Werte NICHT dem aktuellen Filial-Default entsprechen.
-- Erwartung: leer (alle gleich) → DROP ist sicher.
-- Wenn nicht leer: Spalten-Werte gehen unwiederbringlich verloren — Walter
-- entscheidet, ob diese Sonderverträge wirklich auf den Filial-Default zurück
-- fallen sollen (das ist der ganze Sinn des Refactorings) oder ob ein Pre-Save
-- der Werte sinnvoll wäre.
SELECT e.id, e.employee_id, e.contract_start_date,
       e.vacation_percent, c.default_vacation_percent_5weeks, c.default_vacation_percent_6weeks,
       e.holiday_percent,  c.default_holiday_percent,
       e.thirteenth_salary_percent, c.default_thirteenth_salary_percent
  FROM employment e
  JOIN company_profile c ON c.id = e.company_profile_id
 WHERE (e.vacation_percent IS NOT NULL
        AND e.vacation_percent NOT IN (
            COALESCE(c.default_vacation_percent_5weeks, 10.64),
            COALESCE(c.default_vacation_percent_6weeks, 13.04)))
    OR (e.holiday_percent IS NOT NULL
        AND e.holiday_percent <> COALESCE(c.default_holiday_percent, 2.27))
    OR (e.thirteenth_salary_percent IS NOT NULL
        AND e.thirteenth_salary_percent <> COALESCE(c.default_thirteenth_salary_percent, 8.33));

-- ─── DROP (erst NACH dem deploy + Sanity-Check ausführen) ──────────────
ALTER TABLE employment DROP COLUMN IF EXISTS vacation_percent;
ALTER TABLE employment DROP COLUMN IF EXISTS holiday_percent;
ALTER TABLE employment DROP COLUMN IF EXISTS thirteenth_salary_percent;

-- ─── Bestätigung ────────────────────────────────────────────────────────
SELECT column_name, data_type
  FROM information_schema.columns
 WHERE table_name = 'employment'
   AND column_name LIKE '%percent%';
-- Erwartung: 0 rows.
