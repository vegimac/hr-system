# Favicons fuer onecrew.ch und test.hr-srgmbh.ch

Dieses Paket enthaelt dasselbe Bogen-Favicon fuer beide Domains, ohne Text und ohne Zusatzsymbol.

## Empfehlung

- `onecrew/` fuer `onecrew.ch`
- `hr-srgmbh/` fuer `test.hr-srgmbh.ch`

## Einbau im HTML-Head

```html
<link rel="icon" href="/favicon.ico" sizes="any">
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<link rel="manifest" href="/site.webmanifest">
```

## Wichtig fuer Claude

Die Dateien muessen ins oeffentliche Root-Verzeichnis der jeweiligen Web-App, also so, dass `/favicon.ico`, `/favicon.svg`, `/apple-touch-icon.png` und `/site.webmanifest` direkt erreichbar sind. Keine kreative Anpassung, kein Text, keine weiteren Zeichen. Nur diesen Bogen als Favicon verwenden und die Dateinamen/Pfade korrekt in den bestehenden App-Head einbauen.
