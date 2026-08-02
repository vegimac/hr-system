# Mitarbeiter — der Personalbereich

Hier verwaltest du alle Mitarbeiter:innen: Stammdaten, Verträge, Familie, Bank, Quellensteuer, Absenzen, Dokumente. Jeder Bereich hat einen eigenen Tab.

## Wo finde ich was?

Sidebar **Mitarbeiter**:

- **Links:** Liste mit Suche und Filter *Aktive · Inaktive · Alle*. Filiale = Sidebar oben.
- **Rechts:** Detail mit Tabs.
- **Praxis-Aktionen:** Tab **Restaurant Admin** (Kacheln).
- **easy@work synchronisieren:** oben rechts beim MA.

## Neuen Mitarbeiter anlegen

**Neue MA kommen aus easy@work** — immer:

1. Zuerst in **easy@work** anlegen (dort entsteht die Personalnummer).
2. In OneCrew **„＋ Neuer MA aus easy@work"** — Vorschau zeigt **NEU** und **UPDATE**.
3. Auswählen → **„Ausgewählte importieren"**.

💡 Vertraulicher Lohn (FIX-M ohne Tarif in easy@work): MA trotzdem importieren, Lohn danach im OneCrew-Vertrag setzen — siehe [Verträge](#vertraege).

Mehr zur Schnittstelle: [easy@work](#easyatwork).

## Mitarbeiter finden

- Suchfeld in der Liste
- Filter Aktive / Inaktive / Alle
- Spezialfilter (z.B. „QST fehlt", „Keine Bewilligung", „Bank fehlt")
- Globale Suche **⌘K** — siehe [Suche](#suche)

## Die Tabs

### Übersicht
Karten auf einen Blick:

- **Personalien & Adresse** — Strasse, PLZ, Ort, Kanton, Telefon, AHV, Zivilstand, Nationalität, ZEMIS, Ledigname … (viele Felder direkt speicherbar)
- **Anstellung** — Eintritt, Austritt, L-GAV, Kündigung am/per, Probezeit bis, Probezeitgespräch-Status
- **Nachtarbeit** — Pflicht-Badge, Nächte-Zähler, Arztzeugnis / Ausnahme verknüpfen, SECO-Formulare drucken
- **Verträge** — kompakte Liste + Saldi der Periode
- **Weitere Adressen** — Korrespondenz, Sozialamt … (unten in der Personalien-Karte)

### Familie / Schwanger
Ehepartner, Kinder, **Kinderzulagen** (versioniert).  
Bei Frauen zusätzlich **Schwangerschaft / Mutterschaft**: Termin, Fahrplan, Formulare, Fristen. Aktive Schwangerschaft → Badge neben dem Namen.

💡 Ehepartner-Nationalität / C-Ausweis / Pass-Dokument beeinflussen die [Quellensteuer](#qst).

### Bewilligung QST Bank
Drei Blöcke (Bank oben, dann Bewilligung, dann QST):

1. **Bank** — versioniert, eine Hauptbank, optionale Aufteilung
2. **Bewilligungen** — Verlauf; SMS-Erinnerung möglich
3. **Quellensteuer** — Banner + Tarif / Behörden-Befreiung — Details: [QST](#qst)

### Restaurant Admin
Icon-Kacheln für den Filial-Alltag:

| Kachel | Was sie tut |
|---|---|
| **Bewerbungsbogen** | Blanko-Bewerbungsbogen der Filiale als PDF |
| **Probezeit** | Probezeit-Gespräch-PDF, Datum + Protokoll verknüpfen, Kündigung in Probezeit |
| **Arbeitsbestätigung / Arbeitszeugnis / Zwischenzeugnis** | PDF erzeugen |
| **Verwarnung** | Verwarnung erfassen + Formular |
| **Absenzkalender** | Monatsübersicht der Filiale |
| **Postfach-Passwort** | Reset auf Personalnummer + Login-Sperre weg |
| **Onboarding-QR** | QR für ersten Postfach-Login |
| **Face ID zurücksetzen** | Alle Passkeys des MA löschen |

### Stempelzeiten
Nur **lesen**. Quelle = easy@work. Wochentotale; rot wenn über Max-Stunden der Filiale.

### Absenzen / KTG/UVG
Krankheit, Unfall, Ferien … — siehe [Absenzen](#absenzen).

### Verfügbarkeit
Wann der MA einsetzbar ist. Daten aus easy@work (Einzel-Sync). Anzeige read-only; manuelle Versionen bleiben erhalten.

### Zulagen Abzüge Abtretung BVG
- BVG-Zusatz-Mitgliedschaft (pro MA, versioniert)
- Wiederkehrende Zulagen/Abzüge
- **Lohnabtretungen** (Pfändung / Sozialamt) — Behörde + Sachbearbeiter wählen, Beleg verknüpfen (ohne Dokument greift die Abtretung im Lohn **nicht**). Bemerkung beim Erfassen vorausgefüllt mit Name, Vorname, AHV. Ausführlich: [Lohnabtretungen & Behörden](#lohnabtretungen)

### Dokumente
Personalakte — siehe [Dokumente](#dokumente).

## Nachtarbeit (kurz)

In der Übersicht-Karte **Nachtarbeit**:

- Pflicht ja/nein, Anzahl Nächte
- Arztzeugnis und Ausnahme-Regelung als Dokument verknüpfen
- Formulare (Eignung / Verzicht / Ausnahme) drucken
- Fehlende Nachweise erscheinen als To-do und in der HR-Kontrolle

## Phantom-MA (Supervisor ohne Lohn)

Häkchen **„MA ohne Lohn"**: kein Lohnlauf, oft kein Postfach/Bank. Für easy@work-Zugang ohne Anstellung bei euch.

## Austritt

Kündigung am/per in der Anstellung-Karte; Formulare über Restaurant Admin oder [HR](#hr-hub). Ausführlich: [Kündigung & Austritt](#austritt).

## Häufige Stolpersteine

- **Mindestlohn / Lohnsumme / QST** → blockiert Lohnlauf — Banner lesen
- **Probezeitgespräch „offen"** → Datum *und* Protokoll verknüpfen
- **Absenz nicht speicherbar** → [Edit-Sperre](#edit-sperre)
- **AHV-Format** `756.xxxx.xxxx.xx`, Datum `TT.MM.JJJJ`
