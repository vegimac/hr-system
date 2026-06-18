-- ════════════════════════════════════════════════════════════════════════
-- easy@work API Integration — Phase 1 Foundation
-- Walter-Vorgabe 17.06.2026
--
-- Zwei Tabellen:
--   easyatwork_branch_mapping  — CompanyProfile ↔ easy@work-Customer-ID
--   easyatwork_sync_state      — pro Filiale + Resource-Typ: bis wann gesynct
-- ════════════════════════════════════════════════════════════════════════

BEGIN;

CREATE TABLE IF NOT EXISTS easyatwork_branch_mapping (
    id                          SERIAL PRIMARY KEY,
    company_profile_id          INTEGER NOT NULL
        REFERENCES company_profile(id) ON DELETE CASCADE,
    easyatwork_customer_id      INTEGER NOT NULL,
    easyatwork_customer_number  VARCHAR(64),
    easyatwork_customer_name    VARCHAR(256),
    created_at                  TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at                  TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Eine Filiale = ein Mapping. Eine Customer-ID = eine Filiale.
CREATE UNIQUE INDEX IF NOT EXISTS ux_easyatwork_branch_mapping_cp
    ON easyatwork_branch_mapping (company_profile_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_easyatwork_branch_mapping_cust
    ON easyatwork_branch_mapping (easyatwork_customer_id);

CREATE TABLE IF NOT EXISTS easyatwork_sync_state (
    id                       SERIAL PRIMARY KEY,
    company_profile_id       INTEGER NOT NULL
        REFERENCES company_profile(id) ON DELETE CASCADE,
    resource                 VARCHAR(32) NOT NULL,
    last_sync_at             TIMESTAMP,
    last_seen_updated_at     TIMESTAMP,
    last_row_count           INTEGER,
    last_error               TEXT
);

-- Pro (Filiale, Resource) genau eine State-Zeile.
CREATE UNIQUE INDEX IF NOT EXISTS ux_easyatwork_sync_state_cp_res
    ON easyatwork_sync_state (company_profile_id, resource);

COMMIT;
