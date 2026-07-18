-- ════════════════════════════════════════════════════════════════════════
-- easy@work — alternative / alte Mitarbeiter-IDs (Alias)
-- Walter-Vorgabe 18.06.2026
--
-- Hintergrund: Bei manchen MA wechselt die easy@work-interne employee_id
-- mittendrin (z.B. Wiedereintritt / Neuanlage / Filialwechsel). Die alten
-- Stempel hängen dann noch an der ALTEN ID, während der aktuelle MA-Datensatz
-- (und unser gespeichertes easyatwork_employee_id) die NEUE ID trägt. Solche
-- Stempel landeten bisher als UNMATCHED.
--
-- Diese Tabelle hält pro MA beliebig viele zusätzliche („alte") easy@work-IDs.
-- Der Stempel-Sync prüft sie als Fallback, wenn die Stempel-employee_id nicht
-- über die normale MA-Liste auflösbar ist. Befüllt wird sie per Ein-Klick-
-- Knopf „→ diesem MA zuordnen" an der UNMATCHED-Zeile.
--
-- In TablePlus ausführen (kein psql-Wrapper).
-- ════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS easyatwork_employee_alias (
    id            SERIAL PRIMARY KEY,
    employee_id   INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    easyatwork_id INTEGER NOT NULL,
    note          TEXT,
    -- Schweizer Lokalzeit (Walter-Vorgabe 30.06.2026): timestamp WITHOUT time
    -- zone + DateTime.Now. timestamptz + DateTime.Now → Npgsql-Fehler
    -- «Cannot write DateTime with Kind=Local…» (Bug 18.07.2026).
    created_at    TIMESTAMP WITHOUT TIME ZONE DEFAULT now(),
    created_by    TEXT
);

-- Eine easy@work-ID darf nur EINEM MA zugeordnet sein (sonst wäre der Stempel
-- mehrdeutig). Verhindert versehentliche Doppel-Zuordnung.
CREATE UNIQUE INDEX IF NOT EXISTS ux_easyatwork_employee_alias_eawid
    ON easyatwork_employee_alias(easyatwork_id);

CREATE INDEX IF NOT EXISTS ix_easyatwork_employee_alias_emp
    ON easyatwork_employee_alias(employee_id);
