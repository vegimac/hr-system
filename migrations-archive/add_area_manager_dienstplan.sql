-- Bereich «Manager-Dienstplan» nachtragen (Walter-Vorgabe 12.08.2026):
-- die Dashboard-Kachel existierte, wurde aber bei Usern mit eigener
-- Bereichs-Auswahl (allowed_areas) ausgeblendet, weil der Schlüssel
-- 'manager-dienstplan' zum Zeitpunkt ihrer Auswahl noch nicht existierte.
-- Nachtrag = Kachel/Menüpunkt erscheint; abwählbar bleibt der Bereich
-- danach normal über die Benutzerverwaltung.
-- Ausführen in TablePlus:

UPDATE app_user
SET allowed_areas = allowed_areas || ',manager-dienstplan'
WHERE allowed_areas IS NOT NULL
  AND allowed_areas <> ''
  AND allowed_areas NOT LIKE '%manager-dienstplan%';
