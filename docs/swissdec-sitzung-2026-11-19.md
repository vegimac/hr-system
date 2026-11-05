# Sitzung mit Swissdec, 19.11.2026: Notizen

Laufend ergänzt. Ältere Gesprächsliste: `docs/swissdec-call-2026-09-24.md`.

## 1. Meldung je Filiale statt über den Hauptsitz

**Ausgangslage**
- Testfirma Muster AG: eine Firma, eine UID, sechs Filialen als Arbeitsorte, alles in einer Sendung.
- Produktiv bei Schaub: sieben Filialen, jede mit eigenen Nummern bei AHV, FAK, BVG, Swica und Quellensteuer. Die Kassen erwarten getrennte Meldungen.
- Mirus macht es heute so: Jede Filiale sendet zu ihrem Zeitpunkt. Eine Test-QST-Meldung 10/2026 trägt Name, UID und SSL-Nummer der Filiale Oftringen.
- Geplant in OneCrew: Einstellung am Hauptsitz «Je Filiale» oder «Über den Hauptsitz». Die Rechnung bleibt gleich. Die Zertifizierung läuft mit dem Test-SUA und der Muster AG «Über den Hauptsitz».

**Fragen**
1. Ist die Zertifizierung mit der Muster AG gültig, wenn der Betrieb produktiv je Filiale sendet?
2. Welche UID steht bei AHV, FAK, BVG und Versicherern in der Meldung: die der Filiale oder die der Firma? Für die Quellensteuer zeigt Mirus die der Filiale.
3. Braucht es bei jedem Empfänger eine Anmeldung je Filiale?
4. Reicht ein SUA-Zertifikat für alle Filialen, auch für eine Filiale einer anderen GmbH (Langenthal Drive, Schaub Systemgastronomie)?
5. RL 10.2.2 verlangt, dass ein System separate SSL-Nummern und Buchungskreise unterstützt. Prüft die Zertifizierung das? Heute setzt OneCrew den Buchungskreis der Quellensteuer wohl noch nicht in die Meldung (noch zu prüfen).

## 2. Eintrittstag (TF25 Lehmann, TF26 Jenzer)

Eintritt 10.02.2025, Monatslohn 12'000. OneCrew zählt den Eintrittstag mit: 21/30 = 8'400. Die Testdaten erwarten 8'000. Bei TF12 Casanova (Eintritt 27.02.) zählt Swissdec selbst 4 Tage, also mit Eintrittstag. Der Experte hält «Eintrittstag zählt nicht» für möglich korrekt. Auf der Testinstanz sind darum nur die Testdaten angepasst, die Rechenregel nicht (A7, `docs/swissdec-abweichungsprotokoll.md`).

**Frage:** Welche Zählung gilt, und wie passen die beiden Testfälle zusammen?

## 3. BVG-Antwort

RL 8.6.8.6 und 8.6.9: Die Vorsorgeeinrichtung meldet BVG-Code, Gültig-ab und die Beiträge von Arbeitnehmer, Arbeitgeber und Dritten zurück.

**Fragen**
1. Muss ein System diese Beiträge in den Lohn übernehmen, oder reicht die Anzeige mit Abgleich?
2. Sind die Beträge Monats- oder Jahresbeträge?
3. Die Swissdec-Empfängerliste (Datei vom 01.10.2026) führt GastroSocial mit UVG, UVGZ und KTG, die Spalte BVG ist leer. Eine Pensionskasse GastroSocial fanden wir dort nicht. GastroSocial nennt für BVG das Portal connect. Ist eine Anbindung über den Distributor geplant? Bis dahin läuft BVG für Schaub nicht über ELM.
4. Gilt die Zertifizierung für BVG mit der Testfirma auch dann, wenn BVG im ersten Produktivbetrieb nicht gesendet wird?

## 4. Aus den abgeleiteten Regeln

Die 13 Fragen im Entwurf in `docs/swissdec-abgeleitete-regeln.md`, soweit bis zur Sitzung noch offen.
