# Swissdec ELM System Tests (Tech) — Prüfliste

Stand 05.11.2026. Grundlage: Liste aus dem Quality Tool (45 Prüfpunkte, Testlauf ohne Zertifizierungswirkung) und `docs/swissdec-systemtest-katalog.json`.

**Wichtig:** Alles unten ist mit Unit-Tests geprüft, aber noch nicht gegen den Refapps-Empfänger gesendet. Die Bewertung macht das Quality Tool oder der Fachexperte.

Seite für alle Punkte, ausser wo anders genannt: System → **Lohnmeldungen (ELM)** (`page-elm-schnittstelle`). Das Cockpit `page-elm` zeigt dieselben Angaben.

| Test | Prüfpunkte | Was OneCrew zeigt | Stand |
|---|---|---|---|
| 01_01, 01_03 Jahres-/Monatsmeldung | DECLARE, SYNCHRONIZE | Karte «Meldung senden», danach Zeile mit Stand je Empfänger | war da |
| 01_02, 01_04 mit SwissdecFault | FAULT_DISPLAY, ABORT | Roter Hinweis: «Übermittlung abgebrochen …», alle Fehler mit Code, nächster Schritt. Es entsteht keine Meldung in der Liste. | neu |
| 02_01 bis 02_06 Synchronisierung | SYNCHRONIZE, RECEIVED_STATE | Bei jedem Empfänger im Klartext `ReceivedState: Accepted` usw., daneben die Erklärung | neu |
| 02_03 DialogMessage | RECEIVED_STATE | `ReceivedState: DialogMessagePending`, Rückfrage darunter | neu |
| 02_04 Processing | AVAILABILITY_TIME | Siehe «Offen» unten | teilweise |
| 02_05, 02_06 Finished/Rejected | PROCESS_FINISHED | «Der Prozess ist abgeschlossen. Der Fall kann nicht weiter bearbeitet werden.» Kein Abfrage-Knopf, Antwortfelder gesperrt, Server lehnt Antworten ab. | neu |
| 02_07 SwissdecFault beim Synchronisieren | FAULT_DISPLAY, PROCESS_FINISHED | Fehler beim Empfänger, nächster Schritt, Fall geschlossen, keine weiteren Abfragen. Geplante Wartung schliesst den Fall nicht. | neu |
| 03_01, 03_02, 03_03 Completion | COMPLETION | Knopf «Beim Empfänger freigeben» öffnet die URL mit Key und Passwort. Zusatzparameter und je 4000 Zeichen sind getestet. | war da, Test neu |
| 04_01 Lohnausweis mit Viewgen | GENERATE, VIEWGEN_VERSION | Viewgen ist auf der Testinstanz eingebaut (`074fcec`, `docs/claude/swissdec-stand.md`). Produktiv erst nach der Zertifizierung, bis dahin bleibt der eigene Barcode | auf Test prüfen |
| 04_02 Eigener Lohnausweis | GENERATE, SELECT_PERSONS, SCAN_BARCODE, FORWARD_CONTENT | Lohnausweis mit Barcode aus OneCrew. Scannen und Weiterleiten macht Walter. | Siehe «Offen» unten |
| 05_01 Standard-Dialog | GET_DIALOG, UI | Titel, Beschreibung, Abschnitte, Werte, Fragen. Abschnitte fehlten auf dieser Seite. | neu |
| 05_02 BESTA-Dialog | UI | Gleiche Darstellung, alle Feldtypen des Schemas. Muster fehlt im Repo. | ungeprüft |
| 06_01 Warnungen und Informationen | INFO, WARN | «Warnung: …», «Information: …» je Empfänger und «Für die ganze Meldung». Bleiben sichtbar, auch wenn die nächste Antwort keine nennt. | neu |
| 06_02 Ungültige AccountingPeriod | OUT_OF_RANGE, MALFORMED | Anmeldung → Feld Geschäftsjahr und «Profil bestellen». 2003 und 20010 werden abgewiesen, nichts wird gesendet. | neu |

## Offen

1. **Barcode im Lohnausweis (04_02).** Der gedruckte Barcode ist heute ELM v5, das v6-XML wird nur geprüft. Bevor der Fachexperte scannt, entscheidet Walter, ob auf v6 umgestellt wird. Grund für v5 war die Kompatibilität mit den Scannern der Steuerverwaltung.
2. **Zeitpunkt der Verfügbarkeit (02_04).** Das Schema kennt dafür kein Feld: `Processing` ist leer. OneCrew zeigt, was geliefert wird: das Zeitfenster einer geplanten Wartung und Hinweise des Empfängers. Liefert das Quality Tool den Zeitpunkt anders, das Antwort-XML aus dem Archiv ansehen.
3. **Profil bestellen (06_02).** Das Feld erscheint nur bei einer bestehenden Anmeldung (Zertifikat nötig). Die Bestellung ist schema-gültig, wurde aber noch nie gesendet.
4. **BESTA-Dialog (05_02).** Das Muster liegt im DialogMessage-Katalog auf infopoint.swissdec.ch, nicht im Repo.
