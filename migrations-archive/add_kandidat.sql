-- Kandidaten-Pipeline GF → HR (Walter-Vorgabe 10.08.2026, Etappe 1).
-- Der GF reicht nach dem Vorstellungsgespräch einen Einstellungs-Kandidaten
-- an HR ein (bewusst KEIN employee — der MA entsteht erst nach HR-Annahme in
-- easy@work). Anhänge liegen im Storage unter kandidaten/{kandidatId}/.
-- Läuft auch idempotent beim Server-Start (Program.cs).

CREATE TABLE IF NOT EXISTS kandidat (
    id                  serial PRIMARY KEY,
    company_profile_id  integer NOT NULL REFERENCES company_profile(id),
    vorname             text NOT NULL,
    name                text NOT NULL,
    telefon             text,
    fruehester_eintritt date,
    lgav_ausbildung     text,
    wunsch_termin_id    integer REFERENCES hr_interview_termin(id) ON DELETE SET NULL,
    bemerkung           text,
    status              text NOT NULL DEFAULT 'NEU'
                        CHECK (status IN ('NEU','ANGENOMMEN','ABGELEHNT','ERLEDIGT')),
    ablehnungsgrund     text,
    created_at          timestamp without time zone NOT NULL DEFAULT now(),
    created_by          text,
    decided_at          timestamp without time zone,
    decided_by          text
);
CREATE INDEX IF NOT EXISTS ix_kandidat_status ON kandidat (status);

CREATE TABLE IF NOT EXISTS kandidat_dokument (
    id                serial PRIMARY KEY,
    kandidat_id       integer NOT NULL REFERENCES kandidat(id) ON DELETE CASCADE,
    original_filename text NOT NULL,
    storage_filename  text NOT NULL,
    created_at        timestamp without time zone NOT NULL DEFAULT now(),
    created_by        text
);
CREATE INDEX IF NOT EXISTS ix_kandidat_dokument_kandidat ON kandidat_dokument (kandidat_id);

-- Nachtrag 10.08.2026: E-Mail des Kandidaten.
ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS email text;

-- Nachtrag 10.08.2026 (Etappe 2): Absage-Versand an den Kandidaten.
-- Abgelehnte Kandidaten werden 30 Tage nach dem Entscheid automatisch
-- gelöscht (täglicher Sync-Job); angenommene beim Verknüpfen mit dem MA.
ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS absage_gesendet_am timestamp without time zone;
ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS absage_kanal text;

-- Nachtrag 10.08.2026: Wunschtermin überlebt die Kandidat-Löschung am MA
-- (onboarding_wunsch) — sichtbar beim Einladen, gelöscht beim Buchen.
CREATE TABLE IF NOT EXISTS onboarding_wunsch (
    id          serial PRIMARY KEY,
    employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
    termin_id   integer NOT NULL REFERENCES hr_interview_termin(id) ON DELETE CASCADE,
    created_at  timestamp without time zone NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_onboarding_wunsch_emp ON onboarding_wunsch (employee_id);

-- Nachtrag 10.08.2026: kein Sofort-Löschen beim Verknüpfen — Status ERLEDIGT
-- mit MA-Referenz, Auto-Löschung nach 30 Tagen; dazu freie HR-Notiz.
ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS erledigt_am timestamp without time zone;
ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS verknuepft_employee_id integer;
ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS notiz text;
