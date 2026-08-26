-- Walter-Vorgabe 25.08.2026: Notfallkontakt am Mitarbeiter (genau EINER).
-- Zwei Erfassungsarten: (a) Verknüpfung auf ein Familienmitglied (FK — Name/
-- Telefon werden live aus employee_family_member gelesen), (b) freie Person
-- (Name + Beziehung + Telefon, z.B. Schwester/Nachbar). Läuft zusätzlich
-- idempotent beim App-Start (Program.cs) — TablePlus-Ausführung optional.

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS notfall_family_member_id INTEGER,
    ADD COLUMN IF NOT EXISTS notfall_name             VARCHAR(150),
    ADD COLUMN IF NOT EXISTS notfall_beziehung        VARCHAR(100),
    ADD COLUMN IF NOT EXISTS notfall_telefon          VARCHAR(50);

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_employee_notfall_family_member') THEN
        ALTER TABLE employee
            ADD CONSTRAINT fk_employee_notfall_family_member
            FOREIGN KEY (notfall_family_member_id)
            REFERENCES employee_family_member(id) ON DELETE SET NULL;
    END IF;
END $$;

-- Walter 26.08.2026: easy@work führt Notfallkontakte (Mein Unternehmen →
-- Notfallkontakte; API customers/{c}/emergency_contacts, per Probe gefunden).
-- Herkunfts-Id für den Sync — manuelle Erfassung löscht sie (Handpflege gewinnt).
ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS notfall_easyatwork_id BIGINT;
