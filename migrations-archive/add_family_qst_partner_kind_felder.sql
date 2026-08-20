-- Walter-Vorgabe 20.08.2026: QST Partner+Kinder-Paket.
-- Ehepartner: Erwerbstätig-Frage (NULL = offen → blockt Lohnlauf bei
-- QST-pflichtigen verheirateten MA) + Arbeitgeber KOMPLETT wie auf dem
-- QST-Anmeldeformular (Name, Strasse/Nr., PLZ/Ort/Kanton, Stellenantritt).
-- Kind: «in Erstausbildung» (Kinderziffer über den 18. Geburtstag hinaus,
-- KS 45 Ziff. 3.2.2 — sonst endet der Abzug automatisch mit 18).
-- Läuft auch idempotent beim Server-Start (Program.cs).

ALTER TABLE employee_family_member
    ADD COLUMN IF NOT EXISTS erwerbstaetig       BOOLEAN,
    ADD COLUMN IF NOT EXISTS arbeitgeber_name    VARCHAR(150),
    ADD COLUMN IF NOT EXISTS arbeitgeber_strasse VARCHAR(150),
    ADD COLUMN IF NOT EXISTS arbeitgeber_plz     VARCHAR(10),
    ADD COLUMN IF NOT EXISTS arbeitgeber_ort     VARCHAR(120),
    ADD COLUMN IF NOT EXISTS arbeitgeber_kanton  VARCHAR(10),
    ADD COLUMN IF NOT EXISTS stellenantritt      DATE,
    ADD COLUMN IF NOT EXISTS in_erstausbildung   BOOLEAN NOT NULL DEFAULT FALSE;
