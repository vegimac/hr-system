-- MA-Postfach: reine Text-Mitteilung ohne Datei (Walter-Vorgabe 23.05.2026)
-- Ermöglicht eine kurze Nachricht ins Mitarbeiter-Postfach (z.B. Lohnanpassung),
-- ohne dass ein PDF/Bild hochgeladen werden muss. StorageFilename bleibt leer,
-- MimeType NULL, message_body trägt den Text; OriginalFilename = Titel.
-- In TablePlus ausführen.

ALTER TABLE mailbox_document
    ADD COLUMN IF NOT EXISTS message_body text;
