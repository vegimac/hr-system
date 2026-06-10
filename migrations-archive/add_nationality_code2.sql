-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 07.06.2026: zweiter (Alternativ-)Code für die Nationality-
-- Tabelle. Drittsysteme verwenden teils abweichende Codes — Mirus liefert
-- z.B. „XZ" für Kosovo (Post- und Zolldienst-Code) statt des offiziellen
-- ISO-3166-1-alpha-2-Codes „XK".
--
-- Spalte ist nullable. Importer (HrReviewImportController) bauen ihr
-- Lookup-Dictionary aus Code UND Code2 — bei Konflikt gewinnt Code als
-- kanonische Anzeige.
--
-- Lauf in TablePlus, dann ./deploy.sh
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE nationality
    ADD COLUMN IF NOT EXISTS code2 text NULL;

-- Initial-Befüllung: Kosovo — Mirus-Code XZ ergänzen
UPDATE nationality
   SET code2 = 'XZ'
 WHERE code = 'XK';

-- Verifikation
SELECT id, code, code2, is_active
  FROM nationality
 WHERE code2 IS NOT NULL
 ORDER BY code;
