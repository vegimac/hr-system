-- Ferienentschädigung 5 Wochen: 10.64 → 10.65 % (Walter-Vorgabe 06.08.2026, Mirus-Abgleich)
-- In TablePlus ausführen. Nur Filialen anfassen, die noch auf dem alten Wert stehen.
UPDATE company_profile
SET default_vacation_percent_5weeks = 10.65
WHERE default_vacation_percent_5weeks = 10.64;

-- Anzeigename der Lohnposition angleichen
UPDATE lohnposition
SET bezeichnung = 'Ferienentschädigung 10.65%'
WHERE code = '195.2' AND bezeichnung = 'Ferienentschädigung 10.64%';
