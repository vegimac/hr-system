-- =============================================================================
-- init_dev_schema.sql
-- Creates the base tables that Program.cs startup SQL expects to already exist.
-- Run once on a fresh hr_system database BEFORE starting the app.
-- Idempotent: uses IF NOT EXISTS throughout.
-- =============================================================================

-- ── Core lookup tables ──────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS nationality (
    id SERIAL PRIMARY KEY,
    code TEXT,
    is_active BOOLEAN DEFAULT true
);

CREATE TABLE IF NOT EXISTS permit_type (
    id SERIAL PRIMARY KEY,
    code TEXT,
    description TEXT,
    person_group TEXT,
    is_active BOOLEAN DEFAULT true
);

CREATE TABLE IF NOT EXISTS education_level (
    id SERIAL PRIMARY KEY,
    code TEXT,
    is_active BOOLEAN DEFAULT true
);

CREATE TABLE IF NOT EXISTS job_group (
    id SERIAL PRIMARY KEY,
    code TEXT,
    sort_order INTEGER,
    is_active BOOLEAN DEFAULT true,
    is_kader BOOLEAN,
    mirus_funktion_aliases TEXT
);

CREATE TABLE IF NOT EXISTS app_text (
    id SERIAL PRIMARY KEY,
    module TEXT,
    text_key TEXT,
    language_code TEXT,
    content TEXT,
    is_active BOOLEAN DEFAULT true
);

-- ── Company ─────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS company_profile (
    id SERIAL PRIMARY KEY,
    company_name TEXT,
    branch_name TEXT,
    restaurant_code TEXT,
    street TEXT,
    house_number TEXT,
    zip_code TEXT,
    city TEXT,
    country TEXT,
    phone TEXT,
    email TEXT,
    normal_weekly_hours NUMERIC,
    default_vacation_weeks NUMERIC,
    work_location TEXT,
    payroll_period_start_day INTEGER,
    max_part_time_hours_per_week NUMERIC,
    allow_first_3_months_8_percent_reduction BOOLEAN DEFAULT false,
    hold_back_vacation_payout BOOLEAN DEFAULT false,
    pdf_footer_text TEXT,
    notice_period_during_probation_days INTEGER,
    notice_period_after_probation_months INTEGER,
    notice_period_from_tenth_year_months INTEGER,
    minimum_wage_under_18_monthly NUMERIC,
    minimum_wage_under_18_hourly NUMERIC,
    selected_contract_template_id INTEGER,
    default_vacation_percent_5weeks NUMERIC,
    default_vacation_percent_6weeks NUMERIC,
    default_holiday_percent NUMERIC,
    night_start_time VARCHAR(5),
    night_end_time VARCHAR(5),
    thirteenth_month_payouts_per_year INTEGER DEFAULT 12,
    auto_ferien_geld_auszahlung_dezember BOOLEAN DEFAULT true,
    is_active BOOLEAN DEFAULT true,
    bur_nummer VARCHAR(20),
    uid_nummer VARCHAR(20),
    branchen_code VARCHAR(10),
    ahv_kasse VARCHAR(100),
    bvg_versicherer VARCHAR(100),
    gav_name VARCHAR(100),
    ist_gav BOOLEAN DEFAULT false,
    karenzjahr_basis VARCHAR(20) DEFAULT 'ARBEITSJAHR',
    karenz_tage_max NUMERIC(5,2) DEFAULT 14,
    karenz_tage_max_unfall NUMERIC(5,2) DEFAULT 2,
    bvg_wartefrist_monate INTEGER DEFAULT 3,
    lgav_aktiv BOOLEAN DEFAULT true,
    lgav_trigger_monat INTEGER DEFAULT 1,
    lgav_beitrag_voll NUMERIC(8,2) DEFAULT 99,
    lgav_beitrag_reduziert NUMERIC(8,2) DEFAULT 49.5
);

-- ── Employee ────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS employee (
    id SERIAL PRIMARY KEY,
    employee_number TEXT,
    salutation TEXT,
    gender TEXT,
    first_name TEXT,
    last_name TEXT,
    street TEXT,
    house_number TEXT,
    zip_code TEXT,
    city TEXT,
    country TEXT,
    canton_code VARCHAR(2),
    date_of_birth DATE,
    nationality TEXT,
    nationality_id INTEGER REFERENCES nationality(id),
    language_code TEXT,
    phone_mobile TEXT,
    email TEXT,
    entry_date DATE,
    exit_date DATE,
    permit_type_id INTEGER REFERENCES permit_type(id),
    permit_expiry_date DATE,
    quellensteuer_befreit_ab DATE,
    is_active BOOLEAN DEFAULT true,
    social_security_number VARCHAR(20),
    marital_status VARCHAR(40)
);

CREATE TABLE IF NOT EXISTS employment (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    company_profile_id INTEGER REFERENCES company_profile(id),
    employment_model TEXT,
    salary_type TEXT,
    contract_start_date DATE,
    contract_end_date DATE,
    job_title TEXT,
    contract_type TEXT,
    employment_percentage NUMERIC,
    weekly_hours NUMERIC,
    guaranteed_hours_per_week NUMERIC,
    monthly_salary_fte NUMERIC,
    monthly_salary NUMERIC,
    hourly_rate NUMERIC,
    vacation_percent NUMERIC,
    holiday_percent NUMERIC,
    thirteenth_salary_percent NUMERIC,
    vacation_payment_mode TEXT,
    probation_period_months INTEGER,
    probation_end_date DATE,
    is_active BOOLEAN DEFAULT true
);

-- ── Employee sub-tables ─────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS employee_education_history (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    education_level_id INTEGER REFERENCES education_level(id),
    valid_from DATE,
    valid_to DATE,
    note TEXT,
    is_active BOOLEAN DEFAULT true
);

CREATE TABLE IF NOT EXISTS minimum_wage_rule_new (
    id SERIAL PRIMARY KEY,
    job_group_code TEXT,
    employment_model_code TEXT,
    education_level_id INTEGER REFERENCES education_level(id),
    salary_type TEXT,
    amount NUMERIC,
    valid_from DATE,
    valid_to DATE,
    is_active BOOLEAN DEFAULT true
);

CREATE TABLE IF NOT EXISTS employee_import_snapshot (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    job_group_code TEXT,
    employment_model TEXT,
    contract_type TEXT,
    hourly_rate NUMERIC,
    monthly_salary_fte NUMERIC,
    monthly_salary NUMERIC,
    weekly_hours NUMERIC,
    employment_percentage NUMERIC(5,2),
    contract_end_date DATE,
    job_title TEXT,
    nationality_code TEXT,
    gender TEXT,
    imported_at TIMESTAMPTZ,
    is_active BOOLEAN DEFAULT true
);

CREATE TABLE IF NOT EXISTS contract_text (
    id SERIAL PRIMARY KEY,
    text_key VARCHAR(20) NOT NULL,
    contract_types VARCHAR(50) DEFAULT 'ALL',
    language_code VARCHAR(5) DEFAULT 'de',
    content TEXT NOT NULL,
    is_active BOOLEAN DEFAULT true,
    valid_from DATE,
    valid_to DATE
);
CREATE INDEX IF NOT EXISTS "IX_contract_text_key_lang" ON contract_text(text_key, language_code);

CREATE TABLE IF NOT EXISTS employee_family_member (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    member_type TEXT,
    gender TEXT,
    family_status TEXT,
    last_name TEXT,
    maiden_name TEXT,
    first_name TEXT,
    social_security_number TEXT,
    lives_in_switzerland BOOLEAN,
    date_of_birth DATE,
    date_of_death DATE,
    allowance_1_until DATE,
    allowance_2_until DATE,
    allowance_3_until DATE,
    alternative_address_id INTEGER,
    qst_deductible_from DATE,
    qst_deductible_until DATE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS employee_time_entry (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    entry_date DATE,
    time_in TIMESTAMP WITHOUT TIME ZONE,
    time_out TIMESTAMP WITHOUT TIME ZONE,
    comment TEXT,
    duration_hours NUMERIC(6,2),
    night_hours NUMERIC(6,2),
    total_hours NUMERIC(6,2),
    source VARCHAR(50) DEFAULT 'manual',
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ,
    original_time_in TIMESTAMP WITHOUT TIME ZONE,
    original_time_out TIMESTAMP WITHOUT TIME ZONE,
    original_comment TEXT,
    edited_by VARCHAR(100),
    edited_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS absence (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    absence_type VARCHAR(20),
    date_from DATE,
    date_to DATE,
    worked_days INTEGER,
    hours_credited NUMERIC(8,2),
    prozent NUMERIC(5,2) DEFAULT 100,
    notes TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

-- ── Lohn / Payroll tables ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS lohnposition (
    id SERIAL PRIMARY KEY,
    code VARCHAR(20),
    bezeichnung VARCHAR(150),
    kategorie VARCHAR(80),
    typ VARCHAR(10) DEFAULT 'ZULAGE',
    ahv_alv_pflichtig BOOLEAN DEFAULT true,
    nbuv_pflichtig BOOLEAN DEFAULT true,
    ktg_pflichtig BOOLEAN DEFAULT true,
    bvg_pflichtig BOOLEAN DEFAULT true,
    qst_pflichtig BOOLEAN DEFAULT true,
    lohnausweis_code VARCHAR(20),
    dreijehnter_ml_pflichtig BOOLEAN DEFAULT false,
    zaehlt_als_basis_feiertag BOOLEAN DEFAULT false,
    zaehlt_als_basis_ferien BOOLEAN DEFAULT false,
    zaehlt_als_basis_13ml BOOLEAN DEFAULT false,
    lohnausweisfeld VARCHAR(10),
    lohnausweis_kreuz BOOLEAN DEFAULT false,
    statistik_code VARCHAR(20),
    nicht_drucken_wenn_null BOOLEAN DEFAULT true,
    nicht_im_vertrag_drucken BOOLEAN DEFAULT false,
    bvg_auf_100_rechnen BOOLEAN DEFAULT false,
    position_13ml INTEGER DEFAULT 0,
    zaehlt_fuer_tagessatz BOOLEAN DEFAULT true,
    sort_order INTEGER DEFAULT 99,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_lohnposition_code" ON lohnposition(code);

CREATE TABLE IF NOT EXISTS lohn_zulag_typ (
    id SERIAL PRIMARY KEY,
    bezeichnung VARCHAR(100),
    typ VARCHAR(10) DEFAULT 'ZULAGE',
    sv_pflichtig BOOLEAN DEFAULT false,
    qst_pflichtig BOOLEAN DEFAULT false,
    lohnposition_code VARCHAR(20),
    sort_order INTEGER DEFAULT 99,
    aktiv BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS lohn_zulage (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    periode VARCHAR(7),
    lohnposition_id INTEGER REFERENCES lohnposition(id),
    betrag NUMERIC(10,2),
    bemerkung TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS "IX_lohn_zulage_emp_periode" ON lohn_zulage(employee_id, periode);

CREATE TABLE IF NOT EXISTS employee_recurring_wage (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    lohnposition_id INTEGER REFERENCES lohnposition(id),
    betrag NUMERIC(10,2),
    valid_from DATE,
    valid_to DATE,
    bemerkung TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_employee_recurring_wage_period ON employee_recurring_wage(employee_id, valid_from, valid_to);

CREATE TABLE IF NOT EXISTS employment_model_component (
    id SERIAL PRIMARY KEY,
    employment_model_code VARCHAR(10),
    lohnposition_id INTEGER REFERENCES lohnposition(id),
    rate NUMERIC(8,4),
    is_active BOOLEAN DEFAULT true,
    sort_order INTEGER DEFAULT 99,
    bemerkung TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_employment_model_component_model ON employment_model_component(employment_model_code, is_active, sort_order);
CREATE UNIQUE INDEX IF NOT EXISTS employment_model_component_unique ON employment_model_component(employment_model_code, lohnposition_id);

CREATE TABLE IF NOT EXISTS vertragstyp_lohnposition (
    id SERIAL PRIMARY KEY,
    vertragstyp_code VARCHAR(10),
    lohnposition_code VARCHAR(20),
    is_required BOOLEAN DEFAULT false,
    is_default_active BOOLEAN DEFAULT true,
    sort_order INTEGER DEFAULT 99,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_vertragstyp_lohnposition_unique" ON vertragstyp_lohnposition(vertragstyp_code, lohnposition_code);

-- ── Dokument-Management ─────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS dokument_kategorie (
    id SERIAL PRIMARY KEY,
    name TEXT,
    sort_order INTEGER DEFAULT 99,
    aktiv BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS dokument_typ (
    id SERIAL PRIMARY KEY,
    kategorie_id INTEGER,
    name TEXT,
    sort_order INTEGER DEFAULT 99,
    aktiv BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS employee_dokument (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER,
    dokument_typ_id INTEGER,
    branch_code TEXT,
    filename_original TEXT,
    filename_storage TEXT,
    mime_type TEXT,
    groesse_bytes BIGINT,
    bemerkung TEXT,
    gueltig_von DATE,
    gueltig_bis DATE,
    hochgeladen_von TEXT,
    hochgeladen_am TIMESTAMPTZ DEFAULT NOW()
);

-- ── Absenz-Typ ──────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS absenz_typ (
    id SERIAL PRIMARY KEY,
    code VARCHAR(20),
    bezeichnung VARCHAR(100),
    zeitgutschrift BOOLEAN DEFAULT true,
    gutschrift_modus VARCHAR(5),
    utp_auszahlung BOOLEAN DEFAULT false,
    reduziert_saldo VARCHAR(20),
    basis_stunden VARCHAR(10) DEFAULT 'BETRIEB',
    lohnposition_auszahlung_code VARCHAR(20),
    lohnposition_kuerzung_code VARCHAR(20),
    pattern VARCHAR(20) DEFAULT 'KEIN',
    sort_order INTEGER DEFAULT 99,
    aktiv BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_absenz_typ_code" ON absenz_typ(code);

-- ── Swiss Location (PLZ) ────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS swiss_location (
    id SERIAL PRIMARY KEY,
    plz4 VARCHAR(4),
    gemeindename VARCHAR(80),
    bfs_nr INTEGER,
    kantonskuerzel VARCHAR(2)
);
CREATE INDEX IF NOT EXISTS idx_swiss_location_plz ON swiss_location(plz4);
CREATE UNIQUE INDEX IF NOT EXISTS swiss_location_plz_bfs_unique ON swiss_location(plz4, bfs_nr);

-- ── Behörde / Lohnabtretung ─────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS behoerde (
    id SERIAL PRIMARY KEY,
    name VARCHAR(200),
    typ VARCHAR(30) DEFAULT 'BETREIBUNGSAMT',
    adresse1 VARCHAR(200),
    adresse2 VARCHAR(200),
    adresse3 VARCHAR(200),
    plz VARCHAR(10),
    ort VARCHAR(100),
    telefon VARCHAR(30),
    email VARCHAR(200),
    iban VARCHAR(34),
    qr_iban VARCHAR(34),
    bic VARCHAR(20),
    bank_name VARCHAR(100),
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS employee_lohn_assignment (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    behoerde_id INTEGER REFERENCES behoerde(id),
    bezeichnung VARCHAR(100),
    freigrenze NUMERIC(10,2),
    zielbetrag NUMERIC(10,2),
    bereits_abgezogen NUMERIC(10,2),
    valid_from DATE,
    valid_to DATE,
    referenz_amt VARCHAR(100),
    zahlungs_referenz VARCHAR(50),
    bemerkung TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_employee_lohn_assignment_period ON employee_lohn_assignment(employee_id, valid_from, valid_to);

-- ── Bank ────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS bank_master (
    iid VARCHAR(10) PRIMARY KEY,
    bic VARCHAR(15),
    name VARCHAR(200),
    ort VARCHAR(100),
    strasse VARCHAR(200),
    plz VARCHAR(10),
    imported_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS employee_bank_account (
    id SERIAL PRIMARY KEY,
    employee_id INTEGER REFERENCES employee(id),
    iban VARCHAR(34),
    bic VARCHAR(15),
    bank_name VARCHAR(200),
    kontoinhaber VARCHAR(200),
    zahlungsreferenz VARCHAR(50),
    bemerkung TEXT,
    is_hauptbank BOOLEAN DEFAULT true,
    aufteilung_typ VARCHAR(20) DEFAULT 'VOLL',
    aufteilung_wert NUMERIC(10,2),
    valid_from DATE,
    valid_to DATE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_emp_bank_period ON employee_bank_account(employee_id, valid_from, valid_to);
