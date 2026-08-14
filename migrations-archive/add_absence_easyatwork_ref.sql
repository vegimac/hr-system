-- easy@work-Absenz-Sync (Walter-Vorgabe 14.08.2026): Upsert-Schlüssel an der
-- Absenz. «A{id}» = aus customers/{c}/absences, «O{id}» = aus off_times.
-- NULL = manuell oder aus Mirus importiert — solche Absenzen tastet der
-- Sync nie an. Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE absence ADD COLUMN IF NOT EXISTS easyatwork_ref text;
CREATE INDEX IF NOT EXISTS ix_absence_eaw_ref ON absence (easyatwork_ref)
    WHERE easyatwork_ref IS NOT NULL;
