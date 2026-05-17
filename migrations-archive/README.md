# Migrations-Archiv

Hier liegen alle SQL-Migrationen die schon auf Produktion (`test.hr-srgmbh.ch`)
und allen lokalen DBs angewendet wurden — sie sind also faktisch schon "drin".

**Warum behalten statt löschen?**

- Historie: bei „seit wann haben wir Spalte X?" rasch nachschlagbar.
- Frische Dev-DB von Null: Reihenfolge + Inhalt nachvollziehbar
  (alternativ via `RESTORE.md` aus Produktions-Backup).
- Dokumentation für künftige Entwickler.

**Konvention für neue Migrationen:**

Neue, noch nicht ausgeführte SQL-Migrationen kommen ins Projekt-Root.
Sobald sie auf Produktion gelaufen sind → hierher verschieben.

**Dateinamen-Schema:**

- `add_*` — Schema-Änderungen (Spalten, Tabellen, Indexe).
- `import_*`, `insert_*` — Einmalige Daten-Loads (Bank-Master, PLZ-Liste etc.).
- `update_*`, `migrate_*`, `refactor_*` — Daten-Transformationen.
- `fix_*`, `mark_*`, `backfill_*` — Punktuelle Daten-Korrekturen.
- `check_*` — Diagnose-Queries (kein Schreibvorgang).
- `drop_*` — Tabellen/Spalten entfernen.

**Spezielle Datei:**

- `add_behoerde_zahlungshinweis.sql` ist mit `// VERWORFEN` markiert —
  wurde NICHT ausgeführt. Idee verworfen weil Verwendungszweck zur
  `EmployeeLohnAssignment.Bemerkung` gehört, nicht zur `Behoerde`.
