-- add_vertrag_link_moment_text.sql (Walter-Vorgabe 07.07.2026)
-- Eigener Moment-Typ „Arbeitsvertrag-Link" (Code VERTRAG_LINK) + pflegbare SMS-Vorlage.
-- ContractShareController.Create nutzt diese Vorlage (Platzhalter {Vorname}/{Firma}/{Link}/{GueltigBis})
-- statt des fest verdrahteten Texts; Fallback bleibt der bisherige Text, falls keine aktive Vorlage.
-- Idempotent: Typ nur anlegen wenn Code fehlt; Ton NEUTRAL nur wenn Tone-Tabelle leer; Text nur wenn
-- für den Typ noch keiner existiert. Program.cs seedet dasselbe beim Startup zusätzlich.
-- In TablePlus ausführen.

-- 1) Moment-Typ (consent_category = 'appreciation' erfüllt NOT NULL + die UI-Validierung;
--    VERTRAG_LINK wird nie als echter Moment erstellt, nur vom ContractShare genutzt).
INSERT INTO moment_type (code, name, description, consent_category, sort_order, is_active)
VALUES ('VERTRAG_LINK', 'Arbeitsvertrag-Link',
        'SMS-Vorlage für den öffentlichen Vertrags-Link (ContractShare). Platzhalter: {Vorname}, {Firma}, {Link}, {GueltigBis}',
        'appreciation', 8, true)
ON CONFLICT (code) DO NOTHING;

-- 2) Mindestens ein Emotionsgrad muss existieren; falls die Tabelle leer ist, einen neutralen Ton anlegen.
INSERT INTO moment_tone (code, name, description, sort_order, is_active)
SELECT 'NEUTRAL', 'Neutral', 'Neutraler Ton', 0, true
WHERE NOT EXISTS (SELECT 1 FROM moment_tone);

-- 3) SMS-Vorlage für VERTRAG_LINK — nur wenn für diesen Typ noch kein Text existiert.
--    Ton: bevorzugt 'Calm', sonst der erste vorhandene (kleinste sort_order/id).
INSERT INTO moment_text (moment_type_id, moment_tone_id, titel, sms_text, body_text,
                         is_active, sort_order, language_code, version, requires_review, created_at)
SELECT
    (SELECT id FROM moment_type WHERE code = 'VERTRAG_LINK'),
    COALESCE(
        (SELECT id FROM moment_tone WHERE code = 'Calm'),
        (SELECT id FROM moment_tone ORDER BY sort_order, id LIMIT 1)
    ),
    'Arbeitsvertrag-Link',
    'Hallo {Vorname}, hier ist dein Arbeitsvertrag bei {Firma}: {Link}',
    'Vorlage für den SMS-Text des öffentlichen Vertrags-Links. Platzhalter: {Vorname}, {Firma}, {Link}, {GueltigBis}.',
    true, 0, 'de', '1.0', false, now()
WHERE EXISTS (SELECT 1 FROM moment_type WHERE code = 'VERTRAG_LINK')
  AND EXISTS (SELECT 1 FROM moment_tone)
  AND NOT EXISTS (
      SELECT 1 FROM moment_text mt
      JOIN moment_type ty ON ty.id = mt.moment_type_id
      WHERE ty.code = 'VERTRAG_LINK'
  );
