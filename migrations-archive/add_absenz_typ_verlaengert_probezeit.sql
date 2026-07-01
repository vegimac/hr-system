-- Absenz-Typ-Flag „verlängert Probezeit" (Walter-Vorgabe 30.06.2026)
-- true = eine Absenz dieses Typs, die in die Probezeit fällt, verlängert die
-- Probezeit um die Anzahl Absenztage. Pflegbar in den Absenz-Typen.
-- In TablePlus ausführen (nicht via psql-CLI).

ALTER TABLE absenz_typ
    ADD COLUMN IF NOT EXISTS verlaengert_probezeit boolean NOT NULL DEFAULT false;
