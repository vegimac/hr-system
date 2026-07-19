# SMS-Versand & Vertrags-Link

OneCrew verschickt SMS über den Schweizer Anbieter **eCall (F24)** — für Moments, Postfach-Hinweise, den **Arbeitsvertrag-Link** und die **Bewilligungs-Erinnerung**. Diese Seite erklärt den Versand und den sicheren Vertrags-Link.

## Arbeitsvertrag per SMS senden

Im **Mitarbeiter-Detail** hat jeder Vertrag in der Vertrags-Leiste drei Aktionen: **Anschauen · Drucken · SMS**.

Klick auf **SMS**:

1. Rückfrage: „Vertrag per SMS an +41 79 … wirklich senden?" — inkl. Hinweis, ob bereits gesendet wurde und ob der MA den aktuellen Link schon geöffnet hat.
2. Bei OK erzeugt das System einen **persönlichen Link** (gültig 14 Tage) und schickt ihn per SMS an die Mobilnummer des MA.
3. Der MA öffnet den Link und sieht eine neutrale Seite mit dem Button **„Arbeitsvertrag öffnen"** — erst der Klick lädt das PDF. So erscheint der Vertragsinhalt nie als Vorschau im Chat.

Auf der Seite steht fest: *„Dieser Vertrag dient zur Vorbereitung. Die Unterzeichnung erfolgt vor Ort."*

### Sicherheit des Vertrags-Links

- **14 Tage gültig** — danach automatisch deaktiviert („Link abgelaufen").
- **Neuversand entwertet alte Links** — es ist immer nur der zuletzt gesendete Link gültig.
- **Manuell widerrufen:** Button **„Link ⊘"** neben dem SMS-Button macht alle aktiven Links dieses Vertrags sofort ungültig.
- **Öffnungs-Protokoll:** das System merkt sich, wann die Seite geöffnet und wann das PDF abgerufen wurde — du siehst es in der Rückfrage beim erneuten Senden.
- **Kein Login, keine weiteren Daten** — der Link zeigt ausschliesslich das Vertrags-PDF.
- **Keine sensiblen Angaben in der SMS:** der SMS-Text enthält nur Name + Link. Enthält die Vorlage versehentlich z.B. einen Lohnbetrag, blockt das System den Versand.

### Text der Vertrags-SMS anpassen

Der SMS-Text und der Text auf der Link-Seite kommen aus der Moments-Vorlage **VERTRAG_LINK** (Moments → Texte verwalten). Platzhalter: `{Vorname}`, `{Firma}`, `{Link}`, `{GueltigBis}`, `{Briefanrede}`, `{SenderName}`.

## Bewilligung abgelaufen — Erinnerung per SMS

Im Tab **Bewilligung QST Bank** zeigt jede **abgelaufene** Bewilligung einen **SMS**-Button (gleicher Look wie bei den Verträgen).

Klick auf **SMS**:

1. Rückfrage mit Handynummer und dem fertigen SMS-Text.
2. Bei OK geht die Erinnerung über eCall an die Mobilnummer des MA.
3. Ohne Handynummer ist der Button grau/gesperrt — Nummer zuerst im Personal-Tab erfassen.

Standardtext (anpassbar): *«Hallo {Vorname}, deine Bewilligung ({PermitCode}) ist am {GueltigBis} abgelaufen. Kannst du bitte die neue Bewilligung so bald wie möglich bei HR nachreichen? Danke!»*

Vorlage **BEWILLIGUNG_ABGELAUFEN** unter Moments → Texte verwalten. Platzhalter: `{Vorname}`, `{PermitCode}`, `{GueltigBis}`.

## Test-Umleitung — gefahrlos testen

Solange in **Systemeinstellungen → SMS (eCall)** im Feld **„Test-Umleitung"** eine Nummer steht, gehen **alle** SMS (Moments, Vertrag, Bewilligung, Postfach-Hinweis) an diese Nummer statt an die MA — im Text steht `[TEST → Originalnummer]`. Die Erfolgsmeldung im Programm weist mit ⚠ darauf hin.

Für den **Echtbetrieb**: Feld leeren und speichern.

## Konfiguration (nur Admin)

**Systemeinstellungen → SMS (eCall):**

- **SMS-Versand aktiv** — Hauptschalter.
- **API-Benutzer + API-Passwort** — die eCall-Zugangsdaten (Passwort wird verschlüsselt gespeichert).
- **Absender** — was der MA als Absender sieht. Bis 16 Ziffern ODER bis 11 Buchstaben (z.B. «OneCrew»). ⚠ Bei eCall-Free-Accounts ist nur die registrierte Handynummer erlaubt; für «OneCrew» braucht es einen bezahlten Account.
- **Test-Umleitung** — siehe oben.
- **Test-SMS senden** — prüft die gespeicherte Konfiguration sofort.

## SMS-Protokoll

Jeder Versandversuch wird in der Datenbank protokolliert (Zweck, MA, Nummer, erfolgreich/fehlgeschlagen, eCall-Message-ID). Ein Zustell-Status („beim Empfänger angekommen") ist als Ausbaustufe vorgesehen.

## Häufige Fragen

**Der MA hat die SMS nicht bekommen — was tun?**
Erst prüfen, ob die **Test-Umleitung** noch aktiv ist (dann ging die SMS an die Test-Nummer). Danach Mobilnummer im Personal-Tab prüfen. Die Fehlermeldung beim Senden nennt sonst den Grund (z.B. ungültige Nummer, kein Guthaben).

**Kann ich den Link auch ohne SMS weitergeben?**
Ja — nach dem Widerruf/Neuversand zeigt die Box den Link; du kannst ihn kopieren und z.B. per WhatsApp senden. Die 14-Tage-Gültigkeit gilt genauso.

**„SMS-Versand fehlgeschlagen: InvalidContent — From: … is an invalid sender"?**
Der eingetragene Absender ist für euren eCall-Account nicht freigeschaltet — Absender auf die registrierte Nummer stellen oder den Account upgraden.
