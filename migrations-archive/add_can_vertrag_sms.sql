-- Vertrags-SMS nur für ausgewählte Benutzer (Walter-Vorgabe 10.08.2026):
-- Häkchen pro Benutzer+Filiale (Pflege im Filial-Tab «Unterzeichner»).
-- admin/superuser dürfen immer; Rolle user braucht das Häkchen für die
-- Filiale des Vertrags. Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE user_branch_access
    ADD COLUMN IF NOT EXISTS can_vertrag_sms boolean NOT NULL DEFAULT false;
