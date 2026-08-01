# Mitarbeiter — der Personalbereich

Hier verwaltest du alle Mitarbeiter:innen deiner Filialen. Stammdaten, Verträge, Familie, Bank, Quellensteuer, Absenzen, Dokumente — alles auf einer Seite, jeder Bereich in einem eigenen Tab.

## Wo finde ich was?

Wenn du links in der Sidebar auf **Mitarbeiter** klickst, siehst du:

- **Links: Mitarbeiter-Liste** mit Suchfeld und den Filtern *Aktive · Inaktive · Alle*. Filiale wird oben durch den Sidebar-Selektor bestimmt.
- **Rechts: Mitarbeiter-Detail** — wechselnde Inhalte je nach gewähltem Tab.
- **Aktionen** (Passwort, Zeugnis, Probezeit, Face ID …) sitzen im Tab **Restaurant Admin** als Icon-Kacheln — nicht mehr im Detail-Header.
- **easy@work synchronisieren** (einzelner MA) sitzt oben rechts neben Sprache/Theme.

## Wie lege ich einen neuen Mitarbeiter an?

**Neue MA kommen ausschliesslich aus easy@work.** Der Ablauf ist immer:

1. **Zuerst in easy@work anlegen** (dort entsteht auch die Personalnummer).
2. In der Mitarbeiter-Liste auf **„＋ Neuer MA aus easy@work"** klicken. Es öffnet sich ein Fenster mit allem, was sich für deine Filiale geändert hat: **NEU** (noch nicht im System) und **UPDATE** (aktive MA mit Änderungen). Inaktive MA (Austritt in easy@work) werden **nie** angefasst.
3. Alles ist vorangehakt — abwählen, was (noch) nicht übernommen werden soll, dann **„Ausgewählte importieren"**. Stammdaten, Vertrag, Funktion und Lohnmodell kommen mit; der neue MA erscheint sofort in der Liste links.

Der Button steht allen HR-/GF-Rollen zur Verfügung (GF sieht nur seine Filialen). Der frühere **CSV-Import ist Vergangenheit** — bitte nicht mehr verwenden.

💡 **Vertraulicher Lohn (z.B. GF):** Ist in easy@work bewusst kein Lohn erfasst, importierst du den MA trotzdem und erfasst den Lohn danach **direkt im OneCrew-Vertrag** — der Import verändert solche Verträge nie. Details: [Verträge](#vertraege), Abschnitt „Vertraulicher Lohn".

**Stammdaten anreichern:** Bestehende MA bekommen über die Importer **GastroSocial-XLSX** und **Bewilligungsliste** zusätzlich AHV-Nr, Zivilstand, Sprache, Bewilligung etc.

💡 **Tipp:** Auch später geänderte Stammdaten (Adresse, Telefon …) holst du über denselben Stammdaten-Sync nach — die Vorschau zeigt sie als **UPDATE**-Zeilen, du wählst pro MA, was übernommen wird.

## Wie finde ich einen bestimmten Mitarbeiter?

- **Suchfeld** in der MA-Liste (Vor-/Nachname).
- **Filter** über dem Suchfeld: Aktive / Inaktive / Alle.
- **Globale Suche ⌘K** funktioniert von überall — tippe einfach den Namen, die MA-Nr oder die AHV-Nummer. Klick führt direkt zum MA.
- **Spezialfilter** (kleines Dropdown): „Keine Bewilligung", „QST fehlt", „Bankverbindung fehlt", „Austritt erfasst aber noch aktiv" — die Datenqualitäts-Checks.

## Die Tabs im MA-Detail

### Übersicht
Stammdaten auf einen Blick in Karten:

- **Personalien & Adresse** — Strasse, PLZ, Ort, Kanton, Telefon 2, AHV, Briefanrede, Kurzname, Sex, Konfession, Zivilstand, Ledigname, Nationalität, ZEMIS (Nicht-CH). Editierbare Felder speichern direkt in der Karte.
- **Anstellung** — Eintritt, Austritt, L-GAV, Kündigung am/per, &lt; 8 h/Wo., sowie **Probezeit bis** und **Probezeitgespräch**-Status (offen / erledigt). Bei «offen» öffnet **→ eintragen** das Probezeit-Modal — kein Direkt-Upload in der Karte (Protokoll erst nach Hand-Unterschrift scannen).
- **Nachtarbeit** — Pflicht-Badge, Nächte-Zähler, Arztzeugnis / Ausnahme-Regelung verknüpfen, Formulare drucken.
- **Verträge** — kompakte Liste mit Lohn/Pensum; darunter **Saldi** der aktuellen Periode.
- **Weitere Adressen** — falls vorhanden, unten in der Personalien-Karte.

### Familie / Schwanger
Ehepartner und Kinder. Jedes Kind hat eine eigene Karte mit Geburtsdatum und Alter. **Kinderzulagen** sind direkt unter dem Kind versioniert (z.B. 215 CHF/Monat ab 1.6.2025).

**Schwangerschaft / Mutterschaft** lebt ebenfalls hier (nur bei Frauen sichtbar): Schwangerschaft erfassen, errechneter Termin, Fahrplan (Arztbrief, Bestätigung, Geburt …), Fristen-Liste und Formulare. Aktive Schwangerschaft zeigt zusätzlich einen roten Badge **„🤰 Schwanger"** neben dem Namen im Header.

💡 **Wichtig für QST:** wenn der MA verheiratet ist, hat der Ehepartner direkten Einfluss auf den QST-Tarif (C statt A). Trag also Ehepartner sauber ein.

### Bewilligung QST Bank
Drei Blöcke in einem Tab (Reihenfolge: Bank oben, dann Bewilligung + QST):

1. **Bankverbindung** — versioniert, eine **Hauptbank**, optional Aufteilung (fixer Betrag / Prozent / „Netto abzüglich").
2. **Bewilligungen** — Verlauf mit Gültigkeit; abgelaufene Bewilligung kann per **SMS**-Erinnerung an den MA gehen (siehe [SMS & Vertrags-Link](#sms)).
3. **Quellensteuer** — Status-Banner + Erfassung / Behörden-Befreiung (siehe [Quellensteuer](#qst)).

### Restaurant Admin
Icon-Kacheln für den Filial-Alltag (GF, HR und Admin). Hier liegen die Aktionen, die früher im Header standen:

| Kachel | Was sie tut |
|---|---|
| **Verwarnung** | Verwarnung erfassen, Formular, Liste der bisherigen Verwarnungen |
| **Probezeit** | **Probezeit Gespräch**-PDF blanko; ein Gespräch mit Datum + Protokoll-Verknüpfung; Kündigung während der Probezeit |
| **Arbeitszeugnis / Zwischenzeugnis / Arbeitsbestätigung** | Zeugnis-Modal öffnen und PDF erzeugen |
| **Arbeits Aufforderung** | Formular «Aufforderung zur Arbeit» (bei unentschuldigtem Fernbleiben) — PDF erzeugen und ablegen |
| **Postfach-Passwort** | Setzt das MA-Postfach-Passwort auf die Personalnummer zurück und hebt eine Login-Sperre auf |
| **Onboarding-QR** | QR-Code für den ersten Postfach-Login des MA |
| **Face ID zurücksetzen** | Löscht alle Passkeys/Face-ID-Geräte des MA — er meldet sich wieder mit Passwort an |

#### Probezeit — Ablauf

1. Kachel **Probezeit** öffnen (oder in der Anstellung bei «offen» → **eintragen**).
2. **Probezeit Gespräch** generieren/drucken → Gespräch führen → unterschriebenes Protokoll scannen und als Dokument (Typ Probezeitgespräch) ablegen.
3. Im Modal: **Gesprächsdatum** setzen und das Protokoll **verknüpfen** (beides nötig).
4. Erst dann: Status **erledigt** in der Anstellung, und das Todo «Probezeitgespräch offen» verschwindet.
5. Bei Bedarf: **Kündigung während Probezeit** direkt aus dem Modal.

### Stempelzeiten
Anzeige der gestempelten Zeiten aus easy@work. **Nur lesend** — Korrekturen passieren in easy@work und kommen mit dem nächsten Sync automatisch rein (täglicher Auto-Sync; manueller Sync pro MA oben rechts oder in den Systemeinstellungen). Pro Woche siehst du das Total. Wenn das Wochentotal die Filial-Max-Stunden überschreitet, erscheint ein rotes ⚠.

### Absenzen / KTG/UVG
Krankheit, Unfall, Ferien, Feiertag, Schulung, Militär, Nacht-Kompensation. Pro Eintrag wählst du Tage und Ausfall-Prozent. Berechnete Stunden werden automatisch angezeigt.

💡 **Karenz-Visualisierung:** bei Krankheit/Unfall siehst du, wie viele Tage in der Karenzfrist liegen. Der **KTG/UVG-Tagessatz** wird automatisch nach Regel A oder B berechnet und erscheint hier sowie kompakt in der Übersicht.

### Verfügbarkeit
Wann der MA grundsätzlich einsetzbar ist (L-GAV-Anlage). Die Daten kommen aus **easy@work** und werden beim Einzel-MA-Sync («easy@work synchronisieren») gespiegelt — Anzeige read-only. Manuell erfasste Versionen bleiben unangetastet.

### Zulagen Abzüge Abtretung BVG
Vier Bereiche in einem Tab:
- **BVG-Zusatz-Mitgliedschaft** — versioniert. Pro MA einzeln (nicht automatisch FIX-M-gekoppelt).
- **Wiederkehrende Zulagen/Abzüge** — z.B. Diensthandy, Fahrpauschale, Vorschuss-Rückzahlung.
- **Lohnabtretungen** — Pfändung oder Sozialamt-Abtretung mit Freigrenze und Zielbetrag.

### Dokumente
**Alle PDFs, Bilder, Word-Dateien** zum MA. Links die Kategorien-Sidebar (Persönliche Angaben, Vertragsunterlagen, Lohn/Arbeitszeit, Absenzen, Mitarbeiterentwicklung, Ämter & Behörden).

- **Klick auf eine Datei** → Vorschau-Panel von rechts (PDF/Bild direkt; Word/Excel werden serverseitig nach PDF gewandelt).
- **⋮-Menü** pro Zeile → Bearbeiten, Herunterladen, Löschen (kein separater Stift/Mülleimer).
- **PDF drehen** ↺ ↻ direkt im Vorschau-Panel — wird gespeichert.

## Verträge-Leiste im MA-Detail

In der Übersicht (Verträge-Karte) bzw. im Verträge-Modul siehst du alle Verträge. Pro Vertrag: **Bearbeiten**, **Anschauen** (PDF-Vorschau), **SMS** (Arbeitsvertrag als sicheren Link aufs Handy — mit Rückfrage) und **Link ⊘** (verschickte Links widerrufen). Details: [SMS & Vertrags-Link](#sms) und [Verträge](#vertraege).

## Wie trage ich einen Austritt / eine Kündigung ein?

- **Kündigung am / Kündigung per** — direkt in der Übersicht → Anstellung-Karte (Datumsfelder).
- **Vollständiger Austritt** — über den Kündigungs-/Austritts-Flow (u.a. aus Restaurant Admin / Probezeit oder dem Kündigungs-Modul). Das System rechnet Kurzperiode und Ferien-Restanspruch.

⚠️ **Achtung:** Setze den MA nicht selbst auf inaktiv, solange der Austrittsmonat noch nicht durch den Lohnlauf gelaufen ist. Das System verwaltet das selbst.

## Phantom-MA (Supervisor ohne Lohn)

Wenn jemand nur einen easy@work-Zugang braucht aber **kein Lohn** über euch läuft (z.B. ein Bezirks-Supervisor), setze beim Erfassen das Häkchen **„MA ohne Lohn"**. Dann werden Bank, persönliches Postfach, Familie und Bewilligungs-Verlauf ausgeblendet. Im Lohnlauf taucht der MA gar nicht erst auf. Im Restaurant-Admin-Tab fehlen die Lohn-/Konto-Kacheln.

## Häufige Fragen

**Wo trage ich die Nationalität ein?**
In der Übersicht → Personalien (Edit-Felder). Wähle aus der Liste — das System speichert den ISO-Code (CH, DE, MK …) und zeigt überall den Volltext.

**Was bedeuten die farbigen Punkte vor dem Namen?**
- 🟢 Aktiv
- 🔴 Inaktiv (Austritt erfasst und Datum erreicht)
- Daneben das Vertragsmodell-Tag: **FLEX · MTP · FIX · FIX-M**

**Kann ich einen Mitarbeiter löschen?**
Nein — und das ist Absicht. Stattdessen markierst du ihn als inaktiv. Lohnzettel, Verträge, Dokumente bleiben für mindestens 10 Jahre erhalten (gesetzliche Aufbewahrungspflicht).

**Warum darf ich plötzlich keine Absenz mehr bearbeiten?**
Sobald der Akonto- oder Definitivlauf der entsprechenden Periode läuft, sind alle lohnrelevanten Daten **gesperrt** — damit nichts unter den Füssen des Lohnlaufs wegrutscht. Stempelzeiten sind ohnehin immer read-only (nur via easy@work). Erst wenn der Lauf abgeschlossen ist (oder du den Akonto-Lauf zurücksetzt), kannst du wieder editieren.

**Wer hat was geändert?**
Admin-Funktion: Sidebar → System → **Aktivitäts-Log**. Filterbar nach Person, Zeitraum, Tabelle.

## Häufige Stolpersteine

- **„Lohnlauf gesperrt — Mindestlohn unterschritten":** Vertrag öffnen, Lohn auf den L-GAV-Mindestsatz anheben.
- **„QST-Pflicht offen":** im Tab Bewilligung QST Bank den Höchsten Tarif setzen (3 Sekunden) — Lohnlauf läuft danach durch.
- **„Bankverbindung fehlt":** im selben Tab eine IBAN erfassen, sonst kann die DTA-Datei keine Zahlung an die Bank schicken.
- **Probezeitgespräch „offen" trotz Gespräch:** Datum *und* Protokoll-Dokument müssen verknüpft sein — nur eines von beiden reicht nicht.
- **Geburtsdatum oder AHV-Nr im falschen Format:** AHV ist `756.xxxx.xxxx.xx`, Datum `TT.MM.JJJJ`.

---

💡 **Tipp:** Die globale Suche **⌘K** (Mac) oder **Ctrl-K** (Windows) ist dein bester Freund — du findest Mitarbeiter über Name, MA-Nr, AHV-Nr und sogar Dokument-Inhalte ohne durch die Liste zu scrollen.
