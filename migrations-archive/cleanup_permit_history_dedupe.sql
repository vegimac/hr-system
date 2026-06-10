-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 07.06.2026 — Bewilligungs-Cleanup
--
-- Hintergrund: durch Importer (Bewilligungsliste-Import, Initial aus
-- Stammdaten) sind pro MA mehrere überlappende Permit-History-Einträge
-- entstanden. Künftig erlaubt das System keine Überlappungen mehr (POST
-- macht Auto-Close, PUT prüft Overlap). Bereinigung: pro MA nur den
-- Eintrag behalten mit
--   1) maximalem valid_to (NULL gilt als max),
--   2) bei Gleichheit: ältestem valid_from,
--   3) bei Gleichheit: kleinster id.
--
-- Lauf in TablePlus. Schritt 1 = Vorschau, Schritt 2 = wirklicher Delete.
-- ════════════════════════════════════════════════════════════════════════

-- ── Schritt 1: VORSCHAU — welche Zeilen würden gelöscht? ──────────────
WITH ranked AS (
    SELECT id,
           employee_id,
           valid_from,
           valid_to,
           ROW_NUMBER() OVER (
               PARTITION BY employee_id
               ORDER BY COALESCE(valid_to, DATE '9999-12-31') DESC,
                        valid_from ASC,
                        id ASC
           ) AS rn
      FROM employee_permit_history
)
SELECT r.id,
       r.employee_id,
       e.first_name,
       e.last_name,
       r.valid_from,
       r.valid_to,
       CASE WHEN r.rn = 1 THEN 'BEHALTEN' ELSE 'LÖSCHEN' END AS aktion
  FROM ranked r
  JOIN employee e ON e.id = r.employee_id
 WHERE r.employee_id IN (
       SELECT employee_id
         FROM employee_permit_history
        GROUP BY employee_id
       HAVING COUNT(*) > 1
 )
 ORDER BY r.employee_id, r.rn;

-- ── Schritt 2: DELETE (erst ausführen wenn die Vorschau passt!) ───────
WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY employee_id
               ORDER BY COALESCE(valid_to, DATE '9999-12-31') DESC,
                        valid_from ASC,
                        id ASC
           ) AS rn
      FROM employee_permit_history
)
DELETE FROM employee_permit_history
 WHERE id IN (SELECT id FROM ranked WHERE rn > 1);

-- ── Schritt 3: Verifikation — pro MA max. 1 Eintrag ───────────────────
SELECT employee_id, COUNT(*) AS anzahl
  FROM employee_permit_history
 GROUP BY employee_id
HAVING COUNT(*) > 1;
-- Sollte 0 Zeilen liefern.
