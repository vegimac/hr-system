# Swissdec Foundation-Test — Protokoll

Was wir zu welchem Prüfpunkt gemacht haben, wo es im Code steht und was offen ist.
**Zweck:** vor jedem Swissdec-Call nachlesen können, ohne im Chatverlauf zu suchen.

System: **OneCrew** · Schaub Restaurants GmbH · ELM 6.0
Erster Call: **24.09.2026** (30 Minuten, Organisatorisches — Foundation-Test kommt zuerst)
Testlauf im Swissdec-Werkzeug: «Testlauf — keine Zertifizierungswirkung»

## Stand

Die Zahlen der Übersicht sind **Einzelprüfungen** (Zeilen im Werkzeug inkl. TOOL_SETTING /
PREREQUISITE). Vollständiger Wortlaut: Abschnitt «Vollständiger Katalog» unten
(Walter kopiert 24.09.2026).

| Gruppe | Checks | erledigt | Stand |
|---|---:|---:|---|
| F01 Verbindung | 6 | **6** | ✅ abgeschlossen 24.09.2026 |
| F02 Sicherheit | 27 | 1 | F02_01 fertig · `ElmWsSecurity` gebaut · ERP-Schlüssel selbst erzeugen (F07-Kontext) |
| F03 Interoperabilität | 12 | 12 (gebaut) | ✅ gebaut 24.09.2026 · scharfe Prüfung braucht F02-Signatur |
| F04 Archivierung | 3 | 0 | offen (signiert/unverschlüsselt archivieren + SignatureConfirmation) |
| F05 Übermittlung | 8 | 0 | offen · **Expertin: erst nach F07** |
| F06 Validierung | 1 | 0 | offen (PlausibilityRules / Distributor-Ablehnung) |
| F07 SUA-Zertifikat | 20 | 0 | **gebaut 24.09.2026 (UI+Client)** · noch gegen RefApps vorzuführen |
| F08 Prozesse | 13 | 0 | offen (GetStatus, DialogMessages, Sync/Async) |
| **Total** | **90** | **7 belegt · 12 gebaut** | |

«gebaut» heisst: Der Code steht und ist mit nachgebauten Antworten getestet, vorführen lässt
er sich aber erst, wenn der Aufruf durchkommt (F03 braucht die Signatur aus F02).

Fast alles ist `CHECKED_BY_EXPERT` — der Experte prüft im Gespräch; dieses Protokoll ist die
Antwort auf «wie habt ihr das gelöst?».

## Zwei Zertifikate (wichtig — Stand Expertin itserv 24.09.2026)

**Auskunft Expertin (Walter, Call/Mail 24.09.2026 morgens):**

> Alles können wir **selbst in den Foundation-Tests erstellen**. Das geschieht unter
> **Punkt 7 (F07)**. **F07 vor F05** machen, damit das Zertifikat für die Übermittlung da ist.

| Zertifikat | Zweck | Woher laut Expertin |
|---|---|---|
| **ERP / Transmitter** | WS-Security (signieren + verschlüsseln), jede Op. ausser Ping | **selbst erzeugen** im Foundation-Kontext (nicht von Swissdec per Post) |
| **SUA** | Unternehmens-Ausweis; zweite Signatur («doppelt») | **F07**-Prozess: RegisterOrganization → … → SignCertificate |

Frühere Doku («Swissdec stellt das Transmitter-Zertifikat erst nach Zertifizierung aus») gilt für die
**Produktion**. Im Foundation-/RefApps-Testlauf erzeugen und installieren **wir** die Schlüssel
selbst — Ort dafür laut Expertin: **F07**.

WSDL-Hinweis bleibt: `RegisterOrganizationAuthentication` muss mit dem **ERP-Zertifikat**
signiert sein. Praktisch heisst das: In F07 (bzw. vor dem ersten Register-Aufruf) legen wir in
OneCrew ein ERP-Schlüsselpaar an, speichern es, und nutzen es dann für F02/F03/F07/F05.

## Reihenfolge (Expertin itserv 24.09.2026)

1. **F01** ✅ fertig.
2. **F07 vor F05** — Zertifikat(e) selbst anlegen / SUA-Flow durchspielen.
3. Parallel bzw. sobald ERP-Schlüssel da: **F02_02–11** (CheckInterop signiert+verschlüsselt)
   und **F03** (Operanden / Response-Prüfung).
4. **F04** Archiv · **F06** Plausibilität · **F08** Prozesse.
5. **F05** Übermittlung — erst wenn Zertifikat aus F07 vorhanden.

**Salär / Quality-Tool:** zurückgestellt, bis Foundation grün ist.

**Nächster konkreter Schritt:** F07 in OneCrew bauen — ERP-Schlüssel selbst erzeugen + speichern,
dann SUA: RegisterOrganization → Synchronize (Status) → SignCertificate → Renew → Doppel-Signatur.

---

## F01 — Verbindung

### F01_01 Adressierung ✅ erledigt 24.09.2026

**Anforderung (Swissdec):** «Das Sendersystem ist für die korrekte Adressierung des Distributors
verantwortlich. Dazu muss das Empfängersystem via korrekter URL angesprochen werden.»
**Erwartet:** «Die korrekte Adressierung zwischen Sender und Empfänger ist gewährleistet. Die URL
kann vom Endbenutzer nicht beliebig verändert werden.»

**Unsere Lösung — die Schranke ist die Person, nicht das Feld.**

1. **Die Seite ist kein Endbenutzer-Bereich.** Der Verbindungstest liegt im Bereich
   **«Entwicklung»**, der weder zu einer Rolle gehört noch für Admins sichtbar ist. Er wird pro
   Benutzer einzeln freigeschaltet.
2. **Diesen Bereich kann nur der Super-Admin vergeben.** Serverseitig durchgesetzt in
   `UsersController.AreasMitEntwicklungsSchutz`: Beim Anlegen und beim Ändern eines Benutzers wird
   der Bereich «entwicklung» aus der Liste entfernt, wenn der Aufrufer kein Super-Admin ist; eine
   bestehende Vergabe bleibt dabei unverändert. Ein Admin kann sich den Zugang also weder selbst
   geben noch einem anderen erteilen oder wegnehmen.
   *(Diese Prüfung fehlte bis zum 24.09.2026 — die Einschränkung war nur im UI. Beim Nachschauen
   für diesen Prüfpunkt gefunden und geschlossen.)*
3. **Das Super-Admin-Flag selbst ist nicht über die Oberfläche setzbar** — `app_user.is_super_admin`
   wird ausschliesslich per SQL gesetzt (bestehende Regel seit 15.05.2026), und ein
   Super-Admin-Konto darf nur ein Super-Admin bearbeiten.
4. **Die Endpunkte prüfen es nochmals selbst.** `GET /api/elm/endpunkte`, `POST /api/elm/ping` und
   `POST /api/elm/check-interoperability` verlangen zusätzlich zur Rolle `admin` das Super-Admin-Flag
   und antworten sonst mit **403 `NUR_SUPERADMIN`** (`ElmController.IstSuperAdminAsync`). Sichtbarkeit
   im Menü und Berechtigung am Endpunkt sagen damit dasselbe.
5. **Die offiziellen Adressen sind im Programm hinterlegt**, nicht abgetippt:
   `Services/Elm/ElmEndpunkte.cs` führt «Refapps Receiver (Test)» und «Produktiver Distributor»;
   zwei Knöpfe füllen sie ins Feld. Ein Tippfehler in der Distributor-Adresse ist damit im
   Normalbetrieb ausgeschlossen.
6. **Freie Eingabe bleibt möglich — bewusst** (Walter 24.09.2026): Die Testinfrastruktur liefert im
   Lauf der Zertifizierung wechselnde Receiver-Adressen. Da nur der Super-Admin überhaupt an die
   Seite kommt, ist das kein «Endbenutzer, der die URL verändert».

**Code:** `Services/Elm/ElmEndpunkte.cs` · `Controllers/ElmController.cs` ·
`Controllers/UsersController.cs` (`AreasMitEntwicklungsSchutz`) · `wwwroot/js/swissdec.js` ·
Bereichssteuerung `wwwroot/js/app-core.js` (`entw-on`)
**Tests:** `Tests/ElmAdressierungTests.cs` — feste Adressen, Super-Admin-Prüfung auf allen drei
Einstiegen, Entwicklungs-Schutz beim Anlegen und Ändern von Benutzern.

**Falls der Experte nachfragt:** Wir können die freie Eingabe jederzeit abschalten und nur noch die
beiden hinterlegten Ziele zulassen — die Umschaltung ist eine Zeile in `ElmController.ZielAufloesen`.

### F01_02 Erreichbarkeit ✅ erledigt 24.09.2026

**Anforderung:** «Die Erreichbarkeit des Distributors muss geprüft werden. Dazu wird eine einfache
Anfrage (PING) an den Distributor gesendet. Die Rückantwort bestätigt die Erreichbarkeit.»
**Erwartet:** Ping korrekt empfangen (F01_02_1) · «TX stellt Erfolgsmeldung dar» (F01_02_2).

**Unsere Lösung:** Knopf «📡 Ping» auf der Swissdec-Seite. Der Aufruf ist SOAP 1.1 mit leerer
SOAPAction; gesendet werden UserAgent (Producer, Name, Version, StandardVersion, Certificate) und
unsere Systemzeit. Die Antwort erscheint sofort als **grüne Erfolgsmeldung** «✓ Antwort erhalten»
mit HTTP-Status und Laufzeit in Millisekunden, darunter das vollständige Antwort-XML und
aufklappbar die gesendete Anfrage.

**Beleg (Lauf vom 24.09.2026 gegen die RefApps):** Antwort `PingResponse` mit
`Producer swissdec`, `Name swissdec refapps`, `Version 4.0.88 … RefApps stable`,
`StandardVersion 6.0`, `SystemDateTime 2026-09-24T10:49:16.393+02:00`.

**Wichtig und eingehalten:** Der Ping wird **ausschliesslich von Hand** ausgelöst — kein
Hintergrunddienst, kein Zeitplan, keine Wiederholung (Transmitter-Richtlinien Kap. 4).

**Code:** `Services/Elm/ElmTransmitterClient.PingAsync` · `wwwroot/js/swissdec.js` (`elmPing`)

### F01_03 Systemzeit ✅ gebaut 24.09.2026 — wartet auf die Prüfung

**Anforderung:** «Eine Differenz zwischen der Systemzeit des Distributors und der Systemzeit des
Absenders wird detektiert und dem Benutzer angegeben.»
**Erwartet:** «Die Systemzeit des Distributors wird mit der lokalen Systemzeit verglichen. Bei einer
Abweichung >1 Minute wird der Zeitunterschied in Form einer Fehlermeldung dargestellt.»
Ablauf im Test: Swissdec stellt in den RefApps eine falsche Zeit ein («Fake Ping Time», F01_03_0),
der Ping muss korrekt ankommen (F01_03_1) und unser Programm die Abweichung anzeigen (F01_03_2).

**Unsere Lösung:** Jede Ping-Antwort wird ausgewertet
(`ElmTransmitterClient.MitZeitvergleich`): `SystemDateTime` wird gelesen und über
`DateTimeOffset` mit unserer Zeit verglichen — also inklusive Zeitzonen-Versatz, «10:49+02:00» und
«08:49Z» sind derselbe Moment und keine Abweichung. Toleranz **60 Sekunden**
(`ZeitToleranzSekunden`, entspricht der Vorgabe «>1 Minute»).

Anzeige direkt über dem Antwort-XML:
- **innerhalb der Toleranz** — grüner Kasten «✓ Systemzeit stimmt überein — Abweichung 0.4 Sekunden
  (Toleranz 1 Minute)», darunter beide Zeitpunkte im Klartext.
- **über der Toleranz** — roter Kasten «✗ Systemzeit weicht ab — unsere Uhr geht 1 Min. 30 Sek. vor
  (zulässig ist höchstens 1 Minute). Bitte die Systemzeit des Servers prüfen, bevor Meldungen
  übermittelt werden.», ebenfalls mit beiden Zeitpunkten. Der Unterschied wird also **beziffert**,
  nicht nur gemeldet, und die Richtung steht dabei (vor/nach).

**Code:** `Services/Elm/ElmTransmitterClient.cs` (`MitZeitvergleich`, `ZeitToleranzSekunden`) ·
`wwwroot/js/swissdec.js` (`_elmZeitBlock`)
**Tests:** `Tests/ElmSystemzeitTests.cs` — gleiche Zeit, 30 und 45 Sekunden (still), 90 Sekunden und
eine Stunde (Meldung mit korrekter Grösse), Zeitzonen-Versatz ist keine Abweichung, Antwort ohne
Systemzeit und unlesbare Antwort ändern nichts.

**Für die Vorführung:** Sobald Swissdec die «Fake Ping Time» gesetzt hat, einmal auf «📡 Ping»
drücken — der rote Kasten mit der bezifferten Abweichung erscheint sofort.

**Eigener Test ohne Swissdec (Walter-Idee 24.09.2026):** Neben den Knöpfen steht ein Feld
**«Test: Systemzeit verstellen (Sek.)»**. Der Wert verstellt die Zeit, die wir **senden**, UND
unsere **Vergleichsbasis** — das Programm verhält sich damit wie mit einer falsch gehenden
Serveruhr. Ab 60 Sekunden erscheint die rote Fehlermeldung mit der bezifferten Abweichung; darüber
steht ein gelber Hinweis «Simulierter Zeitversatz aktiv: +120 Sekunden», damit niemand eine
Simulation für eine echte Messung hält. Der Wert gilt nur für den einzelnen Aufruf und wird nirgends
gespeichert (Grenze ±24 Stunden). Nützlicher Nebeneffekt: Weil die verstellte Zeit auch im
gesendeten `SystemDateTime` steht, sieht man zugleich, ob der Empfänger seinerseits eine Abweichung
des Absenders meldet.

**Beleg (Lauf vom 24.09.2026, Versatz +600 Sekunden):** HTTP 200, Antwort der RefApps mit
`SystemDateTime 2026-09-24T11:08:59.231+02:00`. Anzeige in OneCrew:
«⚠ Simulierter Zeitversatz aktiv: +600 Sekunden» und darunter
«✗ Systemzeit weicht ab — unsere Uhr geht **10 Minuten vor** (zulässig ist höchstens 1 Minute).
Bitte die Systemzeit des Servers prüfen, bevor Meldungen übermittelt werden.
Empfänger: 24.9.2026, 11:08:59 · hier: 24.9.2026, 11:18:59.»

**Nebenbefund zur Gegenrichtung (24.09.2026):** Die RefApps **spiegeln unseren gesendeten
Zeitstempel nicht** und melden ihn auch nicht als Fehler — trotz eines um 10 Minuten falschen
`SystemDateTime` in der Anfrage kam eine normale `PingResponse` mit HTTP 200 und der echten
Empfängerzeit zurück. Die Prüfung der Absenderzeit findet also (zumindest in der Testinfrastruktur)
nicht statt; F01_03 zielt ausschliesslich auf unsere Seite.

**So wird es von Swissdec vorgeführt:** In der **RefApps-Receiver-App** die Einstellung **«Fake Ping Time»**
setzen (das ist der Prüfschritt F01_03_0, ein Werkzeug-Setting auf Swissdec-Seite), danach in OneCrew
auf «📡 Ping» drücken. Die falsche Zeit kommt in der Antwort zurück, und der rote Kasten zeigt die
bezifferte Abweichung. **Ohne diese Einstellung ist der Fall nicht auslösbar** — der Empfänger
antwortet sonst mit seiner echten Zeit, und wir zeigen korrekt den grünen Kasten.

### Fehlermeldungen bei abgewiesenen Aufrufen (24.09.2026)

Aus demselben Anlass ergänzt: Weist der Empfänger einen Aufruf ab, kommt HTTP 500 mit einem
**SOAP-Fault**. Bisher stand auf dem Bildschirm nur «HTTP 500». Jetzt wird der Fault gelesen
(`ElmTransmitterClient.MitFault`, SOAP 1.1 und 1.2) und als roter Kasten angezeigt: Code plus
Klartext, z.B. «Abgewiesen — Client.security · security requirements not met». Bei
sicherheitsbezogenen Faults steht zusätzlich die Einordnung dabei, dass ab dieser Operation eine
WS-Security-Signatur mit dem Transmitter-Zertifikat verlangt wird. Das dürfte auch für die Gruppen
F05 (Übermittlung) und F08 (Prozesse) nützlich sein, wo die Darstellung von Rückmeldungen geprüft wird.

### F01 abgeschlossen

Die Gruppe hat **drei Punkte mit sechs Einzelprüfungen** — F01_04 bis F01_06 gibt es nicht. Alle
sechs sind grün; der Experte hat zu F01_01 vermerkt: «Ist nur mit SuperAdmin erreichbar.»

---

## F02 — Sicherheit (27 Punkte)

Die Gruppe ist **ein zusammenhängender Block: WS-Security**. Ausser F02_01 läuft jeder Punkt über
**CheckInteroperability** und setzt voraus, dass wir Nachrichten **signieren und verschlüsseln** und
die Antworten des Empfängers **prüfen**. Das geht erst mit dem **Transmitter-Zertifikat**, das
Swissdec im Zertifizierungsprozess ausstellt (eigene CA; es gibt keinen öffentlichen Test-Keystore —
`docs/swissdec/SecurityTransmitter_d.pdf` Kap. 3).

### F02_01 Transportsicherheit ✅ erledigt 24.09.2026

**Anforderung:** «Der Übermittlungskanal muss verschlüsselt sein. Alle Verbindungen sind mittels TLS
gesichert.» **Erwartet:** «TX sendet CheckInterop über einen TLS-gesicherten Kanal.»

**Unsere Lösung:**
- **Nur `https` ist zulässig.** `ElmEndpunkte.IstSicher` weist jede `http://`-Adresse ab — auch eine
  von Hand eingetragene; die Antwort ist 400 mit Klartext. Beide hinterlegten Ziele sind https.
- **Nur TLS 1.2 und 1.3.** Der Transmitter benutzt einen eigenen `SocketsHttpHandler` mit
  `EnabledSslProtocols = Tls12 | Tls13`; SSL 3 und TLS 1.0/1.1 sind ausgeschlossen.
- **Nachweis auf dem Bildschirm:** Nach jedem Aufruf steht über der Antwort ein grüner Kasten
  «🔒 Verbindung verschlüsselt — Tls13 · Chiffre TLS_AES_256_GCM_SHA384 · Serverzertifikat …
  (Aussteller …, gültig bis …)». Die Angaben kommen aus der **tatsächlich aufgebauten Verbindung**
  (`PlaintextStreamFilter` liest den fertigen `SslStream`), nicht aus einer Vermutung. Eine alte
  TLS-Version würde den Kasten rot färben.

**Code:** `Services/Elm/ElmTransmitterClient.cs` (Handler + `TlsInfo`) · `Services/Elm/ElmEndpunkte.cs`
(`IstSicher`) · `wwwroot/js/swissdec.js` (`_elmTlsBlock`)
**Tests:** `Tests/ElmAdressierungTests.cs` — https zulässig, http/ftp/leer abgewiesen, beide Ziele
https, nur TLS 1.2/1.3 freigeschaltet.

### F02_02 – F02_11 🔧 Krypto-Schicht gebaut 24.09.2026, wartet auf das Zertifikat

**Gebaut:** `Services/Elm/ElmWsSecurity.cs` — die vollständige WS-Security-Schicht nach
`SecurityTransmitter_d.pdf`:

- **Signieren:** Body **und** Timestamp (mit `Expires`, Schutz gegen Wiedereinspielen), Zertifikat
  als Base64-`BinarySecurityToken` mit **direkter** `SecurityTokenReference`, `mustUnderstand="1"`,
  Algorithmen **exc-c14n / RSA-SHA256 / SHA256**.
- **Verschlüsseln:** Body-**Inhalt** mit frischem AES-256-CBC-Schlüssel, dieser via **RSA-OAEP** mit
  dem Empfängerzertifikat; der `EncryptedKey` liegt im Security-Header und verweist über eine
  `ReferenceList` auf die Daten. Reihenfolge **erst signieren, dann verschlüsseln**.
- **Prüfen:** entschlüsseln, Signatur verifizieren, Zertifikat beurteilen. Das Ergebnis ist ein
  **Befund** mit eigener Meldung je Fall: `SignaturFehlt`, `SignaturUngueltig`, `ZertifikatFehlt`,
  `ZertifikatNichtVertrauenswuerdig`, `VerschluesselungFehlt`, `EntschluesselungFehlgeschlagen`.
  Eine ungültige Signatur wird **nicht akzeptiert** (Richtlinie Kap. 3.3.1 Punkt 7) — genau das
  verlangt F02_11.
- Eingehend bewusst grosszügig bei den Algorithmen (der Distributor darf andere verwenden),
  ausgehend streng nach Vorgabe.

**Zwei Fallen, die dabei aufgefallen sind** (für den nächsten, der das anfasst):
1. Eine frisch im Speicher gebaute Nachricht kanonisiert anders als dieselbe Nachricht nach dem
   Serialisieren — die Signatur wäre beim Empfänger ungültig gewesen, obwohl niemand etwas
   verändert hat. Darum wird der DOM **vor** dem Signieren einmal durch Text und zurück geschickt.
2. `EncryptedXml.DecryptDocument` findet den WS-Security-Schlüssel nicht (er liegt im Header, nicht
   im `KeyInfo` der Daten). Die Entschlüsselung holt ihn deshalb selbst.

**Tests:** `Tests/ElmWsSecurityTests.cs` (12) — signierte Nachricht enthält alle geforderten Teile,
eigene Signatur wird als gültig erkannt, **verfälschte Nachricht wird zurückgewiesen**, fremdes
Zertifikat fällt auf, fehlende Signatur wird gemeldet, Verschlüsselung verbirgt den Inhalt
tatsächlich, Ver- und Entschlüsseln im Durchlauf, fehlende Verschlüsselung wird gemeldet, ein
gekipptes Zeichen im Chiffrat wird erkannt, fehlender privater Schlüssel meldet Klartext, Ping
bleibt aussen vor. Die Tests erzeugen ihre Zertifikate selbst — sie laufen ohne Swissdec.

**Was noch fehlt:** das **Transmitter-Zertifikat** (kommt aus F07) und die Verdrahtung in
CheckInteroperability, sobald es da ist. Ohne Zertifikat läuft der Aufruf unverändert unsigniert —
die RefApps weisen ihn dann wie gehabt mit `Client.security` ab.

### Ursprüngliche Einschätzung (überholt)

| Punkt | Verlangt | Was wir dafür bauen müssen |
|---|---|---|
| F02_02 WS-Verschlüsselung | Alle Requests ausser Ping sind verschlüsselt | XML-Encryption der Nutzdaten mit dem Empfängerzertifikat |
| F02_03 Verfälschte Verschlüsselung | Falsch verschlüsselte Antwort erkennen und melden | Entschlüsselung + Fehlerbehandlung mit klarer Meldung |
| F02_04 Unverschlüsselte Antwort | Fehlende Verschlüsselung der Antwort erkennen | Pflicht-Prüfung «Antwort muss verschlüsselt sein» |
| F02_05 WS-Signatur | Alle Requests ausser Ping sind signiert | XML-Signatur (Reihenfolge Signatur → Verschlüsselung) |
| F02_06 Verfälschte Signatur | Verfälschte Antwortsignatur erkennen | Signaturprüfung der Antwort |
| F02_07 Unsignierte Antwort | Fehlende Signatur erkennen | Pflicht-Prüfung «Antwort muss signiert sein» |
| F02_08 Falsches Zertifikat | Antwort mit unbekanntem Schlüssel erkennen | Zertifikats-/Vertrauensprüfung gegen die Swissdec-CA |
| F02_09 Signierter SOAP-Fault | Signierten Fault anzeigen | Fault-Anzeige (steht seit 24.09.) + Signaturprüfung |
| F02_10 Unsignierter SOAP-Fault | Unsignierten Fault anzeigen | Fault-Anzeige (steht) |
| F02_11 Verfälschter SOAP-Fault | Fault mit ungültiger Signatur **zurückweisen** | Signaturprüfung; Fault verwerfen statt anzeigen |

Bei jedem dieser Punkte stellt Swissdec vorher eine RefApps-Einstellung um
(`Tamper Encryption`, `Enable Encryption` aus, `Tamper Signature`, `Enable Signature` aus,
`Use unknown Key`, SOAP-Fault-Varianten) — die Prüfung ist also immer: **erkennen und dem Benutzer
zeigen**. Die Anzeige-Hälfte haben wir seit dem 24.09. (SOAP-Fault im Klartext, F02_09/F02_10);
es fehlt die Krypto-Hälfte.

**Was ohne Zertifikat schon vorbereitet werden kann:** die WS-Security-Schicht selbst (Signieren,
Verschlüsseln, Prüfen) samt Tests mit einem selbst erzeugten Testschlüssel, sowie einheitliche
Fehlermeldungen für die acht Fälle. Scharf prüfen lässt sich erst mit dem echten Zertifikat.

**Woher das Zertifikat kommt (Auskunft aus der Beratung, 24.09.2026):** Wir erstellen es **selbst
unter F07 «SUA-Zertifikat»** (20 Prüfpunkte). F07 ist im Werkzeug noch gesperrt — vermutlich müssen
die vorangehenden Gruppen zuerst erledigt sein. F02 ist damit **nicht von Swissdec abhängig**,
sondern von unserem eigenen Fortschritt bis F07.

---

## F03 — Interoperabilität (12 Punkte) ✅ gebaut 24.09.2026

`CheckInteroperability` ist eine Rechenprobe über den ganzen Weg: Wir senden eine
vorgegebene Zeichenkette mit Umlauten und eine vorgegebene Zahl, der Empfänger schickt
beides zurück und rechnet damit. Stimmt etwas nicht, liegt es am Encoding oder am
Zahlenformat.

**Gefundener Fehler bei uns (24.09.2026):** Unser Aufruf sendete als ersten Operanden
`1234.55`. Das XSD gibt den Wert aber fest vor — «use following value for the FirstOperand:
999000000000.00 (999 Milliarden)» — und F03_01 verlangt genau diese Konstante. Korrigiert.

**Was jetzt gilt:**

| Feld | Wert | Herkunft |
|---|---|---|
| `UmlautString` | `ÄËÖÜÁÉÓÚÀÈÒÙÂÊÔÛ` | fest im Programm (F03_01) |
| `FirstOperand` | `999000000000.00` | fest im Programm (F03_01) |
| `SecondOperand` | wählbar, drei Knöpfe `0.01` / `0.00` / `−999'000'000'000.00` | Eingabe (F03_02) |

**Zahlformat (F03_03):** Das Schema verlangt `[\-]?[0-9]+\.[0-9]{2}` — Punkt als Trenner,
immer genau zwei Nachkommastellen. Die Eingabe darf mit Komma und Tausendertrennern
erfolgen (Schweizer Tastatur); `ElmInterop.LiesBetrag` liest sie, rundet auf zwei Stellen,
und `ElmInterop.Betrag` formatiert beim Senden. Ein Wert wie `12.345` geht also als
`12.35` hinaus, nie als `12.345`.

**Die Antwort wird nachgerechnet, nicht geglaubt (F03_04/F03_05).** Swissdec verfälscht in
den RefApps absichtlich die Antwort («Tamper UmlautString», «Tamper FirstOperand»); wer sie
bloss anzeigt, fällt durch. `ElmInterop.Pruefe` prüft darum fünf Dinge und bestätigt die
Interoperabilität nur, wenn alle fünf stimmen:

1. `UmlautStringIsCorrect` = true (der Empfänger bestätigt unsere Sendung),
2. `FirstOperandIsCorrect` = true,
3. der zurückgegebene `UmlautString` ist exakt `äëöüáéóúàèòùâêôû` (dieselbe Kette in
   Kleinbuchstaben — so steht es im XSD-Kommentar),
4. `AdditionResult` = FirstOperand + SecondOperand,
5. `SubtractionResult` = FirstOperand − SecondOperand,

je Betrag zusätzlich das Zahlformat. Beim dritten Prüfwert ist das Ergebnis besonders
aufschlussreich: Addition `0.00`, Subtraktion `1998000000000.00` — wer intern mit
`float` statt `decimal` rechnet, sieht es hier.

Bei einer Abweichung steht rot auf dem Bildschirm, WAS nicht stimmt (erwartet/erhalten je
Zeile), und der Satz «Interoperabilität NICHT bestätigt».

**Dateien:** `Services/Elm/ElmInterop.cs` (neu), `ElmTransmitterClient.CheckInteroperabilityAsync`,
`ElmController.CheckInteroperability`, `wwwroot/js/swissdec.js` (`_elmInteropBlock`, `elmSetOperand`),
Feld `elmOperand2` in `index.html`. Tests: `Tests/ElmInteropTests.cs` (30).

**Offen:** Scharf vorführen lässt sich F03 erst, wenn der Aufruf durchkommt — CheckInteroperability
wird ohne WS-Security-Signatur mit «Client.security» abgewiesen (F02). Die Prüflogik steht
und ist mit nachgebauten Antworten belegt; sie wartet nur auf das Zertifikat.

---

## F07 — SUA-Zertifikat: Bauanleitung (Recherche 24.09.2026)

**Wird von Walter mit Cursor gebaut.** Hier steht, was aus den Schemas und dem Beispiel-XML
hervorgeht, damit die Suche nicht zweimal gemacht werden muss.

### Ablauf in drei Schritten

**1 · `RegisterOrganizationAuthentication`** — meldet das Unternehmen an.
**Enthält NOCH KEINEN Zertifikatsantrag** (Beleg: `samples/RegisterOrganizationAuthentication.xml`).
Inhalt: `RequestContext` · `Job/Addressee` (mit `addresseeID`, `AddresseeIdentification`,
`ProcessByDistributor`) · `RegisterOrganization` mit `Institution` (z.B. `UVG-LAA` mit
`addresseeIDRef`), `CompanyDescription` (Name, Adresse, `UID-BFS/UID` = `CHE-123.456.789`)
und `Contact/Name`. Attribut `schemaVersion` ist Pflicht.

Antwort (`Addressees/Addressee`) ist eine Auswahl aus **`Processing`** / **`Error`** /
**`Success`**. Bei Erfolg kommen die zwei Dinge, die den ganzen weiteren Verlauf tragen:

* `AddresseeContext/CertificateRequestID`
* `Credentials/{Key, Password}`

Beides muss gespeichert werden — ohne sie findet man den Fall nicht wieder.

**2 · `SynchronizeRegisterOrganizationAuthentication`** — Statusabfrage UND Antrag in einem.
Aufbau: `Sender/UID-BFS` · `Addressee` · `Case`:

```
Case
 ├ CaseContext      ← Credentials (Key/Password) + CertificateRequestID (+ TestCase)
 ├ ReceivedState    ← optional: der Zustand, den wir zuletzt gesehen haben (Quittierung)
 ├ SignCertificate  ← optional: Creation, StoryID, PEM (der CSR), OneTimePassword
 └ RenewCertificate ← optional: Creation, StoryID, PEM — OHNE OneTimePassword
```

Antwort: `Error` oder `…Consumer/Case` mit `CaseContext`, **`State`**, optional `Quittance`
und optional `Certificate`.

**Die Zustände sind kleingeschrieben** (`RegisterOrganizationAuthenticationStateType`):
`processing` · `registered` · `rejected` · `verified` · `expired`.
Achtung: **`expired` ist ein sechster Zustand**, den die Prüfliste (F07_03–F07_06) nicht nennt —
er muss trotzdem angezeigt werden, sonst steht der Benutzer vor einem leeren Bildschirm.

**3 · Zertifikat holen.** Erst bei `State = verified` wird der Antrag mitgeschickt
(`SignCertificate` mit CSR + Einmalpasswort). Die Antwort ist `CertificateSignResponseType`:
`SubjectDN` · `IssuerDN` · `NotBefore` · `NotAfter` · **`PEM` (base64-kodiert)**.

### Der Subject-DN kommt vom Empfänger, nicht von uns

`Quittance/X509Subject` liefert `CommonName`, `OrganizationName`, `LocalityName`,
`StateOrProvinceName`, `CountryName` und optional `BusinessCategory`. **Der CSR ist mit genau
diesen Werten zu bauen** — ein selbst ausgedachter DN wird beim Signieren abgelehnt. Vor der
Quittung kennen wir sie nicht; darum: erst Register + Synchronize bis `verified`, dann CSR.

### Stolperstelle: wohin mit dem privaten Schlüssel

**Nicht ins Programmverzeichnis.** `deploy.sh` macht auf dem Server `rm -rf /var/www/hr-system/*`
und packt das Publish-Paket neu aus — alles dort ist nach dem nächsten Deploy weg. Muster im
Haus ist `Documents:StoragePath` (`/var/data/hr-system/documents`, gesetzt über die
systemd-Umgebung). Für die Schlüssel also dasselbe: eigener Pfad ausserhalb von `/var/www`,
Rechte 600, und **auf beiden Servern** einrichten (Test + Prod).

### Was schon da ist und nicht neu gebaut werden muss

| Vorhanden | Wo |
|---|---|
| Signieren / Verschlüsseln / Prüfen (WS-Security) | `Services/Elm/ElmWsSecurity.cs` — `Signiere(doc, zert)` genügt für den Register-Aufruf |
| SOAP-Umschlag, Versand, TLS-Nachweis, Fault-Auswertung, Zeitvergleich | `Services/Elm/ElmTransmitterClient.cs` (`Envelope`, `PostAsync`, `MitFault`, `MitZeitvergleich`) |
| Ziel-Adressen + https-Zwang | `Services/Elm/ElmEndpunkte.cs` |
| Superadmin-Schranke für alle ELM-Endpunkte | `ElmController.NurSuperAdmin()` |
| Nachrechnen der Interop-Antwort | `Services/Elm/ElmInterop.cs` |

`RegisterOrganizationAuthentication` muss **mit dem ERP-Zertifikat signiert** sein — das
Schlüsselpaar dafür erzeugen wir laut Expertin selbst (siehe Abschnitt «Zwei Zertifikate»).

### Prüfpunkte, die daraus folgen

F07_02 verlangt einen **Fault** beim Register (RefApps-Einstellung) — die Fault-Anzeige steht
seit 24.09. F07_03/04/05 sind die drei Zustände, F07_06 das Signieren bei `verified`,
F07_07 die Erneuerung (`RenewCertificate`, ohne Einmalpasswort) und F07_08 die
**Doppelsignatur** von CheckInteroperability mit ERP- **und** SUA-Zertifikat.

### Gebaut 24.09.2026 (Walter + Cursor)

| Baustein | Wo |
|---|---|
| ERP erzeugen/laden, SUA speichern, Fall (RequestID+Credentials), Empfänger-.cer | `Services/Elm/ElmZertifikatStore.cs` — Pfad `Swissdec:CertStoragePath` bzw. neben Documents |
| Register / Synchronize / Sign / Renew + CSR aus Subject-DN | `Services/Elm/ElmSuaService.cs` |
| Signiert (+ optional verschlüsselt) senden | `ElmTransmitterClient.PostGesichertAsync` |
| API (Superadmin) | `GET/POST /api/elm/sua/*` in `ElmController` |
| UI-Karte «F07 · SUA-Zertifikat» | `wwwroot/index.html` + `js/swissdec.js` |
| Tests | `Tests/ElmSuaTests.cs` (9) |

**Noch offen nach dem Bau:** gegen RefApps vorführen (F07_01–08); Empfängerzertifikat aus der
RefApps-UI hinterlegen, sonst nur Signatur ohne Verschlüsselung; F07_08 Doppel-Signatur
(CheckInterop mit ERP+SUA) — `ElmWsSecurity` signiert heute mit einem Zertifikat.

**Server:** `Swissdec__CertStoragePath=/var/data/hr-system/swissdec-certs` (Test:
`…-test/swissdec-certs`) in der systemd-Umgebung setzen — analog Documents.

---

## F03–F08 — Kurzstand (Details im Katalog unten)

| # | Was gebaut werden muss | Vorhanden? |
|---|---|---|
| **F03** | CheckInterop mit festem FirstOperand/UmlautString; SecondOperand wählbar (0.01 / 0.00 / −999'000'000'000.00); immer 2 Nachkommastellen; Response prüfen (Umlaut klein, Operanden); Tamper-Varianten melden | ✅ **gebaut 24.09.2026** — `ElmInterop` + UI + 30 Tests (siehe Abschnitt F03) |
| **F04** | Jeder Request/Response **signiert und unverschlüsselt** archivieren; SignatureConfirmation in der Response prüfen | fehlt |
| **F05** | SubscribeOrganization; 1 vs. n Addressees; Declare mit Empfängerwahl; DeclarationId spiegeln; Substitution; eindeutige RequestID; `<TestCase/>` | Sample-XML vorhanden · Client/UI fehlt (E4) |
| **F06** | PlausibilityRules-Verletzung → Distributor-Fehler dem User zeigen | fehlt |
| **F07** | RegisterOrganization → Synchronize (Processing/Registered/Rejected) → SignCertificate (Verified) → Renew → doppelte Signatur (ERP+SUA) auf CheckInterop | ✅ Client+UI 24.09. · RefApps-Vorführung + Doppel-Signatur offen |
| **F08** | Sync-Übermittlung mit Quittungen; GetStatus(JobKey); Stories; DialogMessage (manuell/Reply/Confirm/Random/Complete) | fehlt |

---

## Vollständiger Katalog (Walter 24.09.2026 aus dem Werkzeug)

Wortlaut der Prüfpunkte — Referenz. Lösungsdetails stehen oben (F01/F02) bzw. werden
nach Erledigung pro Punkt ergänzt.

### F01 Verbindung

| ID | Typ | Erwartet |
|---|---|---|
| F01_01_1 | CONFIGURATION | URL-Handhabung beim TX — Experte: «nur SuperAdmin / Entwicklung» ✅ |
| F01_02_1 | PING | Ping korrekt empfangen ✅ |
| F01_02_2 | UI | TX stellt Erfolgsmeldung dar ✅ |
| F01_03_0 | TOOL_SETTING | RefApps «Fake Ping Time» |
| F01_03_1 | PING | Ping mit falscher Systemzeit empfangen ✅ (Simulation + Fake) |
| F01_03_2 | UI | TX stellt Fehlermeldung dar ✅ |

### F02 Sicherheit

| ID | Typ | Erwartet |
|---|---|---|
| F02_01_1 | CHECK_INTEROP | CheckInterop über TLS ✅ |
| F02_02_1 | CHECK_INTEROP | Request-Nutzdaten verschlüsselt |
| F02_03_0/1/2 | TOOL+INTEROP+UI | Tamper Encryption → Fehlermeldung |
| F02_04_0/1/2 | TOOL+INTEROP+UI | Enable Encryption aus → Fehlermeldung |
| F02_05_1 | CHECK_INTEROP | Request signiert |
| F02_06_0/1/2 | TOOL+INTEROP+UI | Tamper Signature → Fehlermeldung |
| F02_07_0/1/2 | TOOL+INTEROP+UI | Enable Signature aus → Fehlermeldung |
| F02_08_0/1/2 | TOOL+INTEROP+UI | Use unknown Key → Fehlermeldung |
| F02_09_0/1/2 | TOOL+INTEROP+UI | signierter SOAP-Fault anzeigen |
| F02_10_0/1/2 | TOOL+INTEROP+UI | unsignierter SOAP-Fault anzeigen |
| F02_11_0/1/2 | TOOL+INTEROP+UI | Fault mit ungültiger Signatur **zurückweisen** |

### F03 Interoperabilität

| ID | Typ | Erwartet |
|---|---|---|
| F03_01_0/1 | INTEROP+UI | CheckInterop senden; Response handhaben/anzeigen |
| F03_02_1 | INTEROP | SecondOperand = **0.01** |
| F03_02_2 | INTEROP | SecondOperand = **0.00** |
| F03_02_3 | INTEROP | SecondOperand = **−999'000'000'000.00** |
| F03_03_1 | INTEROP | immer zwei Nachkommastellen |
| F03_04_0/1/2 | TOOL+INTEROP+UI | Tamper UmlautString → Interop **nicht** bestätigt |
| F03_05_0/1/2 | TOOL+INTEROP+UI | Tamper FirstOperand → Interop **nicht** bestätigt |

Konstante im Request (Schema): Operand1 = `9.99E11`, UmlautString =
`ÄËÖÜÁÉÓÚÀÈÒÙÂÊÔÛ` (Antwort erwartet Kleinbuchstaben-Variante).

### F04 Archivierung

| ID | Typ | Erwartet |
|---|---|---|
| F04_01_1 | VALIDATOR | Requests/Responses signiert **und unverschlüsselt** archiviert |
| F04_01_2 | UI | manuelle Prüfung der Archiv-Dateien im ERP |
| F04_02_1 | SIGNATURE_CONFIRMATION | SignatureConfirmation in der Response im Archiv |

### F05 Übermittlung

| ID | Typ | Erwartet |
|---|---|---|
| F05_01_1 | SUBSCRIBE_ORG | SubscribeOrganization-Request |
| F05_02_1 | ADDRESSEE | genau **ein** Adressat |
| F05_03_1 | ADDRESSEES | **mehr als ein** Adressat (`ProcessedByDistributor=1`) |
| F05_04_1 | DECLARE | Declare mit gewählter Adressatenauswahl |
| F05_05_1 | DECLARATION_ID | DeclarationId im Synchronize gespiegelt |
| F05_06_1 | SUBSTITUTION | Substitution-Tag mit DeclarationId der Ursprungsmeldung |
| F05_07_1 | REQUEST_ID | jede RequestID eindeutig (Wiederholung = Fehler) |
| F05_08_1 | TEST_CASE | vollständiger Prozess mit `<TestCase/>` |

### F06 Validierung

| ID | Typ | Erwartet |
|---|---|---|
| F06_01_1 | PLAUSIBILITY | Verletzung der PlausibilityRules → Fehlermeldung beim TX |

### F07 SUA-Zertifikat

| ID | Typ | Erwartet |
|---|---|---|
| F07_01_1 | REGISTER_ORG | RegisterOrganization erfolgreich |
| F07_02_0/1/2 | TOOL+REG+UI | Register → Fault → anzeigen |
| F07_03_0/1/2 | TOOL+SYNC+UI | Synchronize → **Processing** anzeigen |
| F07_04_0/1/2 | TOOL+SYNC+UI | Synchronize → **Registered** anzeigen |
| F07_05_0/1/2 | TOOL+SYNC+UI | Synchronize → **Rejected** anzeigen |
| F07_06_0/1/2 | TOOL+SIGN+UI | ReceivedState=Verified → **SignCertificate** + Ergebnis anzeigen |
| F07_07_0/1 | TOOL+RENEW | Renew + weiterer Request **doppelt signiert** mit neuem Zertifikat |
| F07_08_0/1 | TOOL+DOUBLE | SUA installiert (AB-18) → CheckInterop **doppelt signiert** |

### F08 Prozesse

| ID | Typ | Erwartet |
|---|---|---|
| F08_01_1 | SYNCHRON | synchrone Übermittlung → gesammelte Quittungen in der Response |
| F08_02_1 | GET_STATUS | GetStatus mit JobKey nach Declare |
| F08_02_2 | PROCESSED | Fall `ProcessedByDistributor=false` korrekt handhaben |
| F08_03_1 | STORY | synchronisierte Stories korrekt |
| F08_04_1 | UI | Dialog-Funktionalität (z.B. BFS) manuell bedienbar |
| F08_05_1 | REPLY_DIALOG | Dialog im nächsten Synchronize mit Pflichtfeldern beantworten |
| F08_06_1 | CONFIRM_DIALOG | DialogMessage mit Error im nächsten Synchronize quittieren |
| F08_07_0/1/2 | TOOL+GET+UI | RandomDialogMessage empfangen und darstellen |
| F08_08_0/1/2 | TOOL+GET+UI | CompleteDialogMessage-Felder darstellen |

---

## Offene Fragen (Foundation)

1. ~~Woher kommt das ERP-Zertifikat?~~ → **geklärt 24.09.2026 (Expertin itserv):** selbst in
   Foundation erstellen, unter **F07**; **F07 vor F05**.
2. Braucht Subscribe/Declare (F05) zwingend schon SUA, oder reicht das ERP-Zertifikat allein?
   (Expertin: F07 vor F05 «um das Zertifikat zu haben» — vermutlich SUA und/oder beides.)
3. Ist «AB-18» in F07_08_0 eine interne Prüfnummer (= SUA installiert) oder ein separates Dokument?

Fachfragen Testmandant bleiben in `docs/swissdec-call-2026-09-24.md` (zurückgestellt).

---

*Pflege: nach jedem erledigten Prüfpunkt oben ergänzen — Anforderung, Lösung, Code, Tests,
Datum. Katalog unten nur bei Werkzeug-Änderungen anfassen.*
