-- OBSOLET (Walter-Entscheid 06.08.2026): NICHT AUSFÜHREN.
-- Die alten SSL-Nummern in company_profile_ssl sind evtl. falsch erfasst —
-- massgebend sind ausschliesslich die von Hand erfassten Lohndatenempfänger.
-- Datei bleibt nur als Doku der verworfenen Migration erhalten.
--
-- 1) Pro Kanton aus company_profile_ssl einen QST-Empfänger im Katalog
--    sicherstellen (falls Walter ihn nicht schon selbst erfasst hat).
INSERT INTO lohndaten_empfaenger (art, bezeichnung, kanton_code)
SELECT DISTINCT 'QST', 'Steuerverwaltung Kanton ' || s.kanton_code, s.kanton_code
FROM company_profile_ssl s
WHERE NOT EXISTS (
    SELECT 1 FROM lohndaten_empfaenger e
    WHERE e.art = 'QST' AND e.kanton_code = s.kanton_code
);

-- 2) Zuordnung pro Filiale: SSL-Nummer als Mitgliednummer übernehmen.
--    Nur wo für (Filiale, Kanton) noch KEINE QST-Zuordnung existiert.
INSERT INTO company_profile_empfaenger (company_profile_id, empfaenger_id, mitgliednummer, bemerkung)
SELECT s.company_profile_id, e.id, s.ssl_nummer, s.bemerkung
FROM company_profile_ssl s
JOIN lohndaten_empfaenger e
  ON e.art = 'QST' AND e.kanton_code = s.kanton_code
WHERE NOT EXISTS (
    SELECT 1
    FROM company_profile_empfaenger z
    JOIN lohndaten_empfaenger e2 ON e2.id = z.empfaenger_id
    WHERE z.company_profile_id = s.company_profile_id
      AND e2.art = 'QST' AND e2.kanton_code = s.kanton_code
);

-- Kontrolle: Zuordnungen pro Filiale/Kanton mit SSL
SELECT cp.restaurant_code, e.kanton_code, z.mitgliednummer AS ssl, e.bezeichnung
FROM company_profile_empfaenger z
JOIN lohndaten_empfaenger e ON e.id = z.empfaenger_id AND e.art = 'QST'
JOIN company_profile cp ON cp.id = z.company_profile_id
ORDER BY cp.restaurant_code, e.kanton_code;
