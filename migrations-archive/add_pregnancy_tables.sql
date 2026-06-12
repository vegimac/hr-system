-- Walter-Vorgabe 10.06.2026: Mutterschafts-Modul.
-- Globales Regelwerk + pro-MA-Schwangerschaften mit automatischer Fristenberechnung.

CREATE TABLE IF NOT EXISTS pregnancy_rule (
    id SERIAL PRIMARY KEY,
    code VARCHAR(30) NOT NULL UNIQUE,
    bezeichnung TEXT NOT NULL,
    beschreibung TEXT,
    gesetz VARCHAR(100),
    berechnung_basis VARCHAR(20) NOT NULL DEFAULT 'ET',   -- ET | GEBURT | MELDUNG
    offset_monate INTEGER DEFAULT 0,
    offset_wochen INTEGER DEFAULT 0,
    richtung VARCHAR(10) NOT NULL DEFAULT 'VORHER',       -- VORHER | NACHHER
    ist_arbeitsverbot BOOLEAN DEFAULT false,
    sort_order INTEGER DEFAULT 99,
    aktiv BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS employee_pregnancy (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER NOT NULL REFERENCES employee(id),
    meldedatum DATE NOT NULL,
    errechneter_termin DATE NOT NULL,
    geburtsdatum DATE,
    arztzeugnis_vorhanden BOOLEAN DEFAULT false,
    bemerkung TEXT,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_pregnancy_employee ON employee_pregnancy(employee_id);

-- Seed: gesetzliche Default-Regeln (ArG/ArGV 1/OR). Walter kann sie über die
-- Admin-UI anpassen falls sich die Rechtslage ändert oder ein interner
-- Standard greift.
INSERT INTO pregnancy_rule (code, bezeichnung, beschreibung, gesetz, berechnung_basis, offset_monate, offset_wochen, richtung, ist_arbeitsverbot, sort_order) VALUES
('RISIKO',        'Risiko-Assessment durchführen',          'Gefährdungsbeurteilung am Arbeitsplatz, kein schweres Heben >5kg',  'ArGV 1 Art. 62',         'MELDUNG', 0, 0, 'NACHHER', false, 10),
('STEHEN_4H',     'Stehend max. 4 Stunden/Tag',             'Stehende Tätigkeit ab dem 4. Schwangerschaftsmonat eingeschränkt',  'ArGV 1 Art. 61 Abs. 3',  'ET',     -5, 0, 'VORHER',  false, 20),
('NACHT_VERBOT',  'Keine Nachtarbeit (20:00–06:00)',        'Nachtarbeitsverbot ab der 8. Schwangerschaftswoche vor ET',         'ArG Art. 35a Abs. 4',    'ET',      0,-8, 'VORHER',  false, 30),
('UEBERZEIT',     'Keine Überstunden',                       'Überzeitverbot ab dem 8. Schwangerschaftsmonat',                   'ArG Art. 35a Abs. 2',    'ET',     -1, 0, 'VORHER',  false, 31),
('VERBOT_VOR',    'Arbeitsverbot vor Geburt',                'Freiwilliges Arbeitsverbot mit Arztzeugnis',                       'ArG Art. 35a Abs. 3',    'ET',      0,-8, 'VORHER',  true,  40),
('VERBOT_NACH',   'Absolutes Arbeitsverbot nach Geburt',     'Arbeitsverbot 8 Wochen nach Geburt — keine Ausnahmen',             'ArG Art. 35a Abs. 3',    'GEBURT',  0, 8, 'NACHHER', true,  50),
('FREIWILLIG',    'Freiwillige Arbeit (Woche 9–16)',         'MA darf arbeiten wenn sie will, AG darf nicht verlangen',           'ArG Art. 35a Abs. 3',    'GEBURT',  0,16, 'NACHHER', false, 60),
('KUENDIG_SCHUTZ','Kündigungsschutz',                        'Kündigung durch AG ist nichtig',                                   'OR Art. 336c Abs. 1c',   'GEBURT',  0,16, 'NACHHER', false, 70),
('STILLZEIT',     'Bezahlte Stillpausen',                    'Stillende Mütter haben Anspruch auf bezahlte Stillzeit',           'ArG Art. 35a Abs. 2',    'GEBURT', 12, 0, 'NACHHER', false, 80)
ON CONFLICT (code) DO NOTHING;
