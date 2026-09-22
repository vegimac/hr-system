-- Funktion pro Vertragsabschnitt geprüft (Walter-Vorgabe 22.09.2026).
-- Steuert die Kontrollliste System → Kontrolle → «Funktionen prüfen»:
-- geprüfte Abschnitte erscheinen dort nicht mehr. Wer/wann steht im Audit-Log.
-- Läuft beim Start automatisch (Program.cs, Schema-Stand 19) — dieses Skript
-- ist die Kopie für TablePlus, falls man es von Hand nachziehen will.
-- BEIDE Datenbanken: hr_system_test UND hrsystem.
ALTER TABLE employment ADD COLUMN IF NOT EXISTS funktion_geprueft BOOLEAN NOT NULL DEFAULT false;
