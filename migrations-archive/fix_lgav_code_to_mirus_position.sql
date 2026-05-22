-- ====================================================================
-- Fix: LGAV-Lohnposition-Code an Mirus-Position angleichen (Walter 22.05.2026)
-- ====================================================================
-- Problem: Die LGAV-Lohnposition trug den Code "140". Der Fibu-Journal-
-- Generator verlinkt Lohnpos-Abzüge per Code → Kontoplan (lohn_konto_mapping),
-- und die Kontoplan-Position für LGAV ist 600.24 (Soll 1920 / Gegen 2023).
-- Code "140" traf KEINE Kontoplan-Zeile → alle LGAV-Abzugszeilen wurden
-- übersprungen, Durchlaufkonto 1920 ging um den LGAV-Betrag nicht auf.
--
-- Fix: lohnposition.code 140 → 600.24 (= Mirus Position.SubPos, wie das
-- Design es vorsieht: Lohnposition.Code == Mirus Position.SubPos).
-- LgavBeitragService.LgavCode ist parallel auf "600.24" gesetzt.
--
-- Kein Daten-Risiko: lohn_zulage referenziert die Lohnposition per FK (Id),
-- nicht per Code-String. Der Unique-Index IX_lohnposition_code bleibt frei,
-- weil 600.24 noch nicht als Lohnposition-Code existiert.
--
-- NACH dieser Migration + Deploy: bestehende Snapshots tragen noch "140" im
-- SlipJson → im Lohn-Modul (Definitiv-Tab, admin) "🔄 Codes nachtragen"
-- klicken, danach das Fibu-Journal neu erstellen. Konto 1920 geht dann auf
-- (minus der noch offenen AG-Beiträge + Ferien/Feiertag-Rückstellungen).
--
-- TablePlus: reinen Block ausführen.
-- ====================================================================

UPDATE lohnposition
SET    code = '600.24'
WHERE  code = '140';
