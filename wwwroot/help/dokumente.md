# Dokumente & Posteingang

Hier landen alle PDFs, Bilder, Word-Dateien und Excel-Tabellen. Pro MA seine eigene digitale Personalakte, plus zentrale Postfächer für Filiale, HR und Buchhaltung.

## Die zwei Welten

| | Dokumente | Posteingang |
|---|---|---|
| **Wo?** | MA-Detail → Tab „Dokumente" | Sidebar → „Posteingang" |
| **Was drin?** | Persönliche Akte: Verträge, Stempelkarten, Arztzeugnisse, AHV-Bestätigung … | Eingangs-Schale für Filiale / HR / Buchhaltung |
| **Wer kann hochladen?** | Admin / Superuser / GF (user) / Buchhaltung | Alle eingeloggten User mit Filial-Zugang |
| **Was ist die Idee?** | Endgültige Ablage pro Person | Sammelstelle bevor sortiert wird |

Typischer Ablauf: GF lädt das Arztzeugnis ins **Posteingang** seiner Filiale → HR sieht es, sortiert es dem MA zu → es wandert in die **MA-Dokumente** rein.

## Die Dokumente eines Mitarbeiters

Sidebar **Mitarbeiter → MA wählen → Tab Dokumente.**

Links siehst du die **Kategorien-Sidebar:**
- **Persönliche Angaben** (AHV-Bestätigung, Ausweis-Kopien, Adressen, Bankkarten)
- **Vertragsunterlagen** (Arbeitsvertrag, Zusätze, Kündigungs-Schreiben)
- **Lohn / Arbeitszeit** (Lohnabrechnungen, Spesen, Stundenreports)
- **Absenzen** (Arztzeugnisse, Unfall-Meldungen, Ferien-Bestätigungen)
- **Mitarbeiterentwicklung** (Beurteilungen, Schulungs-Zertifikate)
- **Ämter & Behörden** (Steueramt, RAV, Betreibungsamt)

Klick auf eine Kategorie filtert die Liste. „Alle Dokumente" zeigt alles.

## Dokument anschauen

Klick auf den **Dateinamen** → das Vorschau-Panel schiebt von rechts rein. Was funktioniert:

- **PDF** — direkt im Browser, mit Zoom und Suchen.
- **Bilder** (JPG, PNG) — angezeigt.
- **Word / Excel / PowerPoint** — werden serverseitig in PDF gewandelt und im Panel angezeigt. Beim **Download** kriegst du das Original zurück.
- **Andere Typen** — kein Vorschau möglich, aber Download geht.

**Klick ausserhalb des Panels** → schliesst es.

💡 **Tipp:** Wenn das Vorschau-Panel zu schmal ist, kannst du es an der **linken Kante** breiter ziehen.

## PDF drehen

Bei PDFs hat das Vorschau-Panel zwei Pfeile **↺ ↻** im Header. Klick dreht die Seite um 90° und **speichert das Resultat**. Beim nächsten Öffnen ist's gedreht.

Wenn du nur EINE bestimmte Seite drehen willst (z.B. Seite 3 eines 5-seitigen PDF), gib die Seitenzahl im Feld vor den Pfeilen ein.

## Dokument bearbeiten

In der Liste das **⋮-Menü** rechts der Zeile → **Bearbeiten**. Du kannst:

- **Mitarbeiter wechseln** — Datei wird physisch in den neuen MA-Ordner verschoben.
- **Kategorie und Typ** umsortieren.
- **Gültig von / bis** — z.B. ein Arbeitszeugnis mit Gültigkeit.
- **Bemerkung** anpassen.

## Dokument hochladen

Oben rechts **„+ Dokument hochladen"**. Drag&Drop oder Datei-Browser.

1. **Datei wählen** (PDF / Bild / Word / Excel / PowerPoint, bis 50 MB).
2. **Kategorie + Typ** wählen.
3. **Gültig von / bis** falls relevant.
4. **Bemerkung** — kurzer Text was das ist.
5. **Speichern** → landet sofort in der Liste.

💡 **Tipp:** Du kannst auch direkt einen Posteingang-Eintrag in die Personalakte verschieben statt neu hochzuladen — siehe nächster Abschnitt.

## Posteingang — die Eingangs-Schale

**Sidebar → Posteingang.**

Pro Filiale gibt's einen eigenen Posteingang. Du wählst die Filiale oben in der Sidebar. Plus für admin/HR:

- **HR-Postfach** — zentral, sichtbar nur für admin + superuser.
- **Buchhaltungs-Postfach** — sichtbar für admin + buchhaltung.

Was du machst:

1. **Klick auf Datei** → Vorschau im Panel.
2. Falls die Datei zu einem MA gehört: **„Zu MA zuordnen"** → MA suchen → Kategorie+Typ wählen → speichern. Datei wandert physisch in den MA-Ordner und verschwindet aus dem Posteingang.
3. Falls die Datei generell wichtig ist (z.B. neue Filial-Vorlage): Posteingang behalten oder löschen.

## Postfach-Nachricht an einen MA senden

Administrative Text-Mitteilungen (z.B. „Dein Lohn steigt per …") schickst du aus dem **Posteingang-Bereich** als **Postfach-Nachricht**: MA wählen, Titel + Mitteilung schreiben, senden. Die Nachricht landet im persönlichen **MA-Postfach** (Login nötig — dadurch geschützt), und der MA bekommt automatisch eine **SMS mit dem Hinweis** und dem Link zum Postfach.

💡 Für persönliche Grüsse (Geburtstag, Jubiläum) nutze stattdessen [Moments](#moments) — sensible Themen gehören hierhin, ins geschützte Postfach.

## Office-Dateien anschauen

Word, Excel und PowerPoint kann der Browser nicht direkt zeigen. Das System macht das so:

1. Du klickst auf die Datei.
2. Im Hintergrund konvertiert der Server (LibreOffice) die Datei in ein PDF — dauert 1–3 Sekunden.
3. Du siehst das PDF im Panel.
4. Beim Download kriegst du das **Original** (Word/Excel) zurück, nicht das PDF.

💡 **Wichtig:** Das funktioniert nur, wenn LibreOffice auf dem Server installiert ist. Bei uns ist's installiert — falls's mal nicht klappt, gib uns Bescheid.

## Dokument-Metadaten — wer hat wann was

Pro Datei führt das System diese Stempel:

- **Erstellt am** — wann hochgeladen.
- **Geändert am** — letzte Änderung an Metadaten (Kategorie, Bemerkung etc.).
- **Datei geändert am** — letzte Änderung der Datei selbst (z.B. nach Drehen).
- **Zugriff am** + **von** — wer hat die Datei zuletzt angeschaut.

Unten im Vorschau-Panel siehst du diese Stempel kompakt nebeneinander.

## Audit-Modus

Wenn du die Belegschaft mit einem fixen Filter durchscrollen willst (z.B. „alle Arbeitsverträge der Filiale Oftringen"):

1. Im Doku-Tab auf eine Kategorie klicken (z.B. „Vertragsunterlagen").
2. **Wechsle zu einem anderen MA** in der Liste links.
3. **Der Filter bleibt** — du siehst beim neuen MA direkt nur seine Vertragsunterlagen.

So gehst du die Liste durch ohne ständig neu klicken zu müssen.

## Häufige Fragen

**Was ist die maximale Dateigrösse?**
50 MB. Bei grösseren Dateien (lange Scans) → kleinere PDFs (z.B. via Acrobat „Reduzierte Dateigrösse"), bevor du hochlädst.

**Wer darf was sehen?**
- **Admin / Superuser / Buchhaltung** sehen alles in ihrer Filiale.
- **GF** sieht alles seiner Filialen.
- **MA selbst** (Postfach-Login) sieht nur sein persönliches Postfach, nicht die ganzen Personalakte.

**Kann ich ein Dokument löschen?**
Ja — nur Admin/Superuser, über das **⋮**-Menü in der Doku-Zeile. Beachte: gelöschte Dokumente sind weg, **kein Papierkorb**. Wenn du unsicher bist, lieber den MA wechseln und in einen „Archiv"-Bereich verschieben.

**Wie sortiere ich ein Dokument zu mehreren MA?**
Geht nicht direkt — ein Dokument gehört genau einem MA. Workaround: lade es mehrfach hoch ODER lass es im Posteingang als „allgemein" stehen.

**Wo sehe ich alle Dokumente einer Filiale?**
Globale Suche **⌘K** mit Dateiname oder Bemerkung. Oder einzeln durch die MA scrollen — der Audit-Modus hält den Filter.

## Häufige Stolpersteine

- **Vorschau zeigt „HTTP 404"** → die Datei existiert in der DB aber nicht physisch auf dem Server. Wende dich an Admin — meistens nach einem Backup-Restore-Problem.
- **Office-Datei zeigt „Datei wird umgewandelt..."** dauert ewig → LibreOffice-Service hat sich aufgehängt. Server-Neustart hilft.
- **Datei beim Hochladen rejected** → meist Pfad-Problem mit Sonderzeichen oder zu lange Dateinamen. Datei umbenennen (ohne `/`, `\`, `:`, `?`, `*`, `"` , `<`, `>`, `|`).
