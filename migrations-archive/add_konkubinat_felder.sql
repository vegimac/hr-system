-- Walter-Vorgabe 25.08.2026 (docs/konkubinat-qst-konzept.md):
-- Konkubinats-Logik für die QST (H1/A0) — zwei neue Felder am Familienmitglied:
--   ma_hat_hoeheres_einkommen    (Typ Konkubinatspartner): Hat der/die MA das
--                                höhere Bruttoeinkommen? NULL = Frage offen.
--   gemeinsames_kind_mit_partner (Typ Kind): Gemeinsames Kind mit dem
--                                Konkubinatspartner? NULL = Frage offen.
-- Neuer member_type-Wert «Konkubinatspartner» braucht KEIN Schema (freier String).
-- Läuft zusätzlich idempotent beim App-Start (Program.cs).

ALTER TABLE employee_family_member
    ADD COLUMN IF NOT EXISTS ma_hat_hoeheres_einkommen    BOOLEAN,
    ADD COLUMN IF NOT EXISTS gemeinsames_kind_mit_partner BOOLEAN;

-- Walter-Bug 25.08.2026: die Ur-Tabelle hat einen CHECK-Constraint mit der
-- alten member_type-Liste → «Konkubinatspartner» wurde von der DB abgelehnt
-- («Fehler beim Speichern»). Constraint entfernen (Typen validiert das UI):
DO $$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT conname FROM pg_constraint
              WHERE conrelid = 'employee_family_member'::regclass
                AND contype  = 'c'
                AND pg_get_constraintdef(oid) ILIKE '%member_type%'
    LOOP
        EXECUTE 'ALTER TABLE employee_family_member DROP CONSTRAINT ' || quote_ident(c.conname);
    END LOOP;
END $$;
