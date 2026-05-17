-- add_absenz_typ_bez_absenz.sql
-- ---------------------------------------------------------------------------
-- Bezahlte Absenz als neuer Absenz-Typ.
-- Walter-Vorgabe 15.05.2026: der Dienstplan-Code „ZF" steht für eine
-- bezahlte Absenz mit voller Zeitgutschrift 1/5 wie ein Krankheitstag —
-- sinnvoll für Arzt-/Behördentermine, Trauertag, Hochzeit etc., wo der Tag
-- voll zählen soll, ohne KTG-/Karenz-Mechanik wie bei Krankheit.
--
-- Konfig spiegelt KRANK ohne Karenz/KTG:
--   Zeitgutschrift = true, Modus = 1/5, Basis = Betrieb
--   UtpAuszahlung  = false (analog KRANK)
--   Pattern        = KEIN  (Festlohn deckt's bei FIX/MTP, keine Lohnposition)
--
-- Idempotent: INSERT läuft nur wenn der Code noch nicht existiert.
-- In TablePlus ausführen.
-- ---------------------------------------------------------------------------

INSERT INTO absenz_typ
    (code, bezeichnung, zeitgutschrift, gutschrift_modus, utp_auszahlung,
     basis_stunden, pattern, sort_order, aktiv, zwischenverdienst_kuerzel)
SELECT 'BEZ_ABSENZ', 'Bezahlte Absenz', true, '1/5', false,
       'BETRIEB', 'KEIN', 37, true, NULL
WHERE NOT EXISTS (SELECT 1 FROM absenz_typ WHERE code = 'BEZ_ABSENZ');

-- Kontrolle
SELECT id, code, bezeichnung, zeitgutschrift, gutschrift_modus,
       basis_stunden, sort_order, aktiv
FROM   absenz_typ
ORDER  BY sort_order, code;
