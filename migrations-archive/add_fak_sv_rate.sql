-- ====================================================================
-- FAK (Familienausgleichskasse) als SV-Satz — nur AG (Walter 22.05.2026)
-- ====================================================================
-- FAK ist ein reiner Arbeitgeber-Beitrag (kein AN-Abzug), läuft auf der
-- AHV-Basis. Mirus bildet drei Altersklassen ab (0–17 befreit, 18–64 voll,
-- 65+ mit 1'400-Freibetrag) — unsere AHV-Basis macht das schon: unter 18 keine
-- AHV → keine FAK; 65+ → AHV-Freibetrag steckt bereits drin. Darum genügt EIN
-- FAK-Satz × AHV-Basis. Wert lt. GastroSocial/Mirus: AG 1.635 %, gültig ab 10.2024.
--
-- Die Lohn-Engine ÜBERSPRINGT diese Zeile als AN-Abzug (Rate=0 & rate_employer
-- gesetzt → AG-only) — sie erzeugt also KEINE Phantom-AN-Zeile im Lohnzettel.
-- Das Fibu-Journal bucht FAK = rate_employer × AHV-Basis → Position 501 (Soll
-- 4062 / Gegen 2070). Berührt Konto 1920 NICHT.
--
-- HINWEIS Kanton: FAK ist kantonal. Falls deine Filialen unterschiedliche
-- FAK-Sätze haben, muss FAK pro Filiale geführt werden (dann eigene Lösung) —
-- diese Zeile ist EIN globaler Satz für alle Filialen.
--
-- TablePlus: reinen Block ausführen. Idempotent (WHERE NOT EXISTS).
-- ====================================================================

INSERT INTO social_insurance_rate
    (code, name, rate, rate_employer, basis_type, fibu_position, valid_from, is_active, sort_order)
SELECT 'FAK', 'Familienausgleichskasse (FAK)', 0, 1.635, 'gross', 501, DATE '2024-10-01', true, 95
WHERE NOT EXISTS (SELECT 1 FROM social_insurance_rate WHERE code = 'FAK');
