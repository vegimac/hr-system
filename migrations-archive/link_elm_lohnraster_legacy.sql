-- ============================================================================
-- Einmaliges Mapping (Walter 17.08.2026): bestehende OneCrew-Lohnpositionen
-- den ELM-Lohnraster-Eintraegen zuordnen. Die OneCrew-Codes wurden bei der
-- Migration vereinfacht (10 statt 10.1, 2 statt 10.2 usw.) und decken sich
-- deshalb nicht mit den Raster-Codes — teils bedeutet dieselbe Nummer sogar
-- etwas anderes (OneCrew 60.2 = Taggeld 80% vs. Raster 60.2 = Karenz 88%).
-- Idempotent: nur unverlinkte Raster-Zeilen, nur vorhandene aktive
-- Lohnpositionen, keine Doppel-Zuordnung derselben Lohnposition.
-- Ausfuehrung: TablePlus, reiner Copy-Paste-Block.
-- ============================================================================
UPDATE elm_lohnraster r
SET verwendet_lohnposition_id = lp.id
FROM (VALUES
    ('10.1', '10'),    -- Festlohn
    ('10.2', '2'),     -- Festlohn Ferien                = Festlohn fuer bezogene Ferien
    ('10.3', '3'),     -- Festlohn fuer Feiertage        = Festlohn fuer bezogene Feiertage
    ('10.4', '4'),     -- Zusatzstd.                     = Zusatzstunden (MTP)
    ('50.1', '50'),    -- Ausbezahlte Feiertage
    ('60.2', '60'),    -- Taggeld Karenz (88%)           = Unfall (Karenzentschaedigung)
    ('60.3', '60.2'),  -- Versicherungstaggeld UVG (80%) = Unfall (Taggeld 80%)
    ('70.1', '70'),    -- Krankentaggeld Karenz (88%)    = Krankheit (Karenzentschaedigung)
    ('70.2', '70.2')   -- Versicherungstaggeld KTG (80%) = Krankheit (Taggeld 80%)
) AS m(raster_code, onecrew_code)
JOIN lohnposition lp ON lp.code = m.onecrew_code AND lp.is_active = true
WHERE r.code = m.raster_code
  AND r.verwendet_lohnposition_id IS NULL
  AND NOT EXISTS (SELECT 1 FROM elm_lohnraster x WHERE x.verwendet_lohnposition_id = lp.id);
