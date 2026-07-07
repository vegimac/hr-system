# Pendenzen / Backlog

Offene, bewusst zurückgestellte Punkte (noch NICHT umgesetzt — status quo).
Reihenfolge ohne Priorisierung; Datum = erfasst am.

## Befristete Verträge & Probezeit (erfasst 30.06.2026)

Hintergrund: Bei einem befristeten Vertrag ist eine Probezeit rechtlich
grundsätzlich nicht zulässig. Walter stellt ab **1.7.2026** intern keine
befristeten Verträge mehr aus; die bereits laufenden befristeten Verträge
(noch ~6–7 Monate) behalten ihre Probezeit.

Aktueller Stand im Code:
- Die Auto-Probezeit setzt weiterhin bei ALLEN Erstverträgen eine Probezeit
  (auch befristet) — die Regel „befristet → keine Probezeit" liegt als
  inaktiver Schalter `SkipProbationForBefristet = false` bereit
  (`EmploymentsController.Create` + `EasyAtWorkEmployeeSyncService` Import-Anker).

Pendent:
1. **Dashboard-Warnung bei befristetem Neu-Vertrag ab 1.7.2026.** Wird nach dem
   1.7.2026 ein NEUER (Erst-)Vertrag importiert/angelegt, der befristet ist
   (ContractType „befristet" oder Enddatum gesetzt, ContractStartDate ≥ 1.7.2026),
   eine Warnung auf dem Dashboard zeigen („Befristeter Vertrag entgegen interner
   Regel ab 1.7.2026"). Nur Hinweis, kein Block.
2. **Befristung später ganz eliminieren.** Sobald Verträge ausschliesslich in
   diesem System ausgestellt werden (keine befristeten mehr im Umlauf):
   - `SkipProbationForBefristet` auf `true` setzen (befristet → keine Probezeit), und/oder
   - die Befristungs-Option in der Vertrags-Erfassung entfernen.

---

## OneCrew Moments — Link-Gültigkeit pro Filiale konfigurierbar (Walter 01.07.2026)

Aktueller Stand: Der Moment-Token-Link (`moment.html?t=…`) läuft **fix nach 30 Tagen**
ab dem Erstellen ab — hartcodiert in `MomentsController.Create`
(`ExpiresAt = DateTime.Now.AddDays(30)`). Nach Ablauf zeigt die Seite „Abgelaufen".
Öffnen/Lesen ändert die Lebensdauer nicht.

Pendent: Die Gültigkeitsdauer (Tage) als **Filial-Einstellung** hinterlegen
(analog anderer `CompanyProfile`-Felder), im Filial-Einstellungen-Tab pflegbar,
und in `MomentsController.Create` statt der fixen 30 Tage aus der Filiale des MA
lesen (Fallback 30 Tage, wenn nicht gesetzt).

---

## MA-Postfach: Biometrisches Login (Face ID / Fingerprint) via WebAuthn/Passkeys (Walter 01.07.2026)

Ziel: MA meldet sich im Postfach per Face ID / Touch ID / Android-Fingerprint an,
statt Passwort zu tippen. Technik = **WebAuthn / Passkeys** (der Browser kann den
Sensor nicht direkt lesen). Private Schlüssel + Biometrie bleiben im Gerät; Server
speichert NUR den öffentlichen Schlüssel — keine biometrischen Daten serverseitig
(DSG-freundlich, phishing-resistent).

Fixe Entscheidungen:
- **RP-ID / produktive Domain = onecrew.ch** (Passkeys sind domaingebunden;
  test.hr-srgmbh.ch bräuchte eigene Registrierung).
- **Passwort bleibt immer als Rückfall** — Biometrie ist Zusatz, kein Ersatz.

Umsetzung (3 Etappen):
1. Backend: Tabelle `employee_webauthn_credential` (app_user_id, credential_id,
   public_key COSE, sign_count, transports, device_label, created_at, last_used_at);
   kurzlebige Challenges (Replay-Schutz); Library **Fido2 (fido2-net-lib)**;
   4 Endpunkte: register-begin/complete, login-begin/complete. JWT identisch zum
   Passwort-Login.
2. Postfach-UI: „Face ID aktivieren" (nach Passwort-Login, Opt-in pro Gerät) +
   „Mit Face ID anmelden" auf dem Login-Screen.
3. Geräteverwaltung: mehrere Geräte pro MA, einzeln entfernbar, Gerätename;
   HR kann Credentials bei Geräteverlust löschen.

Hinweise: HTTPS Pflicht (vorhanden); iOS 16+ synct Passkeys über iCloud-Keychain
auf weitere Apple-Geräte des MA; HR-Passwort-Reset killt Passkey nicht automatisch.

---

## Absenz „frei" (Mirus-Code „FR") + automatische Frei-Tag-Erkennung (Walter 04.07.2026)

Hintergrund: Der Mirus-Absenz-Import (`RosterAbsenceImportController`) meldet den
Code **„FR"** aktuell als „Code unbekannt — wird nicht importiert". „FR" bedeutet
**frei** (freier Tag). Das ist kein Fehler — der Code wird bewusst nicht als Absenz
importiert.

Fixe Entscheidungen von Walter:
- **Beim Import mit „FR" nichts machen** — nicht als Absenz übernehmen (Status quo
  = überspringen ist korrekt; die „unbekannt"-Meldung ist nur kosmetisch unschön).
- Es gibt **noch keinen Absenz-Typ „frei"** — muss noch angelegt werden.
- Die Absenz „frei" wird **ausschliesslich für die Arbeitszeit-Kontrolle** gebraucht
  (Prüfung, ob der MA genügend Freitage hatte). Sie ist KEINE normale Absenz für den
  Lohnlauf.

Pendent (später umzusetzen):
1. **Absenz-Typ „frei" anlegen** — eigener Typ, nur für die Arbeitszeitkontrolle,
   nicht lohnrelevant.
2. **Automatische Frei-Tag-Erkennung:** Hat ein MA an einem Tag **keine Stempelzeit**
   UND ist an diesem Tag **keine Absenz** erfasst, gilt der Tag als **frei**.
   Für diese Tage die Absenz „frei" im Hintergrund hinterlegen (für die Kontrolle),
   aber **NICHT in der Absenzen-Liste des MA anzeigen** — sie dient nur der
   Arbeitszeit-/Freitage-Auswertung.
3. Optional: „FR" im Importer als bekannten, bewusst übersprungenen Code führen
   (neutrale Info statt „Code unbekannt").
