-- HR-Büro-Kalender für Vorstellungsgespräche (Walter-Vorgabe 09.08.2026).
-- Ersetzt den GF-Zeitfenster-Prozess: HR pflegt Termine mit Platz-Kapazität
-- (max. 2 Monate im Voraus) und bucht Kandidaten beim Einladen selbst.
-- Die alten Tabellen interview_fenster/interview_termin bleiben stehen
-- (UI-Zugang entfernt). Läuft auch idempotent beim Server-Start (Program.cs).

CREATE TABLE IF NOT EXISTS hr_interview_termin (
    id         serial PRIMARY KEY,
    datum      date NOT NULL,
    von_zeit   time NOT NULL,
    bis_zeit   time,
    plaetze    integer NOT NULL DEFAULT 1,
    bemerkung  text,
    created_at timestamp without time zone NOT NULL DEFAULT now(),
    created_by text
);
CREATE INDEX IF NOT EXISTS ix_hr_interview_termin_datum ON hr_interview_termin (datum);

CREATE TABLE IF NOT EXISTS hr_interview_buchung (
    id         serial PRIMARY KEY,
    termin_id  integer NOT NULL REFERENCES hr_interview_termin(id) ON DELETE CASCADE,
    kandidat   text NOT NULL,
    telefon    text,
    bemerkung  text,
    status     text NOT NULL DEFAULT 'GEPLANT' CHECK (status IN ('GEPLANT','ABGESAGT')),
    created_at timestamp without time zone NOT NULL DEFAULT now(),
    created_by text
);
CREATE INDEX IF NOT EXISTS ix_hr_interview_buchung_termin ON hr_interview_buchung (termin_id);
