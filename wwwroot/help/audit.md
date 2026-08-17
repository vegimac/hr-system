# Aktivitäts-Log

Das ist dein **Sicherheitsnetz**: jede Änderung im Programm wird automatisch festgehalten. Wer? Wann? Was geändert? Wie war's vorher?

## Wer kann das sehen?

**Nur Admins.** Andere Rollen sehen den Eintrag in den Systemeinstellungen gar nicht. Grund: Datenschutz — sonst könnten User die Aktivitäten anderer User einsehen.

## Wo finde ich es?

Sidebar **Systemeinstellungen → Aktivitäts-Log** (rote Lupe-Icon, ganz unten).

## Was wird automatisch protokolliert

**JEDE** Datenbank-Änderung über das Programm:

- 🟢 **Neuer Eintrag** (CREATE) — z.B. ein MA wird angelegt, ein Vertrag erfasst, ein Dokument hochgeladen.
- 🔵 **Änderung** (UPDATE) — z.B. der Lohn wird angepasst, eine Adresse geändert.
- 🔴 **Löschung** (DELETE) — z.B. eine Familienzulage entfernt.

**Was NICHT geloggt wird:**
- Reine Anzeigen (jemand schaut sich was an). Sonst würde das Log überlaufen.
- Das Audit-Log selbst (sonst Endlos-Schleife).
- Die Lohnperioden-eigene Audit-Spur (eigenes System).

## Bei Änderungen siehst du den genauen Diff

Wenn jemand den Lohn von 4'500 auf 4'800 geändert hat, steht da:

> `hourlyRate: 4500 → 4800`

Pro geändertes Feld eine Zeile **alter Wert → neuer Wert**. Damit weisst du nicht nur *was* geändert wurde, sondern auch *was vorher drin stand*.

## Filtern und suchen

Oben gibt's einen Filter-Block:

- **Von / Bis** — Zeitraum eingrenzen (Default: letzte 7 Tage).
- **User** — auf eine bestimmte Person einschränken.
- **Entität** — z.B. nur Änderungen an „Employee" oder „Employment".
- **Aktion** — CREATE / UPDATE / DELETE separat anzeigen.
- **Volltext** — Suche in User-Name, Route oder im JSON-Diff. Z.B. `hourly_rate` findet alle Lohn-Änderungen.
- **Max. Einträge** — 100 bis 2'000 pro Anzeige.

Klick auf **„🔄 Aktualisieren"** lädt mit den aktuellen Filtern neu.

## Detail-Ansicht

Pro Zeile rechts der **„Detail"-Button** → öffnet ein Modal mit dem vollständigen JSON-Diff. Da siehst du wirklich jedes geänderte Feld, plus Route (welcher HTTP-Endpoint) und IP-Adresse.

## CSV-Export

Knopf **„⬇ CSV-Export"** lädt die aktuelle Filter-Auswahl als CSV herunter (bis zu 50'000 Zeilen). Excel-kompatibel mit BOM. Praktisch wenn du eine Audit-Auswertung für den Wirtschaftsprüfer brauchst.

## Aufbewahrung

Einträge älter als **6 Monate** werden automatisch gelöscht. Das Programm macht das jede Nacht selbst. Konfigurierbar in `appsettings.json` falls du länger aufbewahren willst.

## Zusammenhang mit der Mirus-Änderungsmail

Die morgendliche **[Mirus-Änderungsmail](#mirus-digest)** an die Sachbearbeiter liest genau dieses Aktivitäts-Log — gefiltert auf lohnkritische Entitäten (Stammdaten, Adresse, Vertrag, Bank, QST, Bewilligung, Familie …). Wenn etwas in der Mail fehlt, siehst du hier, ob die Änderung überhaupt protokolliert wurde.

## Wofür brauche ich das im Alltag?

**Szenario 1: „Jemand hat einen Lohn geändert, aber wer?"**

→ Filter: Datum (letzte Woche), Entität „Employment", Aktion „UPDATE", Volltext „hourly_rate"
→ Du siehst alle Lohn-Änderungen, mit User-Name und altem+neuem Wert.

**Szenario 2: „Wo ist Mitarbeiter X plötzlich weg?"**

→ Filter: Entität „Employee", Aktion „DELETE", Volltext „Schmid"
→ Falls jemand wirklich gelöscht hat (sollte nicht passieren, das System verhindert das normalerweise), steht hier wer.

**Szenario 3: „Tägliche Kontrolle aller Löschungen"**

→ Filter: Aktion „DELETE", Datum „heute".
→ Schneller Überblick über alle Löschungen in der Filiale.

**Szenario 4: „Tageszusammenfassung für Buchprüfung"**

→ Filter: Datum vom 1.1. bis 31.12., CSV-Export → in Excel öffnen → nach User gruppieren.

## Was kann ich NICHT?

- **Eine Änderung rückgängig machen** — das Log zeigt nur was passiert ist, kann's aber nicht zurückspulen. Du kannst aber den alten Wert ablesen und manuell wieder eintragen.
- **Reine Anzeigen tracken** (wer hat MA X angeschaut). Aus Performance-Gründen werden nur Schreib-Operationen geloggt.

## Häufige Fragen

**Warum sehe ich „System" als User?**
Das passiert bei automatischen Hintergrund-Operationen (z.B. Auto-Cleanup oder Migrations-Skripte) — da gibt's keinen menschlichen User.

**Warum sind manche User-Namen leer?**
Beim Bulk-Import oder beim Datenbank-Restore wird kein User-Kontext mitgeloggt. Sollte selten passieren.

**Wie lange dauert die Anzeige?**
Default-Filter (letzte 7 Tage) lädt in <1 Sekunde. Wenn du auf 2'000 Zeilen gehst und Volltext-Suche dazu nimmst, kann es 2–3 Sekunden dauern.

## Häufige Stolpersteine

- **Keine Einträge angezeigt obwohl du was geändert hast** → Filter „Von/Bis" prüfen. Default ist letzte 7 Tage; wenn du eine Änderung von vor 2 Wochen suchst, Datum anpassen.
- **CSV-Export hat leere Zellen** → manche Felder sind in den Audit-Einträgen nicht gesetzt (z.B. IP bei System-Operationen). Nicht ungewöhnlich.
- **Diff zeigt JSON statt Klartext** → die Detail-Ansicht zeigt den rohen JSON-Diff. Im Detail-Modal ist's hübsch formatiert.


## Lesbare Anzeige (neu)

Die Liste zeigt Klartext statt Technik:

- «📄 **Dokument angesehen**» statt roher Zugriffs-Zeitstempel — mit
  **Dateiname** und dem **Mitarbeiter des Dokuments**.
- «🔐 **Anmeldung**» statt Login-Feldern.
- Deutsche Namen für Bereiche (Dokument, Vertrag, Ferienplanung …) und Felder
  (Von/Bis, Strasse, PLZ …); Datumswerte im Format TT.MM.JJJJ.
- Bei Absenzen, Ferienplanung und Dienstplan steht der **MA mit Name und
  Nummer** direkt in der Zeile.

Der vollständige technische Diff bleibt hinter **«Detail»** verfügbar.
