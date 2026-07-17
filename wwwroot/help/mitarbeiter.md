# Mitarbeiter — der Personalbereich

Hier verwaltest du alle Mitarbeiter:innen deiner Filialen. Stammdaten, Verträge, Familie, Bank, Quellensteuer, Absenzen, Dokumente — alles auf einer Seite, jeder Bereich in einem eigenen Tab.

## Wo finde ich was?

Wenn du links in der Sidebar auf **Mitarbeiter** klickst, siehst du drei Bereiche:

- **Links: Mitarbeiter-Liste** mit Suchfeld und den Filtern *Aktive · Inaktive · Alle*. Filiale wird oben durch den Sidebar-Selektor bestimmt.
- **Mitte/Rechts: Mitarbeiter-Detail** — wechselnde Inhalte je nach gewähltem Tab.
- **Oben rechts: Postfach-Passwort · Bearbeiten · Austritt**

## Wie lege ich einen neuen Mitarbeiter an?

**Neue MA kommen ausschliesslich aus easy@work.** Der Ablauf ist immer:

1. **Zuerst in easy@work anlegen** (dort entsteht auch die Personalnummer).
2. In der Mitarbeiter-Liste auf **„＋ Neuer MA aus easy@work"** klicken. Es öffnet sich ein Fenster mit allem, was sich für deine Filiale geändert hat: **NEU** (noch nicht im System) und **UPDATE** (aktive MA mit Änderungen). Inaktive MA (Austritt in easy@work) werden **nie** angefasst.
3. Alles ist vorangehakt — abwählen, was (noch) nicht übernommen werden soll, dann **„Ausgewählte importieren"**. Stammdaten, Vertrag, Funktion und Lohnmodell kommen mit; der neue MA erscheint sofort in der Liste links.

Der Button steht allen HR-Rollen zur Verfügung (GF sieht nur seine Filialen). Der frühere **CSV-Import ist Vergangenheit** — bitte nicht mehr verwenden.

💡 **Vertraulicher Lohn (z.B. GF):** Ist in easy@work bewusst kein Lohn erfasst, importierst du den MA trotzdem und erfässt den Lohn danach **direkt im OneCrew-Vertrag** — der Import verändert solche Verträge nie. Details: [Verträge](#vertraege), Abschnitt „Vertraulicher Lohn".

**Stammdaten anreichern:** Bestehende MA bekommen über die Importer **GastroSocial-XLSX** und **Bewilligungsliste** zusätzlich AHV-Nr, Zivilstand, Sprache, Bewilligung etc.

💡 **Tipp:** Auch später geänderte Stammdaten (Adresse, Telefon …) holst du über denselben Stammdaten-Sync nach — die Vorschau zeigt sie als **UPDATE**-Zeilen, du wählst pro MA, was übernommen wird.

## Wie finde ich einen bestimmten Mitarbeiter?

- **Suchfeld** in der MA-Liste (Vor-/Nachname).
- **Filter** über dem Suchfeld: Aktive / Inaktive / Alle.
- **Globale Suche ⌘K** funktioniert von überall — tippe einfach den Namen, die MA-Nr oder die AHV-Nummer. Klick führt direkt zum MA.
- **Spezialfilter** (kleines Dropdown): „Keine Bewilligung", „QST fehlt", „Bankverbindung fehlt", „Austritt erfasst aber noch aktiv" — die Datenqualitäts-Checks.

## Die Tabs im MA-Detail

### Übersicht
Stammdaten auf einen Blick: Briefanrede, Kurzname, Ledigname, Adresse, AHV, Zivilstand, Konfession, Nationalität, ZEMIS (Nicht-CH), Telefon 2, Eintritt/Austritt, L-GAV / &lt;8h, Kündigung, Nachtarbeit, Verträge und kompakte KTG/UVG-Tagessatz-Karte. Editierbare Felder speichern direkt in der Karte.

**Weitere Adressen** sitzen in der Personalien-Karte. Bewilligung und Bank sind im Tab «Bewilligung QST Bank».

### Familie
Ehepartner und Kinder. Jedes Kind hat eine eigene Karte mit Geburtsdatum und Alter. **Kinderzulagen** sind direkt unter dem Kind versioniert (z.B. 215 CHF/Monat ab 1.6.2025).

💡 **Wichtig für QST:** wenn der MA verheiratet ist, hat der Ehepartner direkten Einfluss auf den QST-Tarif (C statt A). Trag also Ehepartner sauber ein.

### Bank
Versionierte Bankverbindungen für die Lohnzahlung. Eine ist die **Hauptbank** (Rest geht dahin), zusätzlich kann der MA aufteilen (fixer Betrag, Prozent, „Netto abzüglich").

Auch hier sitzt das Feld **Postfach-Passwort** (oben im Header) — Klick setzt es auf die Personalnummer zurück und entsperrt einen evtl. gesperrten Login.

### Quellensteuer
**Tab erkennt automatisch**, ob der MA QST-pflichtig ist. Wenn ja und keine Erfassung vorhanden:

- 🔴 **„Höchsten Tarif erfassen"** legt sofort den maximalen Tarif an (A0Y für Ledige, C0Y für Verheiratete). Lieber zu viel abziehen — zu wenig ist ein Verstoss.
- 📄 **„Behörden-Befreiung erfassen"** falls der MA ein offizielles Befreiungs-Schreiben der Steuerbehörde hat. Du lädst es als Dokument hoch und gibst den Gültigkeitsbereich an.

💡 **Lohnlauf-Sperre:** Wenn QST-pflichtig aber nicht erfasst → der Lohn lässt sich nicht abschliessen. Das Dashboard warnt dich rechtzeitig.

### Stempelzeiten
Anzeige der gestempelten Zeiten aus easy@work. **Nur lesend** — Korrekturen passieren in easy@work und kommen mit dem nächsten Sync automatisch rein (täglicher Auto-Sync über die easy@work-Schnittstelle; manueller Sync in den Systemeinstellungen). Pro Woche siehst du das Total. Wenn das Wochentotal die Filial-Max-Stunden überschreitet, erscheint ein rotes ⚠.

### Absenzen
Krankheit, Unfall, Ferien, Feiertag (ausbezahlt), Schulung, Militär, Nacht-Kompensation. Pro Eintrag wählst du Tage und Ausfall-Prozent. Berechnete Stunden werden automatisch angezeigt.

💡 **Karenz-Visualisierung:** bei Krankheit/Unfall siehst du, wie viele Tage in der Karenzfrist liegen.

### Zulagen & Abzüge
Drei Bereiche in einem Tab:
- **BVG-Zusatz-Mitgliedschaft** — versioniert. Pro MA einzeln (nicht automatisch FIX-M-gekoppelt).
- **Wiederkehrende Zulagen/Abzüge** — z.B. Diensthandy, Fahrpauschale, Vorschuss-Rückzahlung.
- **Lohnabtretungen** — Pfändung oder Sozialamt-Abtretung mit Freigrenze und Zielbetrag.

### KTG/UVG
Tagessatz-Berechnung für Krankentaggeld und Unfall — wird vom System automatisch nach Regel A oder B berechnet (12-Monats-Durchschnitt).

### Dokumente
**Alle PDFs, Bilder, Word-Dateien** zum MA. Links siehst du die Kategorien-Sidebar (Persönliche Angaben, Vertragsunterlagen, Lohn/Arbeitszeit, Absenzen, Mitarbeiterentwicklung, Ämter & Behörden).

- **Klick auf eine Datei** → Vorschau-Panel schiebt von rechts rein. Klick ausserhalb schliesst es.
- **Bleistift ✎** öffnet die Bearbeiten-Maske (Kategorie wechseln, Beschreibung, gültig-bis, anderen MA zuordnen).
- **PDF drehen** ↺ ↻ direkt im Vorschau-Panel — wird gespeichert.
- **Office-Dateien** (Word/Excel/PowerPoint) werden serverseitig nach PDF konvertiert und im selben Panel angezeigt.

## Verträge-Leiste im MA-Detail

Unter dem MA-Kopf siehst du alle Verträge als Leiste. Pro Vertrag: **Bearbeiten** (öffnet die Vertrags-Maske — z.B. um einen vertraulichen Lohn zu erfassen, Mindestlohn-Prüfung inklusive), **Anschauen** (PDF-Vorschau), **Drucken**, **SMS** (Arbeitsvertrag als sicheren Link aufs Handy des MA — mit Rückfrage) und **Link ⊘** (verschickte Links widerrufen). Details: [SMS & Vertrags-Link](#sms) und [Verträge](#vertraege).

## Wie trage ich einen Austritt ein?

Oben im MA-Header der 🛑-Button. Du gibst das **Austrittsdatum** ein. Das System rechnet automatisch:

- **Kurzperiode** falls der Austritt mitten im Monat liegt (Tagessatz × Kalendertage).
- **Ferien-Restanspruch** — was wird in der letzten Periode noch ausbezahlt.
- **Schweizer Standard:** Arbeitsverhältnis endet in der Regel auf Monatsende — das System schlägt dir „Ende aktueller Monat" / „Ende nächster Monat" als Schnellwahl vor.

⚠️ **Achtung:** Setze den MA nicht selbst auf inaktiv, solange der Austrittsmonat noch nicht durch den Lohnlauf gelaufen ist. Das System verwaltet das selbst.

## Phantom-MA (Supervisor ohne Lohn)

Wenn jemand nur einen easy@work-Zugang braucht aber **kein Lohn** über euch läuft (z.B. ein Bezirks-Supervisor), setze beim Erfassen das Häkchen **„MA ohne Lohn"**. Dann werden Bank, persönliches Postfach, Familie und Bewilligungs-Verlauf ausgeblendet. Im Lohnlauf taucht der MA gar nicht erst auf.

## Häufige Fragen

**Wo trage ich die Nationalität ein?**
Im Edit-Modus auf der Personalien-Seite. Wähle aus der Liste — das System speichert den ISO-Code (CH, DE, MK …) und zeigt überall den Volltext.

**Was bedeuten die farbigen Punkte vor dem Namen?**
- 🟢 Aktiv
- 🔴 Inaktiv (Austritt erfasst und Datum erreicht)
- Daneben das Vertragsmodell-Tag: **FLEX · MTP · FIX · FIX-M**

**Kann ich einen Mitarbeiter löschen?**
Nein — und das ist Absicht. Stattdessen markierst du ihn als inaktiv. Lohnzettel, Verträge, Dokumente bleiben für mindestens 10 Jahre erhalten (gesetzliche Aufbewahrungspflicht).

**Warum darf ich plötzlich keine Stempelzeit oder Absenz mehr bearbeiten?**
Sobald der Akonto- oder Definitivlauf der entsprechenden Periode läuft, sind alle lohnrelevanten Daten **gesperrt** — damit nichts unter den Füssen des Lohnlaufs wegrutscht. Erst wenn der Lauf abgeschlossen ist (oder du den Akonto-Lauf zurücksetzt), kannst du wieder editieren.

**Wer hat was geändert?**
Admin-Funktion: Sidebar → Systemeinstellungen → **Aktivitäts-Log**. Filterbar nach Person, Zeitraum, Tabelle.

## Häufige Stolpersteine

- **„Lohnlauf gesperrt — Mindestlohn unterschritten":** Vertrag öffnen, Lohn auf den L-GAV-Mindestsatz anheben.
- **„QST-Pflicht offen":** im QST-Tab den Höchsten Tarif setzen (3 Sekunden) — Lohnlauf läuft danach durch.
- **„Bankverbindung fehlt":** im Bank-Tab eine IBAN erfassen, sonst kann die DTA-Datei keine Zahlung an die Bank schicken.
- **Geburtsdatum oder AHV-Nr im falschen Format:** AHV ist `756.xxxx.xxxx.xx`, Datum `TT.MM.JJJJ`.

---

💡 **Tipp:** Die globale Suche **⌘K** (Mac) oder **Ctrl-K** (Windows) ist dein bester Freund — du findest Mitarbeiter über Name, MA-Nr, AHV-Nr und sogar Dokument-Inhalte ohne durch die Liste zu scrollen.
