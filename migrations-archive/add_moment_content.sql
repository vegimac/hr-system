-- HINWEIS (01.07.2026): Diese Datei ist NUR REFERENZ. Der massgebliche, idempotente
-- Seed (7 Typen, 3 Emotionsgrade Calm/Warm/Personal, 21 Texte) läuft automatisch
-- beim App-Start in Program.cs. NICHT mehr manuell ausführen — sonst entstehen die
-- alten Platzhalter-Emotionsgrade. Tabellen/Seed werden beim Deploy automatisch angelegt.
--
-- OneCrew Moments — Inhalts-Tabellen (Walter-Vorgabe 01.07.2026):
--   moment_type  = die Momente (7 Typen, mit Consent-Kategorie)
--   moment_tone  = Emotionsgrad (schlicht/herzlich/sehr persönlich/kurz)
--   moment_text  = Texte je Kombination Typ × Emotionsgrad (mehrere möglich)
-- In TablePlus ausführen (nicht via psql-CLI).

CREATE TABLE IF NOT EXISTS moment_type (
    id               serial PRIMARY KEY,
    code             text NOT NULL,
    name             text NOT NULL,
    consent_category text NOT NULL,
    sort_order       integer NOT NULL DEFAULT 0,
    is_active        boolean NOT NULL DEFAULT true
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_moment_type_code ON moment_type (code);

CREATE TABLE IF NOT EXISTS moment_tone (
    id          serial PRIMARY KEY,
    code        text NOT NULL,
    name        text NOT NULL,
    sort_order  integer NOT NULL DEFAULT 0,
    is_active   boolean NOT NULL DEFAULT true
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_moment_tone_code ON moment_tone (code);

CREATE TABLE IF NOT EXISTS moment_text (
    id             serial PRIMARY KEY,
    moment_type_id integer NOT NULL REFERENCES moment_type(id) ON DELETE CASCADE,
    moment_tone_id integer NOT NULL REFERENCES moment_tone(id) ON DELETE CASCADE,
    titel          text,
    sms_text       text,
    body_text      text NOT NULL,
    is_active      boolean NOT NULL DEFAULT true,
    sort_order     integer NOT NULL DEFAULT 0,
    created_at     timestamp without time zone NOT NULL DEFAULT now(),
    created_by     text
);
CREATE INDEX IF NOT EXISTS ix_moment_text_combo ON moment_text (moment_type_id, moment_tone_id);

-- ── Seed: 7 Moment-Typen ────────────────────────────────────────────────
INSERT INTO moment_type (code, name, consent_category, sort_order, is_active) VALUES
    ('EmployeeBirthday',         'Geburtstag',                          'birthday',     1, true),
    ('WorkAnniversary',          'Arbeitsjubiläum',                     'birthday',     2, true),
    ('Appreciation',             'Danke / Wertschätzung',               'appreciation', 3, true),
    ('PromotionCongratulations', 'Gratulation (Beförderung / Ereignis)','appreciation', 4, true),
    ('WelcomeBackVacation',      'Willkommen zurück (Ferien)',          'care',         5, true),
    ('CareHeatNotice',           'Fürsorge-Hinweis (Hitze)',            'care',         6, true),
    ('WelcomeBackNeutral',       'Willkommen zurück (neutral)',         'care',         7, true)
ON CONFLICT (code) DO NOTHING;

-- ── Seed: Emotionsgrade ─────────────────────────────────────────────────
INSERT INTO moment_tone (code, name, sort_order, is_active) VALUES
    ('schlicht',    'Schlicht',        1, true),
    ('herzlich',    'Herzlich',        2, true),
    ('persoenlich', 'Sehr persönlich', 3, true),
    ('kurz',        'Kurz',            4, true)
ON CONFLICT (code) DO NOTHING;

-- ── Seed: Starter-Texte (nur wenn moment_text noch leer) ────────────────
INSERT INTO moment_text (moment_type_id, moment_tone_id, titel, sms_text, body_text, is_active, sort_order, created_at)
SELECT t.id, o.id, v.titel, v.sms, v.body, true, v.sort, now()
FROM (VALUES
  -- Geburtstag
  ('EmployeeBirthday','herzlich','Herzlichen Glückwunsch zum Geburtstag',
   'Hallo {Vorname}, wir denken heute an dich. Tippe auf den Link:',
   '{Anrede}

Herzlichen Glückwunsch zum Geburtstag! Wir wünschen dir alles Gute, viel Freude und einen schönen Tag.

{Absender}', 1),
  ('EmployeeBirthday','schlicht','Alles Gute zum Geburtstag',
   'Hallo {Vorname}, alles Gute zum Geburtstag. Tippe auf den Link:',
   'Hallo {Vorname}

Alles Gute zum Geburtstag. Wir wünschen dir einen schönen Tag.

{Absender}', 2),
  ('EmployeeBirthday','kurz','Happy Birthday',
   'Hallo {Vorname}, alles Gute! Tippe auf den Link:',
   '{Vorname}, alles Gute zum Geburtstag!

{Absender}', 3),
  -- Arbeitsjubiläum
  ('WorkAnniversary','herzlich','Herzlichen Glückwunsch zum Jubiläum',
   'Hallo {Vorname}, heute feiern wir dich. Tippe auf den Link:',
   '{Anrede}

Herzlichen Glückwunsch zu deinem Arbeitsjubiläum. Danke für deine Treue und deinen Einsatz — wir freuen uns, dich im Team zu haben.

{Absender}', 1),
  -- Danke / Wertschätzung
  ('Appreciation','herzlich','Danke',
   'Hallo {Vorname}, eine kurze Nachricht von uns für dich. Tippe auf den Link:',
   '{Anrede}

Danke für deinen tollen Einsatz. Wir schätzen deine Arbeit sehr.

{Absender}', 1),
  ('Appreciation','schlicht','Danke für deinen Einsatz',
   'Hallo {Vorname}, danke für deinen Einsatz. Tippe auf den Link:',
   'Hallo {Vorname}

Danke für deinen Einsatz. Wir wissen ihn zu schätzen.

{Absender}', 2),
  -- Gratulation
  ('PromotionCongratulations','herzlich','Herzliche Gratulation',
   'Hallo {Vorname}, wir gratulieren dir. Tippe auf den Link:',
   '{Anrede}

Herzliche Gratulation! Wir freuen uns sehr für dich und mit dir.

{Absender}', 1),
  -- Willkommen zurück (Ferien)
  ('WelcomeBackVacation','herzlich','Willkommen zurück',
   'Hallo {Vorname}, schön, dass du wieder da bist. Tippe auf den Link:',
   '{Anrede}

Schön, dass du wieder da bist. Wir hoffen, du hast dich gut erholt, und freuen uns, dich wieder im Team zu haben.

{Absender}', 1),
  -- Fürsorge-Hinweis (Hitze)
  ('CareHeatNotice','herzlich','Pass gut auf dich auf',
   'Hallo {Vorname}, ein kurzer Hinweis von uns. Tippe auf den Link:',
   '{Anrede}

Morgen wird es heiss — bitte trink genug und gönn dir genügend Pausen. Pass gut auf dich auf.

{Absender}', 1),
  -- Willkommen zurück (neutral)
  ('WelcomeBackNeutral','herzlich','Schön, dass du wieder da bist',
   'Hallo {Vorname}, schön, dass du wieder da bist. Tippe auf den Link:',
   '{Anrede}

Schön, dass du wieder da bist. Wir freuen uns, dich wieder im Team zu haben.

{Absender}', 1)
) AS v(type_code, tone_code, titel, sms, body, sort)
JOIN moment_type t ON t.code = v.type_code
JOIN moment_tone o ON o.code = v.tone_code
WHERE NOT EXISTS (SELECT 1 FROM moment_text);
