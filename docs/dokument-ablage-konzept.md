# Ablage nach Angabe statt nach Ordner

Stand 25.09.2026 · Walter-Vorgabe · Weg weg von der Dokumentstruktur (Kategorie/Typ)

## Idee

In der **Dokumentverwaltung** (MA → Dokumente → «+ Dokument hochladen») wählt man
zuerst, **wofür** das Dokument ist: Ausweis, AHV-Karte, Vertrag X, Absenz Y,
Bewilligung … Die **Kategorie ergibt sich daraus** und steht erst am Schluss
(«automatisch», bei Bedarf änderbar).

Uploads direkt in der MA-Maske (Ausweis-, Bewilligungs-, Vertrags-Knöpfe usw.)
bleiben **unverändert** — sie laufen weiter über `openDokUploadModal`.
Der Posteingang nutzt weiter den nachträglichen Dialog `dokVerknuepfenFragen`.

## Regeln

| Angabe | Historie | Verhalten |
|---|---|---|
| Ausweis, AHV-Karte, Geburtsurkunde, Zivilstand, Nachtarbeit, Ausweis Partner/Kind, Geburtsurkunde Kind | nein | neues Dokument **ersetzt still**; das alte bleibt in den Dokumenten, nur unverknüpft |
| Bewilligung | ja | «Neue Bewilligung» (Formular + Einlesen) oder «Bestehende Bewilligung» (Dokument dieses Eintrags austauschen, mit Rückfrage) |
| Vertrag | ja | Dokument hängt am gewählten Vertragsabschnitt; das alte Dokument bleibt beim alten Vertrag |
| Bank, Absenz | ja | bestehendes Konto/Absenz wählen (Rückfrage, wenn schon ein Beleg da ist) oder neu erfassen |

- **Mehrfachauswahl:** ein Dokument kann an mehreren Angaben hängen (z.B. ein
  Scan mit Ausweis und AHV-Karte). Formulare (neue Bewilligung/Bank/Absenz),
  das Mitarbeiterfoto und «Anderes» stehen allein.
- **Kategorie** = die des **ersten** gewählten Ziels.
- **«Anderes»** = kurze Liste statt der ganzen Struktur: Korrespondenz,
  Arztzeugnis (ohne Absenz), Lohn, Weiterbildung, Sonstiges.
- **Löschen:** ein verknüpftes Dokument lässt sich wie bisher nicht löschen
  (`DocumentsController.Delete` prüft alle Verknüpfungen) — gilt auch bei
  Mehrfach-Verknüpfung.

## Kategorie über den Feld-Code

Jedes Ziel kennt einen Feld-Code; die Kategorie ist der erste aktive
Dokument-Typ mit diesem `dokument_typ.linked_field_code` (Reihenfolge
Kategorie → Typ). **Nie über den Namen.**

| Ziel | Code |
|---|---|
| Ausweis | `passport`, sonst `id_card` |
| AHV-Karte | `ahv_card` |
| Geburtsurkunde (MA und Kind) | `birth_cert` |
| Zivilstand | `marriage_cert` |
| Mitarbeiterfoto | `employee_photo` |
| Nachtarbeit Arztzeugnis / Ausnahme | `night_work_exam` / `night_work_ausnahme` (neu) |
| Bank | `bank_card` |
| Vertrag | `contract` |
| Absenz | `absence` (neu) |
| Bewilligung | `permit` |
| Ausweis Partner/in | `spouse` |
| Ausweis Kind | `child_id` (neu) |
| Anderes | `andere_korrespondenz`, `andere_arztzeugnis`, `andere_lohn`, `andere_weiterbildung`, `andere_sonstiges` (neu) |

Hat ein Code noch **keinen** Typ, wählt man die Kategorie am Schluss selbst.
Ein **admin** kann sie sich merken lassen («Für … künftig immer diese Kategorie»
→ `POST /api/documents/ablage-ziele/typ-merken`, setzt den Code am Typ — nur
wenn der Typ noch keinen Code hat und kein anderer Typ den Code trägt).
Die neuen Codes sind in der Dokumentstruktur (Systemeinstellungen) wählbar.

## Technik

| Datei | Aufgabe |
|---|---|
| `Services/DokumentAblage/DokumentAblageService.cs` | **die** Zielliste (`Arten`), Optionen pro MA, Code → Typ, Verknüpfen |
| `Controllers/DocumentsController.cs` | `GET ablage-ziele/{empId}`, `POST ablage-ziele/typ-merken`, `upload` mit `ablageZiele` |
| `wwwroot/js/documents.js` | `openDokAblageModal` / `dabToggle` / `dabHochladen` (Präfix `dab`) |
| `Tests/DokumentAblageTests.cs` | Optionen, Verknüpfen, fremde Einträge, Code-Rückfall |

- `upload` mit `ablageZiele=ausweis;ahv_karte;vertrag:12`: Dokument speichern und
  verknüpfen in **einer Transaktion**; scheitert das Verknüpfen, wird alles
  zurückgerollt und die Datei gelöscht.
- Formular-Ziele, Foto und «Anderes» setzen kein Feld — das UI öffnet danach das
  passende Formular (`dokNeueBewilligungMitDok`, `dokNeueBankMitDok`,
  `dokNeueAbsenzMitDok`, `dokFotoAusschnitt`).
- Kein neues Schema — alle Verknüpfungsfelder existieren seit Schema-Stand 21–23.

## Später

- Posteingang und MA-Masken-Uploads auf dieselbe Zielliste umstellen.
- Dokumentenliste nach Angabe statt nach Ordner gruppieren; dann braucht es die
  Kategorie nur noch für Altbestand, d.velop-Import und Aufbewahrung.
