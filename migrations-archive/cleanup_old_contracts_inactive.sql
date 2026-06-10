-- Walter-Vorgabe 28.05.2026: Alte Vertraege auf inaktiv setzen.
-- Vertraege mit Enddatum VOR dem 1.1.2026 sind klar abgelaufen, das
-- IsActive-Flag steht aber bei vielen noch auf true (haengt aus alten
-- Datenimporten, vor Einfuehrung der Versionierungs-Logik). Dadurch
-- griff der Mindestlohn-Check auf alte Vertraege zu und meldete
-- falsche „Mindestlohn unterschritten"-Alerts.
--
-- In TablePlus ausfuehren.

-- Vorher zaehlen: wie viele werden geaendert?
SELECT COUNT(*) AS to_be_deactivated
FROM employment
WHERE is_active = TRUE
  AND contract_end_date IS NOT NULL
  AND contract_end_date < DATE '2026-01-01';

-- Update
UPDATE employment
SET is_active = FALSE
WHERE is_active = TRUE
  AND contract_end_date IS NOT NULL
  AND contract_end_date < DATE '2026-01-01';

-- Nachher zur Kontrolle: wie viele aktive Vertraege gibt es jetzt noch?
SELECT COUNT(*) AS still_active
FROM employment
WHERE is_active = TRUE;
