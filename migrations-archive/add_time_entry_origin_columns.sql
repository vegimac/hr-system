-- Herkunftsfelder für Stempelzeiten (Walter-Vorgabe 21.06.2026)
-- In TablePlus ausführen.
-- Ein MA, der in mehreren Filialen stempelt, bekommt seine Stempel ALLE auf
-- seinen einen Lohn-MA (IsPayrollExcluded=false) gespeichert. Damit nachvoll-
-- ziehbar bleibt, IN WELCHER Filiale gestempelt wurde, halten wir die Herkunft:
--   easyatwork_customer_id     = easy@work-Customer-ID (Filiale) des Stempels
--   source_company_profile_id  = Cowork-CompanyProfile der Stempel-Filiale (sofern auflösbar)
-- Die Lohnberechnung liest weiterhin ausschliesslich nach employee_id.

ALTER TABLE employee_time_entry
    ADD COLUMN IF NOT EXISTS easyatwork_customer_id     integer,
    ADD COLUMN IF NOT EXISTS source_company_profile_id  integer;
