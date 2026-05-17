-- Familienangehörige: Bewilligung + ZEMIS + Nationalität ergänzen.
-- Pendant zu den gleichnamigen Feldern auf employee.
-- Walter-Anwendungsfall: Ehepartner und Kinder mit eigenem Ausländerausweis
-- (z.B. C-Bewilligung mit GA-Nummer + Ablaufdatum + AFG-Nationalität).
ALTER TABLE employee_family_member
    ADD COLUMN IF NOT EXISTS permit_type_id      INT          NULL REFERENCES permit_type(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS permit_expiry_date  DATE         NULL,
    ADD COLUMN IF NOT EXISTS zemis_number        VARCHAR(40)  NULL,
    ADD COLUMN IF NOT EXISTS nationality_id      INT          NULL REFERENCES nationality(id) ON DELETE SET NULL;
