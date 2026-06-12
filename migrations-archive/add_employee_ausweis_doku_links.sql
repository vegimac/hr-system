-- ──────────────────────────────────────────────────────────────────────
-- Walter-Vorgabe 13.06.2026: explizite Verknüpfung MA → konkretes Doku
-- für QST-Befreiungsbeleg.
--
-- Bisher: QstPflichtCheckService prüfte „existiert irgendein Dokument
-- mit linked_field_code='id_card'/'passport'/'permit' beim MA". Das war
-- unscharf — beim MA-Detail-Tab kann der User nicht erkennen, WELCHES
-- Dokument als Beleg zählt.
--
-- Jetzt: am `employee` direkt zwei FK-Spalten — eine pro Befreiungsgrund:
--   • id_pass_dokument_id  → für CH-Bürger (Pass ODER ID-Karte als Beleg)
--   • c_ausweis_dokument_id → für C-Ausweis-Inhaber (Bewilligungs-Dokument)
--
-- Analog der bereits bestehenden `qst_befreiung_dokument_id` (Behörden-
-- Befreiung), nur eben automatisch je nach Befreiungsgrund.
--
-- ON DELETE SET NULL: wenn der MA das Dokument später aus den Dokumenten
-- löscht, wird die Verknüpfung still aufgehoben (kein Cascade-Crash).
-- ──────────────────────────────────────────────────────────────────────

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS id_pass_dokument_id INTEGER
    REFERENCES employee_dokument(id) ON DELETE SET NULL;

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS c_ausweis_dokument_id INTEGER
    REFERENCES employee_dokument(id) ON DELETE SET NULL;
