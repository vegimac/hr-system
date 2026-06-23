ALTER TABLE employment
    ADD COLUMN IF NOT EXISTS easyatwork_manual_override boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN employment.easyatwork_manual_override IS
    'Lokal gepflegter Vertrag/Lohn; easy@work-Sync darf diese Employment-Zeile nicht ueberschreiben.';
