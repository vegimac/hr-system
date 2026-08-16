-- Lohnausweis-Finalisierung (Walter 16.08.2026): DocID (UUID) + CreationDate
-- werden beim ersten «Final erzeugen» pro MA+Jahr eingefroren — Wiederdrucke
-- tragen dieselbe Identifikation. Laeuft auch idempotent beim Serverstart.
CREATE TABLE IF NOT EXISTS lohnausweis_final (
    id            SERIAL PRIMARY KEY,
    employee_id   INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    year          INTEGER NOT NULL,
    doc_id        UUID NOT NULL,
    creation_date TIMESTAMP WITHOUT TIME ZONE NOT NULL,
    created_by    TEXT,
    UNIQUE (employee_id, year)
);
