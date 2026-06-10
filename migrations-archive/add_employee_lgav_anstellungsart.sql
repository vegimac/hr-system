-- ════════════════════════════════════════════════════════════════════════
-- Walter-Vorgabe 07.06.2026: zwei neue MA-Stammdatenfelder im Anstellungs-Block.
--   • lgav_pflichtig (bool) — ✓ = MA zahlt im Lohnlauf den jährlichen L-GAV-
--     Beitrag (volle/halbe Höhe folgt in Stufe 2: ≤ 21 h/Wo. oder
--     < 6 Mt. Betriebszug. = halbe).
--   • teilzeit_unter_8h_woche (bool) — ✓ = MA zahlt KEINE NBU.
--
-- DB-Default = false bei beiden, damit bestehende MA nicht unbemerkt geändert
-- werden. Bei NEUanlage via Cowork-Code wird LgavPflichtig=true gesetzt (C#-
-- Property-Default), wie von Walter gewünscht („beim Erfassen LGAV-Häkchen
-- ein"). Walter pflegt die bestehenden MA über die UI selbst nach.
--
-- Lauf in TablePlus, dann ./deploy.sh
-- ════════════════════════════════════════════════════════════════════════

ALTER TABLE employee
    ADD COLUMN IF NOT EXISTS lgav_pflichtig boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS teilzeit_unter_8h_woche boolean NOT NULL DEFAULT false;

-- Sanity-Check
SELECT COUNT(*) AS total,
       SUM(CASE WHEN lgav_pflichtig THEN 1 ELSE 0 END)            AS lgav_true,
       SUM(CASE WHEN teilzeit_unter_8h_woche THEN 1 ELSE 0 END)   AS unter_8h_true
  FROM employee;
