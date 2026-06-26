-- Mitarbeiter-Hauptadresse vereinheitlichen:
-- employee.street enthält künftig Strasse + Hausnummer, employee.house_number wird entfernt.
-- Zusatzadressen (employee_address) und Filialadressen (company_profile) bleiben unverändert.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'employee' AND column_name = 'house_number'
    ) THEN
        UPDATE employee
        SET street = trim(both from concat_ws(' ', nullif(trim(street), ''), nullif(trim(house_number), '')))
        WHERE house_number IS NOT NULL
          AND trim(house_number) <> ''
          AND (
              street IS NULL OR trim(street) = ''
              OR right(trim(street), length(trim(house_number))) <> trim(house_number)
          );

        ALTER TABLE employee DROP COLUMN house_number;
    END IF;
END $$;
