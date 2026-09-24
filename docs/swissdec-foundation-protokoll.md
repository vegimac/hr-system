# Swissdec Foundation-Test — Protokoll

Was wir zu welchem Prüfpunkt gemacht haben, wo es im Code steht und was offen ist.
**Zweck:** vor jedem Swissdec-Call nachlesen können, ohne im Chatverlauf zu suchen.

System: **OneCrew** · Schaub Restaurants GmbH · ELM 6.0
Erster Call: **24.09.2026** (30 Minuten, Organisatorisches — Foundation-Test kommt zuerst)
Testlauf im Swissdec-Werkzeug: «Testlauf — keine Zertifizierungswirkung»

## Stand

| Gruppe | Punkte | erledigt | Stand |
|---|---:|---:|---|
| F01 Verbindung | 6 | 3 | F01_01, F01_02 grün · F01_03 gebaut und **am 24.09.2026 belegt** (Versatz +600 s) |
| F02 Sicherheit | 27 | 1 | F02_01 erledigt · F02_02–F02_11 brauchen WS-Security (Transmitter-Zertifikat) |
| F03 Interoperabilität | 12 | 0 | offen |
| F04 Archivierung | 3 | 0 | offen |
| F05 Übermittlung | 8 | 0 | offen |
| F06 Validierung | 1 | 0 | offen |
| F07 SUA-Zertifikat | 20 | 0 | offen |
| F08 Prozesse | 13 | 0 | offen |
| **Total** | **90** | **4** | |

Ein Teil der Punkte wird **vom Experten im Gespräch** geprüft (`CHECKED_BY_EXPERT`), nicht
automatisch — dafür ist dieses Protokoll gedacht: es liefert die Antwort auf «wie habt ihr das gelöst?».

## Reihenfolge — wichtig (Stand 24.09.2026)

- **Die Gruppen sind gesperrt und werden nacheinander freigeschaltet.** Im Werkzeug trägt jede
  Gruppe ausser F01 ein Schloss-Symbol. Vermutung: F01 muss vollständig sein, bevor es weitergeht —
  wir sind bei 3 von 6.
- **Das Transmitter-Zertifikat erstellen wir SELBST unter F07 «SUA-Zertifikat»** (Auskunft aus der
  Beratung an Walter). F07 ist noch nicht aktiv.
- **F07 kommt vor F05** (ebenfalls aus der Beratung): erst das Zertifikat, dann die Übermittlung.
- Daraus folgt unsere Arbeitsreihenfolge: **F01 fertigstellen → F02 (WS-Security, sobald das
  Zertifikat da ist) → F07 → F05**. Alles, was Krypto braucht (F02_02–F02_11, vermutlich auch
  Teile von F03), hängt am Zertifikat aus F07 — also nicht an Swissdec, sondern an unserem
  Fortschritt.

**Nächster Schritt:** F01_04, F01_05 und F01_06 anschauen — sie sind die letzten offenen Punkte
der einzigen freigeschalteten Gruppe.

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

### F01_04 – F01_06 — offen

Noch nicht angeschaut. **Was dazu schon steht:** CheckInteroperability ist gebaut (Umlaut-Kette
`ÄËÖÜÁÉÓÚÀÈÒÙÂÊÔÛ` plus zwei Beträge) und wird erwartungsgemäss mit `Client.security` abgewiesen,
solange das Transmitter-Zertifikat fehlt — die Antwort der RefApps ist dabei selbst WS-Security-
signiert (BinarySecurityToken + ds:Signature), was den Fault von einem echten Serverfehler
unterscheidet.

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

### F02_02 – F02_11 ⛔ blockiert durch das fehlende Transmitter-Zertifikat

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

## F03 Interoperabilität · F04 Archivierung · F05 Übermittlung · F06 Validierung · F07 SUA-Zertifikat · F08 Prozesse

Noch nicht bearbeitet. **Was voraussichtlich hilft, wenn die Punkte kommen:**

- **Sicherheit:** JWT mit Rollen, secure-by-default (jeder Endpunkt verlangt Login und HR-Rolle),
  Sicherheits-Header, zweite Prüfung per Authenticator pro Benutzer, Sperrbildschirm,
  Passwortwechsel-Zwang, vollständiges Audit-Log über alle Schreibzugriffe.
- **Archivierung:** Lohnbelege liegen als Snapshot samt `slip_json` fest; Perioden sind nach dem
  definitiven Abschluss gesperrt; Dokumente mit Zeitstempel und Zugriffsprotokoll.
- **Validierung:** XML wird beim Erzeugen gegen die ELM-6.0-Schemas geprüft
  (`Services/Elm/ElmXmlValidator.cs`).
- **Übermittlung:** noch offen — Etappe E4 (Declare / GetStatus / Synchronize) ist nicht gebaut.
- **SUA-Zertifikat:** hängt am Transmitter-Zertifikat, das Swissdec erst im Zertifizierungsprozess
  ausstellt.

---

## Offene Fragen an Swissdec

Die inhaltlichen Fragen zum Testmandanten stehen separat in
`docs/swissdec-call-2026-09-24.md` (sieben Fragen, als PDF verschickbar).
Zum Foundation-Test selbst offen:

1. Welche Punkte prüft der Experte im Gespräch, welche laufen automatisch?
2. Braucht es für F05 Übermittlung bereits das Transmitter-Zertifikat, oder genügt der
   Refapps-Weg?
3. Wird die Statistik-Domäne der Monatsmeldung zusammen mit der Quellensteuer geprüft?

---

*Pflege: nach jedem erledigten Prüfpunkt hier ergänzen — Anforderung im Wortlaut, unsere Lösung,
Code-Stellen, Tests, Datum. Bei Prüfpunkten, die wir bewusst anders lösen, die Begründung dazu.*
