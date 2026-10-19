# Inventar Alt → Swissdec (archiviert)

**Stand 19.10.2026 abends:** Migration erledigt. Die Entwicklung-UI zeigt nur noch die
**Formel-Registry** (`LohnFormelwerk.Alle` / `GET /api/lohn-formeln`) — ohne Mirus-Spalte,
ohne Alt→Swissdec-Tabelle.

Historische Zuordnung Mirus→Swissdec bleibt im Code als Alias (`LohnCodeAlias`, `mirus_code`
am Katalog für Parallelvergleich/Kontoplan), nicht mehr als eigene Inventar-Seite.

Lebende Referenz: Registry + `docs/lohn-formeln.md` + Engine-Debugger
(`docs/lohn-engine-karte-skizze.html`).
