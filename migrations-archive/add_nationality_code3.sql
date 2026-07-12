-- ISO-3166 alpha-3 an der Nationalität (Walter 12.07.2026).
-- Die Ausländerausweise drucken den DREIbuchstaben-Code (BGR, MKD, ESP …),
-- das System führt alpha-2 (nationality.code). Die Spalte wird beim
-- Server-Start idempotent aus der statischen Tabelle CountryIso3 gefüllt
-- (nur wo leer) — dieses SQL legt nur die Spalte an. TablePlus-Block:

ALTER TABLE nationality ADD COLUMN IF NOT EXISTS code3 text;
