-- Walter-Vorgabe 28.05.2026: Sicherstellen, dass die Familienzulagen-
-- Lohnpositionen (190.1 Kinderzulage / 190.2 Ausbildungszulage / 190.3
-- Geburts-+Adoptionszulage) QST-pflichtig sind. Falls die Spalte
-- qst_pflichtig versehentlich auf false stand, wurden die Familienzulagen
-- nicht in die Quellensteuer-Bemessungsgrundlage aufgenommen — der Lohnzettel
-- zeigte dann z.B. AHV-Basis = QST-Basis statt QST-Basis = AHV-Basis + FamZ.
--
-- In TablePlus ausführen.

-- 1) Aktuelle Werte ansehen
SELECT code, bezeichnung, qst_pflichtig
FROM lohnposition
WHERE code IN ('190.1', '190.2', '190.3')
ORDER BY code;

-- 2) Auf TRUE setzen wenn nötig
UPDATE lohnposition
SET qst_pflichtig = TRUE
WHERE code IN ('190.1', '190.2', '190.3')
  AND (qst_pflichtig = FALSE OR qst_pflichtig IS NULL);

-- 3) Kontrolle
SELECT code, bezeichnung, qst_pflichtig
FROM lohnposition
WHERE code IN ('190.1', '190.2', '190.3')
ORDER BY code;
