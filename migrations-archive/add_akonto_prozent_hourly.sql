-- add_akonto_prozent_hourly.sql
-- ---------------------------------------------------------------------------
-- Walter 16.05.2026 — Etappe 5a der Akonto-Regelwerk-Umstellung.
--
-- Ergänzt das CompanyProfile um den Akonto-Prozentsatz für Stundenlöhner
-- (UTP/MTP). Bisher existiert nur `akonto_prozent_fix` (für FIX/FIX-M).
-- Mit dem neuen Regelwerk:
--   • FIX / FIX-M : AkontoProzentFix    × Definitiv-Auszahlung
--   • UTP / MTP   : AkontoProzentHourly × (Stunden × Rate + Ferien-Pott − Abzüge)
--
-- Default 100 % = aktuelles Verhalten (voller Anspruch wird ausbezahlt).
-- Walter kann pro Filiale konservativer einstellen (z.B. 95 %) falls er
-- einen Sicherheitspuffer will.
--
-- Idempotent, in TablePlus ausführen.
-- ---------------------------------------------------------------------------

ALTER TABLE company_profile
    ADD COLUMN IF NOT EXISTS akonto_prozent_hourly NUMERIC(5,2) NOT NULL DEFAULT 100;

COMMENT ON COLUMN company_profile.akonto_prozent_hourly IS
    'Akonto-Prozentsatz für UTP/MTP-MA (Stundenlöhner). Default 100 = voller Anspruch. '
    'Wirkt auf: (Stempelzeit-Brutto + Ferien-Pott − SV-Abzüge) × Prozent / 100, abgerundet auf CHF 10.';

-- Kontrolle: beide Akonto-Spalten anzeigen
SELECT column_name, data_type, column_default, is_nullable
FROM   information_schema.columns
WHERE  table_name = 'company_profile'
  AND  column_name LIKE 'akonto_prozent%'
ORDER  BY column_name;
