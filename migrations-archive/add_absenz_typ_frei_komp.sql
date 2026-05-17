-- add_absenz_typ_frei_komp.sql
-- ---------------------------------------------------------------------------
-- Frei-Kompensation als Absenz-Typ.
-- Walter-Vorgabe 15.05.2026: der Dienstplan-Code „FK" steht für einen
-- bezahlten freien Tag, der aus bestehenden Plus-Stunden gespeist wird —
-- also eine Absenz OHNE zusätzliche Zeitgutschrift, die das Stunden-Saldo
-- über die normale Soll/Ist-Differenz reduziert.
--
-- Idempotent: INSERT läuft nur wenn der Code noch nicht existiert.
-- In TablePlus ausführen.
-- ---------------------------------------------------------------------------

INSERT INTO absenz_typ
    (code, bezeichnung, zeitgutschrift, gutschrift_modus, utp_auszahlung,
     basis_stunden, pattern, sort_order, aktiv, zwischenverdienst_kuerzel)
SELECT 'FREI_KOMP', 'Frei-Kompensation', false, NULL, false,
       'BETRIEB', 'KEIN', 35, true, NULL
WHERE NOT EXISTS (SELECT 1 FROM absenz_typ WHERE code = 'FREI_KOMP');

-- Kontrolle
SELECT id, code, bezeichnung, zeitgutschrift, gutschrift_modus,
       basis_stunden, sort_order, aktiv
FROM   absenz_typ
ORDER  BY sort_order, code;
