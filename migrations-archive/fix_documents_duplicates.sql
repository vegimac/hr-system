-- ════════════════════════════════════════════════════════════════════
-- Fix: Doppelte Kategorien & Typen aus mehrfachem Import bereinigen
-- ════════════════════════════════════════════════════════════════════
-- Bricht ab, wenn schon Dokumente hochgeladen sind (Sicherheitsnetz).
-- Setzt anschliessend UNIQUE-Constraints, damit ein Doppel-Import
-- nie wieder durchläuft.
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- Sicherheits-Stop: nur ausführen wenn keine Dokumente existieren
DO $$
DECLARE
    cnt int;
BEGIN
    SELECT COUNT(*) INTO cnt FROM employee_dokument;
    IF cnt > 0 THEN
        RAISE EXCEPTION 'employee_dokument enthaelt % Dokumente -- manuelle Bereinigung erforderlich', cnt;
    END IF;
END $$;

-- Wipe und neu — alles weg
TRUNCATE TABLE employee_dokument, dokument_typ, dokument_kategorie RESTART IDENTITY CASCADE;

-- UNIQUE-Constraints, damit Re-Imports zukünftig idempotent sind
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uk_dokument_kategorie_name') THEN
        ALTER TABLE dokument_kategorie ADD CONSTRAINT uk_dokument_kategorie_name UNIQUE (name);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uk_dokument_typ_kat_name') THEN
        ALTER TABLE dokument_typ ADD CONSTRAINT uk_dokument_typ_kat_name UNIQUE (kategorie_id, name);
    END IF;
END $$;

COMMIT;
