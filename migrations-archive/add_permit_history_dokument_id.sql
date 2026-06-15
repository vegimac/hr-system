-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 14.06.2026: pro Bewilligung das verknüpfte Dokument.
--
-- Neue Spalte employee_permit_history.dokument_id (FK auf employee_dokument)
-- + Index auf den FK für schnellen Zugriff.
-- + ON DELETE SET NULL — wenn das Dokument doch mal gelöscht werden DARF
--   (über den Lösch-Schutz-Check im Controller), bleibt der Permit-Eintrag
--   stehen, nur die Verknüpfung fällt weg.
--
-- Backfill: bisher wurde das C-Ausweis-Dokument auf employee.c_ausweis_
-- dokument_id (ein FK pro MA) gespeichert. Wir wandern das jetzt auf die
-- jüngste Permit-History des MA — pro MA der Eintrag mit dem höchsten
-- ValidTo (NULL = max), bei Gleichheit der älteste ValidFrom (Walter-
-- Konvention: Original vor Import-Duplikat).
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE employee_permit_history
    ADD COLUMN IF NOT EXISTS dokument_id integer
        REFERENCES employee_dokument(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS ix_employee_permit_history_dokument_id
    ON employee_permit_history(dokument_id);

-- ─── Backfill ────────────────────────────────────────────────────────────
-- Pro MA die jüngste Permit-History identifizieren und employee.c_ausweis_
-- dokument_id darauf übertragen. CTE/RANK braucht's nicht — wir nutzen
-- DISTINCT ON wie überall im Postgres-Codebase.
WITH youngest AS (
    SELECT DISTINCT ON (h.employee_id)
        h.id            AS history_id,
        h.employee_id   AS employee_id
    FROM employee_permit_history h
    ORDER BY h.employee_id,
             COALESCE(h.valid_to, DATE '9999-12-31') DESC,
             h.valid_from ASC,
             h.id ASC
)
UPDATE employee_permit_history h
   SET dokument_id = e.c_ausweis_dokument_id
  FROM youngest y
  JOIN employee e ON e.id = y.employee_id
 WHERE h.id = y.history_id
   AND e.c_ausweis_dokument_id IS NOT NULL
   AND h.dokument_id IS NULL;

-- Wie viele Einträge wurden befüllt? (nur Info — kein Effekt)
SELECT COUNT(*) AS backfilled
  FROM employee_permit_history
 WHERE dokument_id IS NOT NULL;
