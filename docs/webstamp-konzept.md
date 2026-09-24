# Briefpost über WebStamp (Schweizerische Post)

Stand 24.09.2026 · System → Kommunikation → **Briefpost (WebStamp)**

## Was es macht

OneCrew erstellt einen Brief als PDF (Adresse im Couvert-Sichtfenster). Über den
**Webservice WebStamp** (SOAP V6) geht das PDF an die Post; mit dem
**Druck- und Versandservice** (`printservice = true` + `document`) frankiert,
druckt, couvertiert und verschickt die Post den Brief. Kein Drucker, kein Couvert,
kein Gang zum Briefkasten.

Kosten laut Post: Porto + CHF 0.20 pro gedruckte Seite + CHF 0.20 pro Brief.
Bis 15.00 Uhr (Mo–Fr) übermittelt → gleichentags gedruckt und verschickt.
PDF-Regeln der Post: kein Scan, keine Absenderadresse als Grafik, max. 16 Seiten
pro Empfänger.

## Stand: Testphase

- **Bestellen (`new_order`) nur gegen die Testumgebung** — Sperre in
  `WebStampEndpunkte.BestellungErlaubt`. Die kostenlose Vorschau
  (`new_order_preview`) geht in beiden Umgebungen.
- Einschreiben: Der Druckservice gilt laut Post nur für Briefe **ohne Barcode**
  (A-/B-Post, Ausland). Einschreiben (Barcode) wäre nur mit Selbstdruck möglich —
  bei der Post nachfragen.
- Echtbetrieb verlangt laut Post ein **Abnahmeprotokoll** und einen
  **Integrationsvertrag**. Erst danach die Sperre per Code-Änderung aufheben.

## Zugang (vom Kunden zu beschaffen)

| Feld | Woher |
|---|---|
| Application-ID | vergibt die Post für OneCrew (max. 32 Zeichen) |
| WS-Kunden-ID | WebStamp-Konto → WebStamp-Einstellungen → Webservice WebStamp (max. 10) |
| WSWS-Passwort | ebenda; bei uns AES-verschlüsselt in `webstamp_setting` |

Kontakt Post: webservice.webstamp@swisspost.ch

## Adressen

| Umgebung | URL |
|---|---|
| Stabile Testumgebung (für Integratoren empfohlen) | `https://wsredesignint2.post.ch/wsws/soap/v6` |
| Produktiv | `https://webstamp.post.ch/wsws/soap/v6` |

Fest im Code (`Services/WebStamp/WebStampEndpunkte.cs`), wie bei Swissdec.
WSDL: `docs/webstamp/wsws-v6.wsdl` (am 24.09.2026 von der Testumgebung geholt).

## Technik

- SOAP 1.1 document/literal, SOAPAction `https://webstamp.post.ch/wsws/soap/v6#<methode>`.
- Methoden-Element im Namespace, alles darunter **unqualifiziert**; Inhalt unter `args`,
  Listen als `item`. `ping` ohne `args`.
- Jede Anfrage (ausser Options/Ping) trägt `identification` mit application,
  language, userid, password.
- Mitteilungen vom Typ `confirm` (z.B. neue AGB) **muss** der Integrator anzeigen —
  nach Fristablauf nimmt WebStamp keine Bestellung mehr an. Die Seite zeigt sie gelb.

| Datei | Aufgabe |
|---|---|
| `Services/WebStamp/WebStampSoap.cs` | Nachrichten bauen + Antworten lesen (rein, getestet) |
| `Services/WebStamp/WebStampClient.cs` | HTTP (TLS 1.2+), loggt NIE die Nachricht (Passwort/PDF) |
| `Services/WebStamp/WebStampBriefPdfService.cs` | Brief-PDF, Adresse auf festen mm im Fenster |
| `Controllers/WebStampController.cs` | `/api/webstamp/*`, nur admin |
| `wwwroot/js/briefpost.js` | Seite `page-briefpost` |
| `Tests/WebStampSoapTests.cs` | Aufbau/Antworten, Adresszeilen, PDF |

Tabellen (Schema-Stand 30): `webstamp_setting` (Singleton), `webstamp_auftrag`
(Protokoll jeder Vorschau/Bestellung; bei Bestellung inkl. PDF).

## Fenster-Masse

Adresse links ab 22 mm (rechts ab 118 mm), Fenster-Oberkante 45 mm, 15 mm frei
über der Adresse für die Frankatur. Ob die Post Adresse und Fenster erkennt,
meldet die Vorschau pro Sendung (`window`, `state`, `reason`). Passen die Masse
nicht, in `WebStampBriefPdfService` anpassen.

## Nächste Schritte

1. Zugang Testumgebung erhalten → Ping, Login prüfen, Produkte laden, Vorschau.
2. Fenster-Masse anhand der Vorschau bestätigen.
3. Einbau in die Abläufe (Kündigung, Behördenkorrespondenz, Mitteilung): Knopf
   «per Post senden», PDF-Kopie ins Personaldossier.
4. Abnahme durch die Post, dann Echtbetrieb freischalten.
