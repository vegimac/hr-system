# MA-Postfach & SMS-Dienst — Übersicht (Stand 04.08.2026)

Bestandsaufnahme für Walter: wie das Mitarbeiter-Postfach und der SMS-Dienst
gelöst sind. Basis für die spätere Schulungsunterlage. Alles hier ist aus dem
Code verifiziert (Stand: Deploy 04.08.2026).

---

## Teil 1 — Das MA-Postfach

### 1.1 Konto und Login

Jeder Mitarbeiter bekommt **automatisch** ein Postfach-Konto, sobald er im
System angelegt wird (auch beim easy@work-Import). Es gibt nichts manuell
einzurichten.

| | |
|---|---|
| **Benutzername** | Personalnummer (z.B. `580003`) |
| **Initial-Passwort** | ebenfalls die Personalnummer |
| **Erster Login** | Passwortwechsel ist Pflicht (min. 8 Zeichen) |
| **Login-Adresse** | normale Login-Seite (test.hr-srgmbh.ch) — das System erkennt die MA-Rolle und leitet automatisch auf das Postfach weiter |
| **Sperre** | nach 5 Fehlversuchen 15 Minuten gesperrt |
| **Session** | MA: 15 Min Leerlauf / max. 30 Min (Backoffice: 30/480) |
| **Face ID / Touch ID** | MA kann im Postfach WebAuthn einrichten (Button im Postfach) |
| **Inaktiver MA** | Konto wird automatisch deaktiviert (Login gesperrt); HR sieht das Postfach weiterhin |
| **Phantom-MA** | MA ohne Lohn (`isPayrollExcluded`, z.B. Supervisor) haben **kein** Postfach |

**Passwort vergessen — zwei Wege:**

1. **HR/GF-Reset:** Im MA-Detail, Button neben «Bearbeiten» → setzt das
   Passwort zurück auf die Personalnummer, hebt eine allfällige Sperre gleich
   mit auf, Passwortwechsel beim nächsten Login wieder Pflicht.
2. **QR-Code-Selbstbedienung:** «Onboarding-QR» im Restaurant-Admin-Tab
   erzeugt einen QR-Code (72 h gültig, einmal verwendbar). MA scannt ihn,
   setzt sein Passwort selbst und ist direkt eingeloggt. Funktioniert für
   Onboarding UND Reset.

### 1.2 Was der MA sieht und darf

Die MA-Rolle (`employee`) ist standardmässig von **allem** ausgesperrt — nur
diese Funktionen sind explizit freigegeben, und jede prüft serverseitig die
Eigentümerschaft:

- eigenes Postfach lesen (Dokumente ansehen/herunterladen)
- eigene Dokumente an Filiale oder HR **hochladen** (max. 10 MB, nur
  Bilder/PDF) — mit «Gesendet»-Tab als Beleg
- eigenes Passwort ändern, Face ID verwalten, Sprache/Theme
- Moments-Einwilligung (SMS-Mitteilungen) selbst ein-/ausschalten

### 1.3 Was landet automatisch im Postfach?

| Auslöser | Inhalt |
|---|---|
| **Definitiv-Abschluss der Lohnperiode** | Lohnzettel-PDF **und** Stundenkontrollblatt-PDF pro MA |
| **Wieder-Öffnen der Periode** (Admin) | beide Dokumente werden automatisch wieder **entfernt** |
| **Mindestlohn-Anpassung** (optional angehakt) | Text-Mitteilung «Lohnanpassung per …» |
| **HR-Mitteilung via Moments** (Zustellweg «Postfach») | Text-Notiz + Push-SMS mit Postfach-Link |
| **Manueller Upload durch HR** | beliebige Dokumente (Posteingang → Upload, Ziel «Mitarbeiter») |

Bewusst **nicht** automatisch im Postfach: Arbeitsverträge (gehen per
SMS-Token-Link, siehe Teil 2), QST-Formulare, Lohnausweise.

### 1.4 Die Postfach-Typen (für HR)

Neben dem persönlichen MA-Postfach (`EMPLOYEE`) gibt es im Posteingang:

| Typ | Wer sieht es |
|---|---|
| `BRANCH` | alle Benutzer mit Zugang zur Filiale |
| `HR` | nur HR-Team-Mitglieder (Flag am Benutzer) + Admin |
| `BUCH` | Rolle Buchhaltung + Admin |
| `ADMIN` | nur Admin |
| `USER` | persönliches Benutzer-Postfach (ein bestimmter Empfänger) |

HR kann Posteingangs-Dokumente **zum MA verschieben** (wird zum
MA-Dokument in der Dokumentenverwaltung, Original-Uploader bleibt fürs
Protokoll erhalten), **weiterleiten** oder löschen.

### 1.5 E-Mails rund ums Postfach

- **Einzige E-Mail an MA:** «Dein Lohnzettel {Monat} ist bereit» beim
  Definitiv-Abschluss — nur wenn beim MA eine E-Mail-Adresse gepflegt ist
  (sonst wird er still übersprungen). Kein PDF im Anhang, nur Link zum
  Postfach.
- **Test-Modus:** Solange in den SMTP-Einstellungen eine
  Test-Umleitungsadresse gesetzt ist, gehen alle Mails dorthin (Betreff mit
  `[TEST → …]`). **Ausnahme:** die Dokument-Benachrichtigung an interne
  OneCrew-Benutzer geht auch im Testmodus scharf raus (bewusst, 04.08.2026).
- **Dokument-Benachrichtigung** (04.08.2026): Beim Ablegen eines Dokuments
  beim MA kann man interne Benutzer per Mail informieren (GF der Filiale +
  Buchhaltung vorangehakt, Mehrfachauswahl). Die Mail ist **anonymisiert**:
  Betreff nur «Neues Dokument für {Personalnummer}», kein MA-Name, kein
  Dateiname, keine Bemerkung — nur der Ablageort (Kategorie → Typ). Der
  Server blockiert Nachrichten, die den Vor- oder Nachnamen des MA enthalten.

Keine E-Mails gibt es bei: Konto-Anlage, Passwort-Reset, Moments-Mitteilung,
MA-Upload.

---

## Teil 2 — Der SMS-Dienst (eCall)

### 2.1 Grundsätzliches

- Provider: **eCall (F24 Schweiz)**, REST-API. Absender z.B. «OneCrew».
- Konfiguration in **Systemeinstellungen → SMS (eCall)** (nur Admin):
  Ein/Aus-Schalter, Zugangsdaten (Passwort verschlüsselt), Absender,
  Test-Umleitungsnummer.
- **Test-Modus:** Ist die Test-Umleitungsnummer gesetzt, gehen **alle** SMS
  dorthin (Text mit Präfix `[TEST → originalnummer]`). Anders als bei E-Mail
  gibt es hier keine Ausnahme. Leeres Feld = Echtbetrieb.
- **Protokoll:** Jeder Versand-Versuch (auch Fehlschläge) steht in der
  Tabelle `sms_log` mit Zweck, MA, Nummer, Umleitung, Erfolg/Fehler.
- SMS werden **nie automatisch** verschickt — alle vier Auslöser sind
  manuelle Aktionen im UI.

### 2.2 Die vier SMS-Auslöser

**1. Test-SMS** — Systemeinstellungen → SMS, nur Admin. Prüft die Anbindung.

**2. Arbeitsvertrag per SMS** («SMS»-Button beim Vertrag im MA-Detail):
- MA bekommt einen Token-Link (14 Tage gültig) auf sein Handy; die
  Landing-Page zeigt bewusst nur einen Button (kein Vertragsinhalt in der
  Messenger-Vorschau), das PDF erst nach Klick.
- Ein neuer Versand entwertet automatisch alle älteren Links desselben
  Vertrags.
- **Sensitiv-Wächter:** Enthält der SMS-Text Wörter wie Lohn, CHF, IBAN,
  AHV-Nr, Passwort, Krankheit …, wird der Versand verweigert.
- Ohne Handynummer beim MA: klare Fehlermeldung, kein Versand.

**3. Mitteilungen (Moments)** — zwei Zustellwege:
- **«Postfach»** (Standard): Text-Notiz ins MA-Postfach + Push-SMS «In
  deinem Postfach wartet eine neue HR-Nachricht» mit Link. Kommt auch ohne
  Handynummer an (dann halt nur im Postfach).
- **«Moment» (Direkt-Link)**: SMS mit Token-Link auf die Nachricht — nur
  für MA mit aktivem **Opt-in** (schaltet der MA selbst im Postfach), und
  sensible Kategorien (Lohn, Steuer, Bewilligung, Vertrag, Medizinisches …)
  sind auf diesem Weg hart gesperrt.

**4. Bewilligungs-Erinnerung** (Tab «Bewilligung QST Bank» im MA-Detail):
- SMS «Deine Bewilligung ist abgelaufen» mit Token-Link (14 Tage).
- Vorschau mit Zeichenzähler; über 160 Zeichen wird blockiert (keine
  Mehrfach-SMS-Kosten). Textvorlagen pflegbar in Systemeinstellungen →
  Moments-Texte.
- Harte Filial-Prüfung: nur Benutzer mit Zugang zur Filiale des MA dürfen
  senden (SMS kosten Geld + der Link enthält Personendaten).

### 2.3 Was es bewusst NICHT gibt

Kein SMS-Versand beim Lohnabschluss (dafür gibt es die E-Mail + das
Postfach), kein SMS beim Postfach-Onboarding oder Passwort-Reset (dafür der
QR-Code), keine automatischen/geplanten SMS-Jobs, keine Kosten-Limits im
System (die Kontrolle läuft über Ein/Aus-Schalter, Test-Umleitung und die
Guards oben).

---

## Teil 3 — Typische Abläufe (Basis für die Schulung)

**Neuer MA startet:** MA wird angelegt/importiert → Postfach-Konto existiert
automatisch → GF zeigt dem MA den Onboarding-QR (Restaurant-Admin) → MA
scannt, setzt Passwort, ist im Postfach. Alternativ: Login mit
Personalnummer/Personalnummer + Pflicht-Passwortwechsel.

**MA hat Passwort vergessen:** GF/HR klickt Reset im MA-Detail (Passwort =
Personalnummer, Sperre weg) oder erzeugt einen neuen QR.

**Lohnlauf abgeschlossen:** Lohnzettel + Stundenkontrollblatt liegen
automatisch im Postfach jedes MA; wer eine E-Mail-Adresse hat, bekommt die
Benachrichtigungs-Mail. MA unterschreibt das Stundenkontrollblatt und gibt
es dem GF zurück.

**HR will dem MA etwas mitteilen:** Moments → Zustellweg «Postfach» (Notiz +
Push-SMS). Für sensible Inhalte immer der Postfach-Weg, nie der Direkt-Link.

**Neuer Arbeitsvertrag:** Vertrag erfassen → «SMS»-Button → MA öffnet den
Link auf dem Handy und sieht das PDF.

**MA reicht ein Dokument ein** (Arztzeugnis, Bewilligung …): MA fotografiert
es im Postfach → Upload an Filiale oder HR → erscheint im Posteingang → HR
verschiebt es zum MA-Dossier.

**Dokument beim MA abgelegt:** Beim Ablegen Benachrichtigung an GF /
Buchhaltung anhaken — die Mail ist anonymisiert (nur Personalnummer).

---

*Bestehende Hilfe-Texte im Handbuch (`wwwroot/help/`): `postfach-ma.md`,
`sms.md`, `moments.md`, `dokumente.md` — inhaltlich deckungsgleich mit
diesem Stand und als Detail-Referenz für die Schulungsunterlage nutzbar.*
