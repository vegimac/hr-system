-- Briefanrede (für Korrespondenz-Vorlagen) und Heimatort (für Schweizer Bürger).
-- Walter wollte die Felder im Edit-Modus pflegen können — vorher gab es nur die
-- read-only Display-Felder ohne Backend-Pendant.

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS letter_salutation VARCHAR(200),
    ADD COLUMN IF NOT EXISTS place_of_origin   VARCHAR(150);
