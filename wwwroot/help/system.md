# System (Admin)

Die Sidebar **System** ist die Schaltzentrale. Nur Rolle **admin**. Hier pflegst du Kataloge, Benutzer, Importer und Schnittstellen.

Die Seite ist in **sechs Hauptkategorie-Kacheln** gegliedert: **Lohn-Stammdaten**, **Filialen & Benutzer**, **Verzeichnisse & Vorgaben**, **Import & Schnittstellen**, **Kommunikation** und **Kontrolle & Datenpflege**. Ein Klick auf die Kategorie zeigt die Karten darunter; die Auswahl bleibt gespeichert. Auf jeder Unterseite führt der **«← Zurück»-Button** neben dem Titel wieder hierher.

## Karten-Übersicht (was wofür)

| Karte | Zweck |
|---|---|
| **Benutzer** | Accounts, Rollen, Filial-Zugang, Unterschrift, Flag «Mirus-Änderungsmail», Bereiche ein-/ausblenden |
| **Lohnperioden** | Perioden anlegen, Bemerkungen, Akonto-Reset, Definitiv wieder öffnen, DTA nochmals holen |
| **Filialen** | Stammdaten, Unterzeichner, Einstellungen — siehe [Filialen](#filialen) |
| **SV-Sätze** | AHV, ALV, NBU, KTG, BVG, FAK … inkl. Höchstlohn / Min / AG-Satz (versioniert) |
| **Mindestlöhne** | L-GAV-Matrix — siehe [Mindestlöhne](#mindestloehne) |
| **Lohnpositionen** | Codes für Zulagen/Abzüge |
| **Kontoplan (Fibu)** | Welche Lohnart auf welches Konto bucht |
| **Warnungen** | Welche Dashboard-To-dos an/aus, Vorlauf, Schwere |
| **QST-Tarife** | Kantonale Tarifdateien |
| **Familienzulagen-Tarife** | FAK-Sätze |
| **Absenz-Typen** | Katalog Krank/Ferien/… |
| **Behörden** | Stamm für [Lohnabtretungen](#lohnabtretungen): Adresse, IBAN, Sachbearbeiter, optional Kontoinhaber = andere Behörde (für DTA, z.B. ORS Burgdorf → Zürich) |
| **Ärzte** | Für Mutterschutz-Briefe |
| **Mutterschafts-Regeln** | Gesetzliche Fristen (ArG/OR) |
| **Dokument-Struktur** | Kategorien und Dokumenttypen |
| **Dokumenten-Audit** | Verdächtige Dateinamen (falsche Filiale?) |
| **Globale Daten** | Banken, Nationen, PLZ/Gemeinden |
| **Aktivitäts-Log** | Wer hat was geändert — [Audit](#audit) |
| **easy@work API** | Verbindung, Syncs, Diagnose — [easy@work](#easyatwork) |
| **d.velop Import** | Alte Personalakten (CSV+ZIP) |
| **Saldi-Vortrag** | Eröffnungssaldi manuell |
| **Neue Filiale importieren** | Migrations-Hub — [Onboarding](#onboarding) |
| **E-Mail (SMTP)** | Lohnzettel-Mails, Test-Umleitung |
| **SMS (eCall)** | Credentials, Test-Umleitung — [SMS](#sms) |
| **Moments-Texte** | Vorlagen für Moments, Vertrags-Link, Bewilligungs-SMS |
| **Datenaufbewahrung** | Alte Stempelzeiten nach X Jahren löschen |
| **Postfach-Backfill** | Postfächer für alle aktiven MA anlegen |

## Faustregeln

- **Versionierte Kataloge** (SV, Mindestlohn): nie „heimlich" alte Sätze ändern, die schon im Lohn waren — immer **Folge-Version ab Datum**.
- Nach grossen Importen: Dashboard-To-dos und einen Test-Lohnlauf prüfen.
- SMTP/SMS: Solange **Test-Umleitung** aktiv ist, landet nichts beim echten Empfänger — gut zum Testen, gefährlich wenn man es vergisst.
