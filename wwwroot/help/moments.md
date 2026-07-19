# Moments — persönliche Mitteilungen an Mitarbeitende

Mit **Moments** schickst du Mitarbeitenden eine persönliche Nachricht aufs Handy — Geburtstagsgruss, Arbeitsjubiläum, Wertschätzung, „Willkommen zurück". Der MA bekommt eine SMS mit einem **Einmal-Link ohne Login** und liest die Mitteilung im Browser.

## Wo finde ich was?

Sidebar **Moments** → links das Formular, rechts die Vorschau und der Verlauf.

## So sendest du einen Moment

1. **Moment-Typ** wählen (Geburtstag, Arbeitsjubiläum, Wertschätzung …).
2. **Emotionsgrad** wählen (Herzlich, Persönlich …) — daraus wird die passende **Vorlage** geladen und die Textfelder werden vorausgefüllt.
3. **Mitarbeiter/in** wählen. Die grüne Zeile darunter zeigt, ob der MA Moments **freigegeben** hat und welche Kategorien erlaubt sind.
4. **Absender** (deine Signatur in der Mitteilung) und **Antwortart** („Nur lesen" oder „Ja/Nein-Antwort").
5. **Vorschau prüfen** → rechts siehst du SMS-Text und Mitteilung genau so, wie sie ankommen.
6. **Moment senden** — die SMS geht direkt über eCall an die Mobilnummer des MA.

Nach dem Senden zeigt die grüne Box **„📲 SMS gesendet an …"**. Scheitert die SMS (z.B. keine Mobilnummer), bleibt der Moment trotzdem bestehen — du kannst den Link kopieren und anders übergeben.

## Platzhalter in den Vorlagen

| Platzhalter | Wird ersetzt durch |
|---|---|
| `{Briefanrede}` | Die gepflegte Briefanrede des MA („Liebe Eleni"), sonst automatisch aus Geschlecht + Vorname |
| `{Vorname}` | Vorname |
| `{Years}` | Vollendete Dienstjahre (für Jubiläen) |
| `{SenderName}` | Dein Absender-Feld |

⚠️ Kann ein Pflicht-Platzhalter nicht befüllt werden (z.B. `{Years}` ohne Eintrittsdatum), wird der Moment **nicht gesendet** — du bekommst eine klare Meldung.

## Freigabe (Consent) — ohne Zustimmung kein Moment

Moments sind Opt-in. Der MA muss OneCrew Moments **freigegeben** haben, und die Unterkategorie (Geburtstag & Jubiläum / Wertschätzung / Willkommen zurück & Fürsorge) muss erlaubt sein. Fehlt die Freigabe, blockt das System den Versand mit einer Meldung.

## Keine sensiblen Inhalte

Moments sind für **schöne, persönliche** Nachrichten. Das System blockt Texte mit sensiblen Begriffen (Lohn, Vertrag, Krankheit, Kündigung, IBAN …) automatisch. Administrative Mitteilungen gehören in den **Postfach-Weg** (Posteingang → Postfach-Nachricht) — dort ist der MA eingeloggt und die Inhalte sind geschützt.

## Vorlagen pflegen

Unter **Systemeinstellungen → Moments-Texte** — Emotionsgrade und Text-Vorlagen (pro Moment-Typ und Emotionsgrad: Titel, SMS-Kurztext, Mitteilung). Dort liegen auch die SMS-Vorlagen für Arbeitsvertrag-Link und Bewilligungs-Erinnerung. Deaktivierte Vorlagen erscheinen nicht mehr in der Auswahl.

## Häufige Fragen

**Wie lange ist der Einmal-Link gültig?**
30 Tage. Danach zeigt der Link „abgelaufen".

**Was sieht der MA genau?**
Nur die Mitteilung — keine Programm-Daten, keine Dokumente, kein Login.

**Warum kann ich für einen MA keinen Moment senden?**
Er hat Moments nicht (oder nicht für diese Kategorie) freigegeben — die Meldung sagt es dir. Alternative: Postfach-Nachricht.

**Geht die SMS wirklich an den MA?**
Solange in Systemeinstellungen → SMS (eCall) eine **Test-Umleitung** hinterlegt ist: nein — dann gehen ALLE SMS an die Test-Nummer (siehe [SMS & Vertrags-Link](#sms)). Für den Echtbetrieb das Feld leeren.
