-- ====================================================================
-- Migration: Saldo-Vortrag — Lohnpositionen 901–906 anlegen
-- Ausführen mit:
--   psql -d hr_system -U postgres -f add_saldo_vortrag.sql
-- ====================================================================
--
-- Zweck:
--   Beim Wechsel vom alten System auf das neue muss pro MA einmalig
--   der Anfangs-Saldo erfasst werden (Zeit, Feiertag, Ferien-Tage,
--   Nacht-Stunden, Ferien-Geld, 13. ML). Diese Werte werden als
--   normale Lohnpositionen-Buchungen in der Migrations-Periode
--   abgelegt, sind im Lohnzettel sichtbar und liefern den Vormonat-
--   Saldo für die erste echte Lohnperiode.
--
--   Mid-Year-Korrekturen laufen weiterhin über die normalen
--   Zulagen-/Abzüge-Mechanismen — diese 6 Positionen sind
--   ausschliesslich für die einmalige Migrations-Erfassung gedacht.
--
-- Eigenschaften aller Vortrag-Lohnpositionen:
--   • Kategorie       = "Saldo-Vortrag"
--   • Typ             = ZULAGE (Vortrag kann positiv ODER negativ sein,
--                       das Vorzeichen wird im Betrag-Feld gespeichert)
--   • SV-Flags        = alle false  → AHV-neutral, kein BVG/KTG/QST
--   • Basis-Flags     = alle false  → fliesst nicht in Bemessungs-
--                       grundlagen (Feiertag, Ferien, 13. ML) ein
--   • DreizehnterML   = false       → kein automatisches Splitting
--   • NichtImVertrag  = true        → erscheint nicht im Arbeitsvertrag
--   • LohnausweisCode = NULL        → nicht auf Lohnausweis (es ist ja
--                       kein neuer Lohn, sondern eine Saldo-Eröffnung)
--
-- Idempotent: Falls die Codes 901–906 bereits existieren, wird nichts
-- doppelt eingefügt. Bestehende Konfiguration wird nicht überschrieben.
-- ====================================================================

BEGIN;

INSERT INTO lohnposition (
    code,
    bezeichnung,
    kategorie,
    typ,
    ahv_alv_pflichtig,
    nbuv_pflichtig,
    ktg_pflichtig,
    bvg_pflichtig,
    qst_pflichtig,
    lohnausweis_code,
    dreijehnter_ml_pflichtig,
    zaehlt_als_basis_feiertag,
    zaehlt_als_basis_ferien,
    zaehlt_als_basis_13ml,
    nicht_drucken_wenn_null,
    nicht_im_vertrag_drucken,
    bvg_auf_100_rechnen,
    position_13ml,
    zaehlt_fuer_tagessatz,
    sort_order,
    is_active,
    created_at
)
SELECT
    code,
    bezeichnung,
    'Saldo-Vortrag',     -- kategorie
    'ZULAGE',            -- typ (Vorzeichen kommt aus dem erfassten Betrag)
    false, false, false, false, false,  -- alle SV-Flags off
    NULL,                -- kein Lohnausweis-Code
    false,               -- kein 13.-ML-Splitting
    false, false, false, -- keine Basis-Flags
    true,                -- nicht_drucken_wenn_null  (= 0 oder leer ⇒ nicht drucken)
    true,                -- nicht_im_vertrag_drucken
    false,               -- bvg_auf_100_rechnen
    0,                   -- position_13ml
    false,               -- zaehlt_fuer_tagessatz  (Vortrag ist kein laufender Lohn)
    sort_order,
    true,
    now()
FROM (VALUES
    ('901', 'Vortrag Zeitsaldo (Stunden)',          901),
    ('902', 'Vortrag Feiertag-Saldo (Tage)',        902),
    ('903', 'Vortrag Ferien-Saldo (Tage)',          903),
    ('904', 'Vortrag Nacht-Saldo (Stunden)',        904),
    ('905', 'Vortrag Ferien-Geld-Saldo (CHF)',      905),
    ('906', 'Vortrag 13. Monatslohn-Saldo (CHF)',   906)
) AS v(code, bezeichnung, sort_order)
WHERE NOT EXISTS (
    SELECT 1 FROM lohnposition lp WHERE lp.code = v.code
);

-- Korrektur: Feiertag-Saldo ist in TAGEN (nicht Stunden), passend zum
-- PayrollSaldo.FeiertagTageSaldo-Modell. Falls 902 mit der alten
-- Bezeichnung "(Stunden)" bereits existiert, korrigieren.
UPDATE lohnposition
   SET bezeichnung = 'Vortrag Feiertag-Saldo (Tage)'
 WHERE code = '902'
   AND kategorie = 'Saldo-Vortrag'
   AND bezeichnung <> 'Vortrag Feiertag-Saldo (Tage)';

COMMIT;

-- Bestätigung anzeigen
SELECT code, bezeichnung, kategorie, typ, sort_order, is_active
FROM lohnposition
WHERE code IN ('901', '902', '903', '904', '905', '906')
ORDER BY code;
