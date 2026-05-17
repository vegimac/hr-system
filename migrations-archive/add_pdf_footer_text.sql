-- Lohnabrechnung-PDF: pro-Filiale Fussnoten-Text (Bemerkungen am Ende)
ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS pdf_footer_text text;

-- Per-Periode Bemerkungstext (überschreibt Filial-Default)
ALTER TABLE payroll_periode ADD COLUMN IF NOT EXISTS pdf_footer_text text;

-- Default-Text auf Filial-Ebene setzen (kann pro Filiale später angepasst werden).
-- L-GAV Art. 12 Ziff. 2 (13. Monatslohn / Probezeit-Auflösung)
UPDATE company_profile
SET pdf_footer_text = 'Gemäss Art. 12 Ziffer 2 L-GAV entfällt der anteilsmässige Anspruch auf den 13. Monatslohn, wenn ein Arbeitsverhältnis im Rahmen der Probezeit aufgelöst wird.'
WHERE pdf_footer_text IS NULL OR pdf_footer_text = '';
