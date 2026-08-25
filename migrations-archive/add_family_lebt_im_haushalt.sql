-- Walter-Vorgabe 25.08.2026: expliziter Haushalt-Status am Familienmitglied.
-- Bisher: «im selben Haushalt» = alternative_address_id IS NULL (Ableitung) —
-- es fehlte der Zustand «nicht im Haushalt, ohne erfasste Adresse» (z.B.
-- erwachsenes, ausgezogenes Kind). Neu 3 Zustände im Familien-Modal:
--   lebt_im_haushalt = TRUE                          → lebt beim MA
--   lebt_im_haushalt = FALSE + alternative_address_id → andere bekannte Adresse
--   lebt_im_haushalt = FALSE ohne Adresse             → nicht (mehr) im Haushalt
-- Läuft zusätzlich idempotent beim App-Start (Program.cs).

ALTER TABLE employee_family_member
    ADD COLUMN IF NOT EXISTS lebt_im_haushalt BOOLEAN NOT NULL DEFAULT TRUE;

UPDATE employee_family_member
   SET lebt_im_haushalt = FALSE
 WHERE alternative_address_id IS NOT NULL
   AND lebt_im_haushalt = TRUE;
