-- ──────────────────────────────────────────────────────────────────────
-- Walter-Vorgabe 29.07.2026: Telefonnummer am Familienmitglied
-- (v.a. Ehepartner — Notfall-/Kontaktnummer).
-- Format wie beim MA: +41 79 333 44 55 (Frontend normalisiert).
-- ──────────────────────────────────────────────────────────────────────

ALTER TABLE employee_family_member
    ADD COLUMN IF NOT EXISTS phone VARCHAR(50);
