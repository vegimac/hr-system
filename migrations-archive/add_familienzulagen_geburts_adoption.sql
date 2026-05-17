ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS employee_id           INTEGER,
    ADD COLUMN IF NOT EXISTS must_change_password  BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS failed_login_count    INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS locked_until          TIMESTAMP;

ALTER TABLE app_user
    ADD CONSTRAINT IF NOT EXISTS fk_app_user_employee
    FOREIGN KEY (employee_id) REFERENCES employee(id) ON DELETE SET NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_app_user_employee
    ON app_user (employee_id) WHERE employee_id IS NOT NULL;

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS login_password_prefix VARCHAR(5);

UPDATE company_profile
   SET login_password_prefix = LEFT(city, 2)
 WHERE login_password_prefix IS NULL AND city IS NOT NULL AND LENGTH(city) >= 2;