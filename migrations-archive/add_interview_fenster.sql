-- Vorstellungsgespräch-Zeitfenster der GF/Manager (Walter-Vorgabe 09.08.2026, Stufe 1).
-- Der GF teilt mit, wann er an einem im Manager-Dienstplan als Arbeit (F/M/S)
-- geplanten Tag Zeit für Vorstellungsgespräche hat; HR sieht die Fenster im
-- HR-Hub. Terminbuchung durch HR = Stufe 2 (separat).
-- Läuft auch idempotent beim Server-Start (Program.cs).

CREATE TABLE IF NOT EXISTS interview_fenster (
    id          serial PRIMARY KEY,
    employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    datum       date NOT NULL,
    von_zeit    time NOT NULL,
    bis_zeit    time NOT NULL,
    bemerkung   text,
    created_at  timestamp without time zone NOT NULL DEFAULT now(),
    created_by  text
);
CREATE INDEX IF NOT EXISTS ix_interview_fenster_emp_datum ON interview_fenster (employee_id, datum);
