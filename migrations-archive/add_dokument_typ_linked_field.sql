-- ════════════════════════════════════════════════════════════════════════
-- Verknüpfung Dokument-Typ ↔ MA-Stammdatenfeld
--
-- Optionale Spalte linked_field_code: zeigt auf ein Stammdaten-Feld in der
-- MA-Maske. Wenn gesetzt, erscheint neben diesem Feld ein 📎-Button, der
-- das neueste Dokument dieses Typs für den MA öffnet.
--
-- Vorgesehene Codes (kann erweitert werden):
--   permit          → Aufenthaltsbewilligung
--   passport        → Pass
--   id_card         → Identitätskarte
--   ahv_card        → AHV-Karte / SVN-Bestätigung
--   bank_card       → Bankkarte / IBAN-Beleg
--   marriage_cert   → Heiratsurkunde
--   contract        → Arbeitsvertrag
--   social_decision → Bescheid Sozialamt / Steueramt
--
-- Non-breaking: alle bestehenden Typen haben NULL → kein 📎-Button, alles
-- läuft wie zuvor. Walter setzt den Code nach und nach im Admin-UI.
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE dokument_typ
    ADD COLUMN IF NOT EXISTS linked_field_code VARCHAR(50) NULL;

COMMENT ON COLUMN dokument_typ.linked_field_code IS
    'Optionaler Code für die Verknüpfung mit einem Stammdaten-Feld in der MA-Maske
     (permit, passport, ahv_card, bank_card, etc.). NULL = keine Verknüpfung.';

CREATE INDEX IF NOT EXISTS idx_dokument_typ_linked_field
    ON dokument_typ(linked_field_code)
    WHERE linked_field_code IS NOT NULL;
