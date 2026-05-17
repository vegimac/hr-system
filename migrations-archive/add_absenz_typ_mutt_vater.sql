-- add_absenz_typ_mutt_vater.sql
-- ---------------------------------------------------------------------------
-- Mutter-/Vaterschaftsurlaub als kombinierter Absenz-Typ.
-- Walter-Vorgabe 15.05.2026: der Dienstplan-Code „MV" deckt beide Fälle ab,
-- darum EIN Typ (statt MUTTER + VATERSCHAFT getrennt).
--
-- Verhalten wie Krank: Zeitgutschrift Ja, 1/5 Arbeitstag, Basis Betrieb.
-- Pattern bleibt KEIN — die Auszahlung läuft separat über die EO-Erstattung
-- (Mutterschafts-/Vaterschaftsentschädigung); die Stunden-Gutschrift sorgt
-- nur für korrekte Saldi.
--
-- Idempotent: INSERT läuft nur wenn der Code noch nicht existiert.
-- In TablePlus ausführen.
-- ---------------------------------------------------------------------------

INSERT INTO absenz_typ
    (code, bezeichnung, zeitgutschrift, gutschrift_modus, utp_auszahlung,
     basis_stunden, pattern, sort_order, aktiv, zwischenverdienst_kuerzel)
SELECT 'MUTT_VATER', 'Mutter-/Vaterschaftsurlaub', true, '1/5', false,
       'BETRIEB', 'KEIN', 45, true, 'D'
WHERE NOT EXISTS (SELECT 1 FROM absenz_typ WHERE code = 'MUTT_VATER');

-- Kontrolle
SELECT id, code, bezeichnung, zeitgutschrift, gutschrift_modus,
       basis_stunden, sort_order, aktiv, zwischenverdienst_kuerzel
FROM   absenz_typ
ORDER  BY sort_order, code;
