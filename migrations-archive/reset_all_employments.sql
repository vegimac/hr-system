-- VERTRAGS-RESET (Walter-Vorgabe 08.07.2026) — sauberer Ausgangspunkt.
--
-- Kontext: Testphase, keine produktiven Löhne. Die Fehl-Importe der letzten
-- Wochen haben Vertrags-Splitter hinterlassen; statt weiter zu flicken wird
-- die Vertragstabelle geleert und per Massen-Sync (Strict-Modus) frisch aus
-- easy@work aufgebaut.
--
-- ACHTUNG:
--   • Löscht ALLE Verträge — auch die manuell erfassten (z.B. vertrauliche
--     GF-Löhne/FIX-M). Diese danach im MA-Detail → Vertrags-Leiste →
--     «Bearbeiten» neu erfassen (der Sync legt den FIX-M-Vertrag ohne Lohn
--     wieder an; nur der Lohnbetrag muss neu rein).
--   • Probezeit-Historie hängt per CASCADE an den Verträgen (wird mitgelöscht).
--   • Offene Vertrags-Links (SMS) zeigen auf gelöschte Verträge → widerrufen.
--
-- In TablePlus ausführen. Danach: Systemeinstellungen → easy@work-API →
-- Mitarbeiter-Stammdaten-Sync pro Filiale → Vorschau (CONFLICTs zuerst in
-- easy@work bereinigen!) → Importieren.

-- Offene Vertrags-Links entwerten (zeigen sonst auf gelöschte Verträge):
UPDATE contract_share_token SET revoked_at = now() WHERE revoked_at IS NULL;

-- Alle Verträge löschen (employment_probation_log löscht per CASCADE mit):
DELETE FROM employment;

-- Kontrolle — beide müssen 0 liefern:
SELECT count(*) AS vertraege        FROM employment;
SELECT count(*) AS probation_log    FROM employment_probation_log;
