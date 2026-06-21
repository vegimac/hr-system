-- ════════════════════════════════════════════════════════════════════════
-- Backfill: inaktive Personaldossiers OHNE Employment-Zeile (Walter 21.06.2026)
-- ════════════════════════════════════════════════════════════════════════
-- Problem: frühere easy@work-Importe haben inaktive MA nur als `employee`
-- angelegt, ohne `employment` → in SQL-Auswertungen fehlt Filiale/Restaurant.
--
-- Dieser Backfill legt für jeden INAKTIVEN MA ohne Employment eine INAKTIVE
-- Employment-Zeile an. Die Filiale wird aus dem Personalnummer-PRÄFIX
-- abgeleitet: pro Filiale haben die bestehenden Verträge ein charakteristisches
-- Nummernpräfix (z.B. 750xxx = Sursee, 580xxx = Oftringen). Wir bilden das
-- Präfix→Filiale-Mapping aus den VORHANDENEN Employments und ordnen darüber zu.
-- MA, deren Präfix sich nicht ableiten lässt, bleiben unangetastet (kein Raten).
--
-- Idempotent: legt nur an, wo NOCH KEINE Employment existiert (NOT EXISTS).
-- Mehrfaches Ausführen erzeugt keine Dubletten.
-- In TablePlus ausführen. Vorher mit dem SELECT (unten) prüfen, was zugeordnet
-- würde.
-- ════════════════════════════════════════════════════════════════════════

WITH prefix_branch AS (
    -- Häufigste Filiale je 3-stelligem Nummernpräfix (aus bestehenden Verträgen).
    SELECT prefix, company_profile_id
    FROM (
        SELECT substring(regexp_replace(e.employee_number, '\D', '', 'g') FROM 1 FOR 3) AS prefix,
               emp.company_profile_id,
               row_number() OVER (
                   PARTITION BY substring(regexp_replace(e.employee_number, '\D', '', 'g') FROM 1 FOR 3)
                   ORDER BY count(*) DESC
               ) AS rk
        FROM employee e
        JOIN employment emp ON emp.employee_id = e.id
        WHERE emp.company_profile_id IS NOT NULL
          AND length(regexp_replace(e.employee_number, '\D', '', 'g')) >= 3
        GROUP BY 1, 2
    ) t
    WHERE rk = 1
)
INSERT INTO employment
    (employee_id, company_profile_id, employment_model, salary_type,
     contract_start_date, contract_end_date, is_active)
SELECT e.id,
       pb.company_profile_id,
       '',                                              -- Modell unbekannt (best-effort)
       '',                                              -- Salary-Type unbekannt
       COALESCE(e.entry_date::date, e.exit_date::date), -- Start = Eintritt, sonst Austritt
       e.exit_date::date,                               -- Ende  = Austritt
       false                                            -- KEINE Lohnlauf-Teilnahme
FROM employee e
JOIN prefix_branch pb
   ON pb.prefix = substring(regexp_replace(e.employee_number, '\D', '', 'g') FROM 1 FOR 3)
WHERE e.is_active = false
  AND length(regexp_replace(e.employee_number, '\D', '', 'g')) >= 3
  AND NOT EXISTS (SELECT 1 FROM employment x WHERE x.employee_id = e.id);

-- ── Vorab-Prüfung (read-only) — vor dem INSERT laufen lassen, um zu sehen,
--    welche MA zugeordnet würden und wie viele KEIN Präfix-Mapping haben:
--
-- WITH prefix_branch AS ( … gleicher CTE wie oben … )
-- SELECT e.id, e.employee_number, e.exit_date, pb.company_profile_id
-- FROM employee e
-- LEFT JOIN prefix_branch pb
--   ON pb.prefix = substring(regexp_replace(e.employee_number,'\D','','g') FROM 1 FOR 3)
-- WHERE e.is_active = false
--   AND NOT EXISTS (SELECT 1 FROM employment x WHERE x.employee_id = e.id)
-- ORDER BY pb.company_profile_id NULLS FIRST, e.employee_number;
