-- Unterschrift pro Benutzer (PNG/JPG-Bytes, optional transparenter Hintergrund).
-- Wird später beim Generieren von Formularen (QST-Anmeldung etc.) eingebettet.
ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS signature_png BYTEA NULL;

COMMENT ON COLUMN app_user.signature_png IS
    'Unterschrift als Bild (PNG/JPG, idealerweise transparenter Hintergrund). NULL = keine Unterschrift hinterlegt.';
