-- Swissdec E3: Stammdaten Rechtseinheit (Walter 28.08.2026)
-- EINE Zeile für die Meldeeinheit — UID, Ausgleichskasse, FAK,
-- Versicherer-/Vertragsnummern UVG/UVGZ/KTG/BVG.
-- Läuft auch idempotent beim App-Start (Program.cs); dieses File ist die
-- TablePlus-Kopie für manuelle Ausführung.

CREATE TABLE IF NOT EXISTS elm_stammdaten (
    id                     serial PRIMARY KEY,
    uid                    varchar(20),
    ak_name                varchar(120),
    ak_kassen_nummer       varchar(40),
    ak_abrechnungs_nummer  varchar(40),
    fak_kassen_nummer      varchar(40),
    fak_abrechnungs_nummer varchar(40),
    uvg_versicherer        varchar(120),
    uvg_kunden_nummer      varchar(40),
    uvg_vertrags_nummer    varchar(40),
    uvgz_versicherer       varchar(120),
    uvgz_kunden_nummer     varchar(40),
    uvgz_vertrags_nummer   varchar(40),
    ktg_versicherer        varchar(120),
    ktg_kunden_nummer      varchar(40),
    ktg_vertrags_nummer    varchar(40),
    bvg_versicherer        varchar(120),
    bvg_kunden_nummer      varchar(40),
    bvg_vertrags_nummer    varchar(40),
    updated_at             timestamp without time zone NOT NULL DEFAULT now(),
    updated_by             varchar(150)
);

-- Nachtrag 28.08.2026: Versicherer-Nummern (Swissdec-Adressierung, z.B. Swica «S122»)
-- + UID/versichert-seit der Versicherer (AHV-Meldeblock). Idempotent.
ALTER TABLE elm_stammdaten ADD COLUMN IF NOT EXISTS uvg_versicherer_nummer varchar(40);
ALTER TABLE elm_stammdaten ADD COLUMN IF NOT EXISTS uvgz_versicherer_nummer varchar(40);
ALTER TABLE elm_stammdaten ADD COLUMN IF NOT EXISTS ktg_versicherer_nummer varchar(40);
ALTER TABLE elm_stammdaten ADD COLUMN IF NOT EXISTS bvg_versicherer_nummer varchar(40);
ALTER TABLE elm_stammdaten ADD COLUMN IF NOT EXISTS uvg_uid varchar(20);
ALTER TABLE elm_stammdaten ADD COLUMN IF NOT EXISTS uvg_versichert_seit date;
ALTER TABLE elm_stammdaten ADD COLUMN IF NOT EXISTS bvg_uid varchar(20);
ALTER TABLE elm_stammdaten ADD COLUMN IF NOT EXISTS bvg_versichert_seit date;
