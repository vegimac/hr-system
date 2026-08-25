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
