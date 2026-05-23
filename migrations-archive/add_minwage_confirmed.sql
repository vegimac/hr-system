-- Mindestlöhne: „bestätigt"-Flag für geplante Folge-Sätze (Walter-Vorgabe 23.05.2026)
-- Drei-Farben-Logik in der Planungsspalte „ab <Datum>":
--   rot    = Folge-Satz noch nicht bestätigt (frisch via /copy, default false)
--   grün   = Betrag gegenüber dem aktuellen Satz geändert
--   orange = bestätigt (gespeichert), aber Betrag unverändert
-- Beim Speichern (PUT /api/minimum-wage-rules/{id}) setzt der Controller confirmed=true.
-- In TablePlus ausführen (kein psql-Wrapper).

ALTER TABLE minimum_wage_rule_new
    ADD COLUMN IF NOT EXISTS confirmed boolean NOT NULL DEFAULT false;
