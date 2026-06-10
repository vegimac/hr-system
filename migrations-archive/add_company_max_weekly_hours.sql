-- Walter-Vorgabe 24.05.2026: Max. gestempelte Stunden pro Woche (Mo–So) pro Filiale.
-- Reine Anzeige-/Warngrenze im Stempelzeiten-Tab (rote Warnung wenn das Wochentotal
-- diesen Wert uebersteigt). NULL = keine Grenze / keine Warnung.
-- Ausfuehren in TablePlus.

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS max_weekly_hours numeric(5,2) NULL;
