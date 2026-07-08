# Rollen & Berechtigungen

Im Programm gibt es **sechs Rollen**. Jede sieht eine andere Welt — vom kompletten Admin-Zugriff bis hin zum Mitarbeiter, der nur sein eigenes Postfach öffnen kann.

💡 Auch **dieses Handbuch** ist rollenbasiert: jeder Benutzer sieht nur die Kapitel der Programmteile, zu denen er berechtigt ist.

## Die sechs Rollen

| Rolle | Wer ist das typischerweise | Was sieht sie |
|---|---|---|
| **admin** | Walter (Geschäftsinhaber) | ALLES. Über alle Filialen. Auch Systemeinstellungen, Audit-Log, Benutzerverwaltung. |
| **superuser** | HR-Verantwortliche | Wie admin, aber ohne Systemeinstellungen. Über alle Filialen. |
| **user** | Filial-Geschäftsführer | Nur die zugewiesenen Filialen. Kein HR-Modul, kein Admin-Bereich. |
| **buchhaltung** | Buchhaltungs-Personal | Wie superuser PLUS Fibu-Bereich. Aber auf zugewiesene Filialen beschränkt. |
| **lowuser** | Eingeschränkter Benutzer | Nur Dashboard, Mitarbeiter und Verträge. Kein Lohnlauf, kein HR-Bereich, keine Systemeinstellungen, kein Datenimport. |
| **employee** | Mitarbeiter selbst | Nur das eigene Postfach + eigene Lohnzettel. Kein Programm-Zugang. |

## Was darf wer im Lohnlauf?

| Aktion | user (GF) | superuser / buchhaltung (HR) | admin |
|---|:---:|:---:|:---:|
| Akonto vorbereiten | ✓ | ✓ | ✓ |
| Akonto pro MA freigeben | ✓ | ✓ | ✓ |
| An HR senden | ✓ | ✓ | ✓ |
| Akonto HR-bestätigen | – | ✓ | ✓ |
| DTA auszahlen | – | ✓ | ✓ |
| Periode wieder öffnen (vor Zahldatum) | – | – | ✓ |
| Periode wieder öffnen (nach Zahldatum) | – | – | nicht möglich |

Das 4-Augen-Prinzip: GF bereitet vor und gibt frei, HR bestätigt und sendet. Admin kann im Notfall zurücksetzen.

## Was sieht jede Rolle in der Sidebar?

**admin** — alles.

**superuser** — Dashboard, Mitarbeiter, Verträge, Lohn, Lohnperioden, Posteingang, HR-Modul (RAV, QST-Anmeldung, Lohnausweis, BFS-LSE), aber **keine** Systemeinstellungen.

**user** (GF) — Dashboard, Mitarbeiter, Verträge, Lohn, Posteingang. Filtert automatisch auf die zugeteilten Filialen.

**buchhaltung** — wie superuser, plus zusätzlich der **Buchhaltungs-Bereich** (Fibu-Journal, Saldo-Listen). Filtert ebenfalls auf zugeteilte Filialen.

**lowuser** — nur Dashboard, Mitarbeiter, Verträge. Für Personen, die Stammdaten nachschlagen, aber nichts mit Lohn zu tun haben.

**employee** — sieht nichts vom Programm. Logt sich auf einer separaten Postfach-Seite ein und sieht nur seine eigenen Lohnzettel + Mitteilungen.

## Filial-Beschränkung

Bei `user` und `buchhaltung` regelt die Tabelle **Benutzerverwaltung → User-Filialen-Zugang**, welche Filialen er sieht. Admin und Superuser sehen immer alle Filialen.

Das Filial-Selektor-Dropdown in der Sidebar zeigt nur die Filialen, auf die der User Zugriff hat. Wechselst du die Filiale, filtern alle Listen automatisch.

💡 **„Alle ausser X"-Workaround:** Wenn ein GF Zugriff auf alle Filialen ausser eine haben soll, hakst du im Filial-Zugang einfach alle anderen an (Positiv-Liste).

## Wer kann was im Admin-Bereich?

Diese Seiten sind **nur für admin** sichtbar UND editierbar:

- Benutzerverwaltung
- SV-Sätze (AHV, ALV, BVG…)
- Mindestlöhne (L-GAV)
- Lohnpositionen
- Kontoplan (Fibu)
- QST-Tarife
- Familienzulagen-Tarife
- Absenz-Typen
- Behörden (Betreibungsämter etc.)
- Banken (Stammdaten)
- Audit-Log

Wenn ein `superuser` versucht eine dieser Seiten zu schreiben, gibt es einen 403-Fehler. Auch wenn er die URL kennt.

## Spezial-Konstruktion: buchhaltung-Rolle

Damit die Buchhaltung **alles wie superuser** machen kann PLUS Zugriff auf den Fibu-Bereich, hat sie technisch **zwei Rollen-Claims** im JWT:

- `buchhaltung` (Hauptrolle, in der DB hinterlegt)
- `superuser` (zusätzlich, aktiviert alle HR-Endpoints)

Damit greifen alle bestehenden HR-Berechtigungen automatisch, und zusätzlich der Fibu-Bereich nur für `buchhaltung`. Die Filial-Beschränkung greift trotzdem (nicht „alle" wie ein echter Superuser).

## Mitarbeiter-Postfach (employee)

Wenn ein MA seine Lohnzettel selber abrufen können soll:

1. **Im MA-Detail** oben rechts auf **„Postfach-Passwort"** klicken.
2. Das System setzt das Passwort auf die **Personalnummer** des MA und merkt sich, dass der MA es beim ersten Login ändern muss.
3. Der MA logt sich auf `https://test.hr-srgmbh.ch/postfach` mit seiner Personalnummer + dem Initial-Passwort ein.
4. Beim ersten Login: zwingender Passwort-Wechsel.

Was der MA sieht: seine Lohnzettel (PDF) + Mitteilungen von HR (z.B. „Dein Lohn steigt").

## Passwort-Reset

**Eigenes Passwort:** Benutzerverwaltung → eigener Eintrag → „Passwort ändern".

**MA-Postfach-Passwort:** im MA-Detail oben → 🔓-Knopf „Postfach-Passwort". Setzt es zurück auf die Personalnummer + hebt eine evtl. Login-Sperre (zu viele falsche Versuche) gleich mit auf.

**User-Passwort eines anderen Users (admin only):** Benutzerverwaltung → User wählen → „Passwort setzen".

## Häufige Fragen

**Kann ich für jemanden Rollen mischen?**
Nein — pro User genau eine Rolle. Wenn jemand sowohl HR-Aufgaben als auch Buchhaltung machen muss, gib ihm `buchhaltung` — das deckt beides ab.

**Wie wird die Rolle „employee" zu einem User?**
Das passiert automatisch wenn du im MA-Detail das **Postfach-Passwort** setzt. Dann existiert ein `app_user`-Eintrag für den MA mit Rolle `employee`. Vorher gibt's keinen Login für ihn.

**Was ist mit 2FA?**
Aktuell nicht aktiviert. Steht auf der Sicherheits-Härtungs-Liste — wird kommen.

**Kann ich einen User deaktivieren statt löschen?**
Ja — in der Benutzerverwaltung das Feld „aktiv" ausschalten. Dann ist der Login gesperrt aber alle bisherigen Aktionen bleiben mit Namen erhalten (Audit-Log etc.).

## Häufige Stolpersteine

- **„Ich sehe den Lohnlauf nicht!"** → Rolle prüfen. `employee` sieht das nie. `user` sieht es nur wenn er Filial-Zugang hat.
- **„Mein GF kann den DTA nicht generieren"** → das ist richtig so. GF gibt nur frei und sendet an HR. Den DTA-Knopf hat nur HR (superuser/buchhaltung/admin).
- **„Buchhaltung sieht eine Filiale nicht"** → User-Filialen-Zugang prüfen. Auch wenn die Rolle auf „alle HR-Funktionen" zugreift, gilt die Filial-Beschränkung.
- **„Admin kann Lohnlauf nach Auszahlung nicht mehr öffnen"** → richtig. Nach DTA-Versand zur Bank ist die Periode endgültig. Wenn wirklich nötig, Daten direkt in der DB ändern (Entwickler-Notfall).
