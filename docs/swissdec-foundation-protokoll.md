# Swissdec Foundation-Test — Protokoll

Was wir zu welchem Prüfpunkt gemacht haben, wo es im Code steht und was offen ist.
**Zweck:** vor jedem Swissdec-Call nachlesen können, ohne im Chatverlauf zu suchen.

System: **OneCrew** · Schaub Restaurants GmbH · ELM 6.0
Erster Call: **24.09.2026** (30 Minuten, Organisatorisches — Foundation-Test kommt zuerst)
Testlauf im Swissdec-Werkzeug: «Testlauf — keine Zertifizierungswirkung»

## Stand

| Gruppe | Punkte | erledigt | Stand |
|---|---:|---:|---|
| F01 Verbindung | 6 | 1 | F01_01 grün (vom Experten geprüft) |
| F02 Sicherheit | 27 | 0 | offen |
| F03 Interoperabilität | 12 | 0 | offen |
| F04 Archivierung | 3 | 0 | offen |
| F05 Übermittlung | 8 | 0 | offen |
| F06 Validierung | 1 | 0 | offen |
| F07 SUA-Zertifikat | 20 | 0 | offen |
| F08 Prozesse | 13 | 0 | offen |
| **Total** | **90** | **1** | |

Ein Teil der Punkte wird **vom Experten im Gespräch** geprüft (`CHECKED_BY_EXPERT`), nicht
automatisch — dafür ist dieses Protokoll gedacht: es liefert die Antwort auf «wie habt ihr das gelöst?».

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

### F01_02 – F01_06 — offen

Noch nicht angeschaut. **Was wir dazu schon haben:** Ping und CheckInteroperability laufen
(`ElmTransmitterClient`, SOAP 1.1, leere SOAPAction). Ping ist ausschliesslich manuell auslösbar —
nie automatisiert, nie zyklisch (Transmitter-Richtlinien Kap. 4). CheckInteroperability wird
erwartungsgemäss mit `Client.security` abgewiesen, solange das Transmitter-Zertifikat fehlt.

---

## F02 Sicherheit · F03 Interoperabilität · F04 Archivierung · F05 Übermittlung · F06 Validierung · F07 SUA-Zertifikat · F08 Prozesse

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
