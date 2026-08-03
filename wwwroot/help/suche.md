# Globale Suche

Dein bester Freund im Programm. Mit **einem Tastendruck** findest du jeden Mitarbeiter, jedes Dokument und jeden Menüpunkt — von wo aus du auch gerade bist.

## So öffnest du sie

- **Mac:** `⌘ + K`
- **Windows / Linux:** `Ctrl + K`
- Oder den **🔍 Lupen-Knopf** oben rechts neben Sprache.

## Was kann sie?

Sie sucht in fünf Bereichen gleichzeitig:

- **Mitarbeiter** — Vor-/Nachname, Personal-Nr, AHV-Nr.
- **Verträge** — Stellenbezeichnung, Modell, MA-Name.
- **Dokumente** (MA-Personalakte) — Dateiname, Bemerkung.
- **Posteingang** — Dateiname, Beschreibung.
- **Menüpunkte / Seiten** — z.B. „Lohnlauf", „Mindestlöhne", „Fibu" → springt direkt hin.

Pro Bereich max. 10 Treffer. Die Suche startet ab **2 Zeichen** mit 180 ms Verzögerung (damit nicht bei jedem Tastendruck eine neue Anfrage rausgeht).

## Bedienung

- **↑ / ↓** — durch die Resultate navigieren.
- **↵ Enter** — gewähltes Resultat öffnen.
- **Esc** — schliessen.
- **Klick** auf einen Treffer — öffnet ihn.

## Was passiert beim Klick?

| Du klickst auf … | Was passiert |
|---|---|
| Einen **Mitarbeiter** | Springt zum MA-Detail, der gewählte MA ist aktiv |
| Einen **Vertrag** | Öffnet das Vertragsmodul mit dem MA aktiv |
| Ein **Dokument** | Vorschau-Panel schiebt sofort von rechts rein — ohne Umweg über die MA-Seite |
| Einen **Posteingang-Eintrag** | Öffnet Posteingang + Vorschau direkt |
| Einen **Menüpunkt** | Springt zur Seite (z.B. „Lohnlauf" öffnet das Lohn-Modul) |

💡 **Wichtig beim Doku-Klick:** Das Vorschau-Panel öffnet sich überall — auch wenn du gerade auf der Lohnlauf-Seite bist. Das spart dir den Umweg über MA-Detail → Dokumente-Tab.

## Mehrere Suchbegriffe — „Senada & Ausweis"

Du kannst **mehrere Wörter** eingeben, getrennt durch Leerzeichen, `&`, `+` oder Komma. Das System sucht dann nach **allen Begriffen** — aber jeder darf in einem **anderen Feld** treffen.

Beispiele:

- **`senada ausweis`** → findet Senada mit B-Ausweis, ODER ein Dokument „Ausweis" das bei Senada liegt.
- **`tomova c`** → findet Aleksandra Tomova mit C-Bewilligung.
- **`580003 B`** → findet MA-Nr 580003 mit B-Ausweis.
- **`lohnpfändung scheibler`** → findet alle Lohnpfändungs-Dokumente bei Frau Scheibler-Fockova.
- **`shift manager oftringen`** → findet alle Shift Manager in der Filiale Oftringen.

Bis zu 5 Begriffe gleichzeitig — danach wird die Anfrage künstlich abgeschnitten.

## Spezielle Tricks

**AHV-Nummer ohne Punkte:**
Tippe `7561234` → findet auch `756.1234.5678.90`. Das System ignoriert die Punkte im Match.

**Permit-Code:**
Tippe `B` oder `C` → findet alle MA mit dieser Bewilligung. Bei nur einem Buchstaben gibt's viele Treffer — kombiniere mit Namen für gezielter.

**Leer beim Öffnen:**
Wenn du das Suchfeld noch nicht ausgefüllt hast, siehst du **Schnellzugriffe** zu den häufigsten Seiten (Dashboard, Mitarbeiter, Lohnlauf, Posteingang, Auswertungen, HR … — je nach Rolle).

## Was die Suche NICHT findet

- **Volltext in PDFs** — Inhalte von Dokumenten werden nicht gescannt. Nur Dateiname + Bemerkung.
- **Lohnzettel-Inhalte** — die Suche schaut nicht in einzelne Lohnzahlen.
- **Audit-Log-Einträge** — die haben ein eigenes Filter-System (Aktivitäts-Log-Seite).

Falls du Volltext-Suche in PDFs brauchst → ein anderes Thema, das könnten wir später bauen (mit Indexer).

## Häufige Fragen

**Funktioniert sie auch in der Lohnlauf-Seite?**
Ja, von überall. Der Lohnlauf wird nicht unterbrochen — die Suche legt sich als Modal drüber, und beim Klick auf einen Treffer wirst du zur Ziel-Seite gebracht.

**Was wenn ich nichts finde, was definitiv da ist?**
- Schreibweise: AHV `756.1234.…` oder `7561234`? Beides geht.
- Filiale: die Suche zeigt MA aller Filialen. Wenn nichts kommt: vielleicht inaktiver MA mit `+alt`-Suffix (Archiv-Import).
- Vorname/Nachname: probier mal beides, manchmal sind sie vertauscht erfasst.

**Wie schnell ist sie?**
Bei normaler Datenmenge (paar Hundert MA, paar Tausend Dokumente) unter 200 ms. Selbst bei mehreren Filialen mit Tausenden Einträgen praktisch immer unter 500 ms.

## Häufige Stolpersteine

- **Nichts gefunden trotz richtiger Schreibweise** → Cache-Problem im Browser? Hard-Refresh (Cmd-Shift-R / Ctrl-Shift-R).
- **Cmd-K macht nichts** → eventuell hat der Browser ⌘K als eigenen Shortcut (z.B. Tab-Suche in manchen Browsern). Dann klick einfach auf die 🔍-Lupe oben rechts.
- **Modal lässt sich nicht schliessen** → Esc oder ausserhalb klicken.
