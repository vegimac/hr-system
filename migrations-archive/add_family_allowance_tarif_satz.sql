-- Walter-Vorgabe 28.05.2026: Pro Kind erfasst Walter den KONKRETEN Tarif-Satz,
-- der für ein Zeitfenster gilt — z.B. „KZ Satz 1 AG vom 1.10.2012 - 30.9.2028",
-- danach „KZ Satz 2 ab 1.10.2028". Die Engine schaut pro Lohnperiode, welcher
-- Satz beim Kind aktiv ist, und holt den aktuell gültigen Wert aus dem
-- FAK-Tarif der Filiale (Systemtabelle).
--
-- Neue Spalte: tarif_satz_nr
--   1 = Satz 1 (jüngere Kinder)
--   2 = Satz 2 (z.B. ab 12 J. — kantonal verschieden)
--   NULL = Pauschal (GZ/AdoptZ — kein Satz, da Pauschalbetrag)
--   NULL = Alt-Daten (vor Umstellung — Engine fällt auf Alter-Heuristik zurück)
--
-- In TablePlus ausführen.

ALTER TABLE family_member_allowance
    ADD COLUMN IF NOT EXISTS tarif_satz_nr INTEGER;

COMMENT ON COLUMN family_member_allowance.tarif_satz_nr IS
    'Welcher Satz aus dem FAK-Tarif gilt: 1=Satz 1, 2=Satz 2 (z.B. ab 12J), NULL=Pauschal (GZ/AdoptZ) oder Alt-Daten.';

-- Kontrolle
SELECT COUNT(*) AS total, COUNT(tarif_satz_nr) AS with_satz
FROM family_member_allowance;
