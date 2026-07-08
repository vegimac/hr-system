-- Vertragsmodell-Rename UTP → FLEX (Walter 08.07.2026)
--
-- FLEX ist der Begriff aus easy@work («Mutter der Daten»); «UTP» war der alte
-- interne/Mirus-Code. Code (Backend + Frontend) verwendet ab diesem Stand
-- durchgängig FLEX; Legacy-Wert «UTP» wird in den Mappern weiterhin als
-- Alias akzeptiert.
--
-- In TablePlus direkt ausführen. Program.cs führt dieselben UPDATEs beim
-- Start idempotent aus — diese Datei ist die manuelle Referenz.
--
-- ACHTUNG: absenz_typ.code = 'UTP' ist ein ANDERER Namensraum (Absenz-Typ)
-- und wird bewusst NICHT umbenannt.
--
-- Eingefrorene Test-Snapshots (payroll_snapshot.slip_json) bleiben
-- unangetastet — reine Testdaten, werden vor dem Go-Live zurückgesetzt.

UPDATE employment                 SET employment_model      = 'FLEX' WHERE employment_model      = 'UTP';
UPDATE minimum_wage_rule_new      SET employment_model_code = 'FLEX' WHERE employment_model_code = 'UTP';
UPDATE social_insurance_rate      SET employment_model_code = 'FLEX' WHERE employment_model_code = 'UTP';
UPDATE vertragstyp_lohnposition   SET vertragstyp_code      = 'FLEX' WHERE vertragstyp_code      = 'UTP';
UPDATE employment_model_component SET employment_model_code = 'FLEX' WHERE employment_model_code = 'UTP';
UPDATE contract_text              SET contract_types = REPLACE(contract_types, 'UTP', 'FLEX')
                                  WHERE contract_types LIKE '%UTP%';

-- Kontrolle: alle sechs Abfragen müssen 0 liefern.
SELECT count(*) AS employment_utp        FROM employment                 WHERE employment_model      = 'UTP';
SELECT count(*) AS minwage_utp           FROM minimum_wage_rule_new      WHERE employment_model_code = 'UTP';
SELECT count(*) AS sv_utp                FROM social_insurance_rate      WHERE employment_model_code = 'UTP';
SELECT count(*) AS vertragstyp_lp_utp    FROM vertragstyp_lohnposition   WHERE vertragstyp_code      = 'UTP';
SELECT count(*) AS model_component_utp   FROM employment_model_component WHERE employment_model_code = 'UTP';
SELECT count(*) AS contract_text_utp     FROM contract_text              WHERE contract_types LIKE '%UTP%';
