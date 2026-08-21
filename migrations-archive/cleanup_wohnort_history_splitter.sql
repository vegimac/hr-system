-- Walter-Vorgabe 20.08.2026: Altbestand-Bereinigung Wohnort-Historie.
-- Unbestätigte (datum_offen) Sync-Zwischenstände löschen, wenn ein NEUERER
-- unbestätigter Eintrag desselben MA existiert — nur der jüngste provisorische
-- Stand bleibt (der wird im Umzugs-Dialog bestätigt). Bestätigte Einträge
-- (datum_offen = false) bleiben unangetastet.
-- Einmalig in TablePlus ausführen; der Sync erzeugt solche Splitter ab sofort
-- nicht mehr (Zwischenstands-Bereinigung in ErfasseWohnortWechselAsync).

DELETE FROM employee_wohnort_history h
WHERE h.datum_offen = true
  AND EXISTS (
      SELECT 1 FROM employee_wohnort_history h2
      WHERE h2.employee_id = h.employee_id
        AND h2.datum_offen = true
        AND h2.id > h.id
  );
