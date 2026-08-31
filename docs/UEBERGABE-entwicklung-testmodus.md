# Übergabe an Cursor — Bereich «Entwicklung» + Testmodus

Stand: 31.08.2026, Repository `~/projects/hr-system`, Branch `main`, HEAD `cfd10b0`.
Arbeitsverzeichnis sauber, alles committet und gepusht.

---

## 1. Was gewollt ist

Ein Sidebar-Bereich **«Entwicklung»** (enthält heute die Swissdec-Seite), der
**nur** sichtbar ist für

- den Super-Admin (`app_user.is_super_admin = true`), **oder**
- Benutzer, bei denen im Benutzer-Modal unter «Sichtbare Bereiche» das Häkchen
  **Entwicklung** gesetzt ist (`app_user.allowed_areas` enthält `entwicklung`).

Die Rolle `admin` allein soll **nicht** genügen.

## 2. Was NICHT funktioniert

Beim Testbenutzer **Simone Ittig** (`role = admin`, `is_super_admin = f`,
`allowed_areas` **ohne** `entwicklung`) erscheint der Menüpunkt weiterhin.

Nach Auskunft des Benutzers auch nach Deploy, hartem Neuladen, geleerten
Browserdaten und Chrome-Neustart.

## 3. Was gebaut wurde (relevante Commits)

| Commit | Inhalt |
|---|---|
| `48e0c39` | Neuer Bereich, Sidebar-Eintrag, Seite `page-entwicklung`, Checkbox `data-area="entwicklung"` im Benutzer-Modal |
| `cb7534f` | Entwicklungsseite im System-Look (`entw-hub-cat` / `entw-hub-group`, eigene Klassen, damit `adminHubShowCat()` sie nicht mit ausblendet) |
| `efd8d21` | Opt-in-Prüfung **vor** dem `return` in `applyAreaVisibility()` |
| `c783bb6` | Sektion von `admin-only-section` auf `entw-only-section` umgestellt |
| `0ba35cc` | CSS: standardmässig `display:none !important`, sichtbar nur über `body.entw-on` |
| `d573b36` | Entscheidung früh in `startApp()`, vor den `await`-Aufrufen |
| `1195a51` | zusätzlich `sec.style.display` direkt am Element |
| `64b5991` | **Testmodus**: `/api/auth/me` liefert neu `impersonating` (Claim `impersonated_by`) |
| `cfd10b0` | Testmodus-Prüfung entschärft (siehe Abschnitt 6) |

### Betroffene Dateien

- `wwwroot/js/app-core.js` — `startApp()` (frühe Entscheidung), `applyAreaVisibility()`
- `wwwroot/css/app.css` — `.entw-only-section` / `body.entw-on .entw-only-section`
- `wwwroot/index.html` — Sidebar-Sektion, Seite, Checkbox, Cache-Buster
- `Controllers/AuthController.cs` — `impersonating` in `/me`

### Aktuelle Logik (zweimal, absichtlich redundant)

```js
// in startApp(), direkt bei der Rollen-Sichtbarkeit:
const _entwErlaubt = currentUser?.isSuperAdmin === true
    || (Array.isArray(currentUser?.allowedAreas)
        && currentUser.allowedAreas.includes('entwicklung'));
document.body.classList.toggle('entw-on', _entwErlaubt);
document.querySelectorAll('.nav-item[data-page="entwicklung"]').forEach(it => {
    const sec = it.closest('.nav-section') || it;
    sec.style.display = _entwErlaubt ? '' : 'none';
});
```

```css
.entw-only-section { display: none !important; }
body.entw-on .entw-only-section { display: flex !important; }
```

`applyAreaVisibility()` überspringt `data-page="entwicklung"` in seiner Schleife
bewusst, damit die frühe Entscheidung nicht überschrieben wird.

## 4. Was nachweislich stimmt

- **Datenlage** (Prod-DB `hrsystem`):
  `Simone Ittig | admin | is_super_admin = f | allowed_areas = mitarbeiter,
  posteingang, auswertungen, roster-absence-import, lohn, hr-hub, fibu, admin-hub`
  → kein `entwicklung`.
- **Server liefert den neuen Code**: `https://onecrew.ch/js/app-core.js` enthält
  `sec.style.display = _entwErlaubt ? '' : 'none';`
- **Frisch geladene Seite, nicht angemeldet**: Sektion trägt
  `nav-section entw-only-section`, `getComputedStyle(...).display === "none"`.
- **HTML wird nicht gecacht**: `curl -sI https://onecrew.ch/index.html` →
  `cache-control: no-cache, no-store, must-revalidate`, kein CDN dazwischen.

## 5. Der entscheidende Fund — und warum alle Tests wertlos waren

Auslesen der laufenden Seite **im Testmodus** ergab:

```json
{
  "angemeldet": "Walter Schaub",
  "superAdmin": true,
  "areas": ["…", "entwicklung"],
  "testmodus": "{\"username\":\"Simone Ittig\",\"role\":\"admin\"}",
  "bodyEntwOn": true,
  "sektion": { "sichtbar": "flex" }
}
```

Der orange Testmodus-Balken kam **allein aus `localStorage.hrImpersonating`**.
Das tatsächlich verwendete Token gehörte dem Admin. `/api/auth/me` antwortete
also mit *Walter*, `isSuperAdmin = true`, `allowed_areas` **mit** `entwicklung`
→ der Menüpunkt wurde völlig korrekt eingeblendet.

**Vermuteter Ablauf:** Das Impersonation-Token läuft ab (Sitzungsdauer des
Zielbenutzers, `EffectiveIdleTimeout(target)`) → der globale 401-Interceptor in
`app-core.js` löscht `hrToken` und lädt neu → Anmeldemaske → Admin meldet sich
neu an → `hrImpersonating` bleibt liegen. Ab da: Balken zeigt Simone, App
arbeitet als Admin.

**Das ist der eigentliche Fehler.** Solange er besteht, ist jede
Rechte-Prüfung über den Testmodus unbrauchbar.

## 6. Offener Punkt beim Testmodus (Regression, dann entschärft)

`64b5991` verwarf den Balken, wenn `currentUser.impersonating !== true`.
Liefert das Backend das Feld nicht (Deploy noch nicht durch, oder die Antwort
enthält es nicht), ist der Wert `undefined` → Testmodus wurde sofort beendet.
Der Benutzer meldete: «funktioniert nicht einmal mehr der Testbenutzer».

`cfd10b0` verwirft jetzt nur noch bei Beweis:

```js
const nameGleich = (imp.username||'').trim().toLowerCase()
    === (currentUser?.username||'').trim().toLowerCase();
const serverSagtNein = currentUser?.impersonating === false;
if (!nameGleich || serverSagtNein) { /* Hinweis verwerfen */ }
```

**Ungeprüft:** ob `/api/auth/me` auf Prod tatsächlich `impersonating` liefert.
Das ist der erste Punkt, den ich prüfen würde.

## 7. Konkrete nächste Schritte

1. **Testmodus zuerst reparieren, dann erst den Menüpunkt beurteilen.**
   In den Testmodus wechseln und im Netzwerk-Tab die Antwort von
   `/api/auth/me` anschauen: Steht dort `username: "Simone Ittig"` und
   `impersonating: true`? Wenn nein — dort liegt das Problem, nicht in der
   Sichtbarkeitslogik.
2. Prüfen, ob der 401-Interceptor (`app-core.js`, «Sitzung abgelaufen») beim
   Aufräumen `hrImpersonating` und `hrTokenAdmin` mitentfernt. Aktuell nicht.
3. Verifikation **ohne** Testmodus: bei einem echten Nicht-Super-Admin ohne
   Häkchen anmelden und schauen, ob der Punkt fehlt. Alternativ beim eigenen
   Konto das Häkchen entfernen — greift beim Super-Admin allerdings nicht,
   da `isSuperAdmin` die Ausnahme bildet. Für einen sauberen Test müsste die
   Super-Admin-Ausnahme testweise entfernt werden.
4. Wenn die Sichtbarkeitslogik bestätigt ist: überlegen, ob die
   Super-Admin-Ausnahme bleiben soll oder ob allein das Häkchen entscheidet.

## 8. Stolperfallen aus dieser Sitzung

- **`.git/index.lock`** blieb mehrfach liegen (zwei Agenten parallel im
  Repository). Folge: `git add` scheitert, die `&&`-Kette bricht ab, **es wird
  nichts deployt** — und man testet stundenlang gegen einen alten Stand.
  Vor dem Deploy prüfen: die Ausgabe muss mit `prod=ok` enden.
- Die Versionsnummer `?v=` im Script-Tag entscheidet **nicht**, welche Datei
  der Server ausliefert. Eine alte gecachte `index.html` kann problemlos mit
  der neuesten `app-core.js` zusammenlaufen — mit gemischten Klassennamen.
- `wwwroot/css/app.css` ist zu gross für oberflächliche Fernprüfungen; Abrufe
  werden abgeschnitten und liefern falsch-negative Ergebnisse.
