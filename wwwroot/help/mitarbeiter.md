# Mitarbeiter — der Personalbereich

Hier verwaltest du alle Mitarbeiter:innen deiner Filialen. Stammdaten, Verträge, Familie, Bank, Quellensteuer, Absenzen, Dokumente — alles auf einer Seite, jeder Bereich in einem eigenen Tab.

## Wo finde ich was?

Wenn du links in der Sidebar auf **Mitarbeiter** klickst, siehst du drei Bereiche:

- **Links: Mitarbeiter-Liste** mit Suchfeld und den Filtern *Aktive · Inaktive · Alle*. Filiale wird oben durch den Sidebar-Selektor bestimmt.
- **Mitte/Rechts: Mitarbeiter-Detail** — wechselnde Inhalte je nach gewähltem Tab.
- **Oben rechts: Postfach-Passwort · Bearbeiten · Austritt**

## Wie lege ich einen neuen Mitarbeiter an?

Drei Wege:

**1. Aus easy@work importieren** *(empfohlen — schnell und vollständig)*

→ Sidebar **Datenimport → Mitarbeiter & Verträge**. Du wirfst die CSV rein, das System macht den Rest: Stammdaten, Vertrag, Funktion, Lohnmodell — alles in einem Schwung. Mehrere MA in derselben Aktion.

**2. Einzeln neu erfassen**

→ Im Mitarbeiter-Tab kannst du oben rechts auf **„Neuer MA"** klicken. Du füllst Vorname, Nachname, Geburtsdatum, AHV-Nummer, Adresse aus. Vertrag wird separat im Vertragsmodul angelegt.

**3. Stammdaten anreichern**

Bestehende MA bekommen über die Importer **GastroSocial-XLSX** und **Bewilligungsliste** automatisch AHV-Nr, Zivilstand, Sprache, Bewilligung etc.

💡 **Tipp:** Die Personalnummer wird automatisch aus dem Filial-Schema vergeben (580001 für Oftringen, 750001 für Sursee usw.). Du musst nichts erfinden.

## Wie finde ich einen bestimmten Mitarbeiter?

- **Suchfeld** in der MA-Liste (Vor-/Nachname).
- **Filter** über dem Suchfeld: Aktive / Inaktive / Alle.
- **Globale Suche ⌘K** funktioniert von überall — tippe einfach den Namen, die MA-Nr oder die AHV-Nummer. Klick führt direkt zum MA.
- **Spezialfilter** (kleines Dropdown): „Keine Bewilligung", „QST fehlt", „Bankverbindung fehlt", „Austritt erfasst aber noch aktiv" — die Datenqualitäts-Checks.

## Die Tabs im MA-Detail

### Persönliche Angaben
Vorname, Nachname, Geburtsdatum, Geschlecht, AHV-Nr, Zivilstand, Konfession, Nationalität, Sprache, Adresse, Telefon, E-Mail, Eintrittsdatum.

**Darunter — drei Sub-Bereiche:**
- **Aufenthalt** *(nur bei nicht-CH-Bürgern):* Bewilligungstyp (B/C/L/G), gültig bis, ZEMIS-Nr. Versioniert — pro Verlängerung ein neuer Eintrag.
- **Weitere Adressen:** Korrespondenz, Wohnsitz Ausland, getrennt lebend, c/o Mutter — alles was nicht die Hauptadresse ist.
- **Persönliches Postfach:** falls der MA sich selbst einloggt um seine Lohnzettel zu sehen.

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
Anzeige der gestempelten Zeiten aus easy@work. **Nur lesend** — Korrekturen passieren in easy@work und werden neu importiert. Pro Woche siehst du das Total. Wenn das Wochentotal die Filial-Max-Stunden überschreitet, erscheint ein rotes ⚠.

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
- Daneben das Vertragsmodell-Tag: **UTP · MTP · FIX · FIX-M**

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
