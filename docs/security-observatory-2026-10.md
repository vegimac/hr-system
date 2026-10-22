# Mozilla Observatory — Analyse OneCrew (22.10.2026)

**Status:** Analyse + **sichere Sofortmassnahmen umgesetzt 22.10.2026** (CDN/Fonts lokal, CSP ohne Fremdhosts, Report-Only parallel, COOP). Inline-Handler-Refactor und COEP weiterhin zurückgestellt.

Bezug: Scan von `www.onecrew.ch` (CSP −20, SRI −5, COEP/COOP fehlend, keine Cookies).

---

## 1. Ist-Architektur (kurz)

| Schicht | Ist |
|---|---|
| Frontend | SPA: `wwwroot/index.html` + viele `js/*.js`, plus `import.html`, `postfach.html`, `mobil.html`, Share-Seiten |
| Auth | JWT Bearer (`Authorization: Bearer …`), Token in **`localStorage.hrToken`** — **keine Session-Cookies** |
| API | ASP.NET Core 8, `DefaultPolicy` + `FallbackPolicy` = eingeloggt **und** Rolle `admin`/`superuser`/`user`/`buchhaltung`/`lowuser` |
| Statik | `UseStaticFiles` **vor** Authorization → HTML/JS/CSS ohne Login erreichbar (bewusst, Login-Seite) |
| Header | Middleware in `Program.cs` (~Z. 7179): nosniff, XFO SAMEORIGIN, Referrer, Permissions-Policy, HSTS, **bereits CSP enforce** |

Kommentar in `Program.cs`/`CLAUDE.md` («bewusst OHNE CSP») ist **veraltet** — seit 13.06.2026 wird CSP gesetzt.

---

## 2. Observatory-Befunde → genaue Ursachen

### 2.1 CSP (−20): «unsichere Script-Regeln»

Aktuelle Policy (`Program.cs`):

```
default-src 'self';
script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval' https://cdnjs.cloudflare.com;
worker-src 'self' blob: data:;
style-src 'self' 'unsafe-inline' https://fonts.googleapis.com;
font-src 'self' https://fonts.gstatic.com;
img-src 'self' data: blob:;
media-src 'self' blob:;
frame-src 'self' blob:;
connect-src 'self'
```

**Warum Observatory abzieht**

| Direktive | Ursache im Code | Observatory |
|---|---|---|
| `script-src … 'unsafe-inline'` | Hunderte `onclick=…` in HTML/JS-generiertem Markup; Inline-`<script>` (z.B. Deploy-Sanduhr, WebAuthn-Check); `innerHTML` mit Handlern | gilt als XSS-Umgehung → starker Abzug |
| `script-src … https://cdnjs.cloudflare.com` | SheetJS: `xlsx.full.min.js` in `index.html`; pdf.js dynamisch in `mobil.html` | Drittanbieter-Script-Host |
| `'wasm-unsafe-eval'` | historisch für Scanbot/WASM kommentiert; aktueller Scanbot-Pfad unklar (nur `getUserMedia` in `m/index.html`) | oft unnötig, wenn kein WASM-Eval |

**Nicht** der Hauptgrund für −20: fehlende CSP — sie **ist** da, aber mit `'unsafe-inline'`.

### 2.2 SRI (−5)

Externe Skripte **ohne** `integrity=` / `crossorigin`:

1. `https://cdnjs.cloudflare.com/ajax/libs/xlsx/0.18.5/xlsx.full.min.js` (`index.html`)
2. pdf.js + Worker von cdnjs (`mobil.html`, dynamisch)

Google Fonts CSS zählt bei SRI typischerweise nicht als Script-SRI; trotzdem Drittanbieter-Abhängigkeit.

### 2.3 COEP / COOP nicht gesetzt

Beide Header fehlen bewusst/noch nicht. Observatory meldet das als «nicht konfiguriert».

- **COOP** (`Cross-Origin-Opener-Policy: same-origin`): isoliert `window.opener` — relativ unkritisch für OneCrew, wenn keine Cross-Origin-Popups erwartet werden.
- **COEP** (`require-corp` / `credentialless`): schärfer — kann **CDN-Ressourcen, Fonts, blob:-iframes (PDF-Vorschau)** und Kamerastreams brechen, wenn Cross-Origin-Ressourcen keine CORP/CORS-Header haben.

### 2.4 «Keine Cookies erkannt»

**Erwartet und korrekt** für die aktuelle Architektur:

- Login setzt **kein** `Set-Cookie`.
- Token liegt in `localStorage` → Observatory sieht keine Auth-Cookies.
- Das ist **kein** Fehlbefund von Observatory, sondern Architekturwahl.

**Nebenwirkung dieser Wahl:** XSS kann das Token lesen (Cookies mit `HttpOnly` wären dagegen immun gegen JS-Diebstahl). Deshalb ist eine harte CSP langfristig wichtiger als Cookie-Umstellung allein.

---

## 3. Session-Management (nach Anmeldung)

| Mechanismus | Verhalten |
|---|---|
| Login | `POST /api/auth/login` → JWT; optional TOTP (`needsTotp` / Setup) |
| Speicherung | `localStorage.hrToken` (+ Impersonation: `hrTokenAdmin`) |
| Idle | Claim `idle_timeout_minutes` (Default 15, 0–30), Frontend `session-lock.js` / `session-timeout.js` |
| Max-Session | Claim `max_session_minutes` (Default 480 HR / 30 employee), JWT `expires` = Session-Start + Max |
| Harte Obergrenze | `HARD_CAP_MINUTES` = 840 (14 h ab `login_at`) |
| Heartbeat | `POST /api/auth/heartbeat` → `SessionRegistry.Touch` |
| Admin-Abmelden | `session_revoked_before` → 401 + `X-Session-Revoked` |
| Logout | Token verwerfen clientseitig + optional `POST /api/auth/logout` |

**Bewertung:** Session-Steuerung ist serverseitig am JWT-Ablauf und an der Registry verankert; Frontend-Wächter ergänzt. Kein klassisches Cookie-Session-Fixation-Risiko.

**Risiken / Lücken (kein Observatory-Punkt, aber relevant):**

1. Token in `localStorage` → XSS = Session-Diebstahl.
2. Statische SPA ohne Auth — Angriffsfläche = XSS in der App nach Login.
3. `import.html?token=…` übergibt JWT in der URL (Referrer-/History-Risiko) — bestehend, bei CSP-Härte nicht lösen, aber merken.

---

## 4. API-Zugriffsschutz

**Stark**

- FallbackPolicy: ohne Token → 401; ohne HR-Rolle → 403.
- `employee` nur auf expliziten Postfach-Endpoints + Eigentümerprüfung.
- Schreibende Admin-Endpunkte oft `[Authorize(Roles="admin")]`.
- Filialfilter `MaFilialZugriff` für GF/Buchhaltung.
- JWT-Secret Pflicht (kein hartcodierter Fallback).

**Bewusste `[AllowAnonymous]`-Ausnahmen** (Auswahl):

| Bereich | Zweck | Schutz |
|---|---|---|
| `AuthController` Login/TOTP | Anmeldung | Rate/Lockouts teilweise vorhanden |
| Share-Links (Vertrag, Lohnausweis, Dokument-View-Token, Exit-Survey, Moments, Kandidaten, …) | Link ohne Bearer | kurzlebige/zufällige Tokens |
| `GET /api/users/{id}/signature` | `<img src>` ohne Header | **ID-enumerierbar** — Signatur-PNG öffentlich, wenn ID bekannt |
| WebDAV | eigene Basic-Auth | getrennt |
| WebAuthn challenge | Passkey-Flow | Challenge-gebunden |

**Empfehlung (später, eigene Freigabe):** Signatur-Endpoint an View-Token oder Auth koppeln (Blob-URL statt nackter ID) — unabhängig von CSP.

API-Calls gehen über `connect-src 'self'` → externe Browser-Fetches zur eigenen Domain nur; Swissdec/easy@work laufen **serverseitig**, nicht im Browser — passt zur aktuellen CSP.

---

## 5. Lösungsvorschlag (ohne Produktivänderung bis Freigabe)

### Prinzip

1. **Zuerst Report-Only** — nichts blockieren, Violations sammeln.
2. Externe Skripte **self-hosten** → SRI-Punkt und CDN in `script-src` entfallen.
3. Fonts **self-hosten** → Google Domains aus CSP streichen.
4. `'unsafe-inline'` **nicht** «wegzaubern» ohne Refactor — sonst bricht die SPA.
5. COOP vorsichtig; **COEP vorerst weglassen** oder nur testen.

---

### Phase A — CSP Report-Only (empfohlen als erste Freigabe)

**Änderung (nur Header, Logik unverändert):**

- Zusätzlich (oder vorübergehend statt enforce):  
  `Content-Security-Policy-Report-Only: …`  
  mit **derselben** Policy wie heute **oder** einer strengeren Test-Policy.
- Optional: `report-uri` / `report-to` auf einen eigenen Endpoint `/api/csp-report` (nur admin/loggen, Rate-Limit) — oder Browser-Console manuell prüfen.

**Strengere Report-Only-Variante (Zielbild, nur melden):**

```
default-src 'self';
script-src 'self' 'wasm-unsafe-eval';   /* bewusst OHNE unsafe-inline und OHNE cdnjs — Violations = Inline/onclick + CDN */
style-src 'self' 'unsafe-inline';      /* Inline-Styles bleiben vorerst */
font-src 'self';
img-src 'self' data: blob:;
media-src 'self' blob:;
frame-src 'self' blob:;
worker-src 'self' blob: data:;
connect-src 'self';
base-uri 'self';
object-src 'none';
frame-ancestors 'self';
form-action 'self'
```

**Nebenwirkungen Phase A:** keine Funktionsstörung (Report-Only). Console voller CSP-Meldungen. Enforce-Policy bleibt vorerst wie heute, bis Violations ausgewertet sind.

**Alternative A′:** Enforce unverändert lassen, nur Report-Only parallel mit Zielbild — doppelte Header sind üblich und erlaubt.

---

### Phase B — SRI / CDN eliminieren (schneller Observatory-Gewinn)

1. SheetJS (`xlsx.full.min.js`) nach `wwwroot/lib/xlsx/` (oder ähnlich) legen, Script-Tag auf `'self'`.
2. pdf.js analog für `mobil.html`.
3. Inter-Font (und ggf. Playfair/Source Sans auf Share-Seiten) lokal unter `wwwroot/fonts/` + `@font-face` in CSS; Google-`<link>` entfernen.
4. Danach aus CSP streichen: `cdnjs.cloudflare.com`, `fonts.googleapis.com`, `fonts.gstatic.com`.

**SRI:** bei self-host oft unnötig für Observatory (kein «external script»). Falls CDN bleibt: `integrity="sha384-…"` + `crossorigin="anonymous"`.

**Nebenwirkungen:** Deploy-Größe steigt leicht; Cache-Buster auf lib-Dateien; Font-Lizenz (Inter OFL) beachten; mobil.pdf-Vorschau testen.

---

### Phase C — CSP ohne `'unsafe-inline'` (grosser Refactor, **nicht** mit Phase A vermischen)

**Warum nötig für «sichere CSP ohne unnötige Ausnahmen»:**

- `index.html` allein: ~800 `onclick=`-Attribute.
- Zusätzlich dynamisches `innerHTML` mit Handlern in Dutzenden JS-Modulen.
- Inline-`<script>` und Inline-`<style>`.

**Wege (aufsteigender Aufwand):**

| Ansatz | Aufwand | Wirkung |
|---|---|---|
| C1: Nonces für *eigene* Inline-`<script>`/`<style>` im HTML-Response (Middleware injiziert Nonce) | mittel | deckt nur statische Inline-Blöcke, **nicht** `onclick=` |
| C2: Event-Delegation statt `onclick` (Refactor HTML+JS) | **sehr hoch** | erst dann `script-src 'self'` ohne unsafe-inline realistisch |
| C3: `'strict-dynamic'` + Nonce für Einstiegs-Scripts | mittel–hoch | moderne Browser; Inline-Handler bleiben Problem |

**Empfehlung:** Phase B zuerst; Phase C nur als eigenes Projekt nach Go-live-Stabilität. Bis dahin `'unsafe-inline'` in **enforce** belassen, Report-Only ohne inline zum Messen.

**Nebenwirkungen C2:** jedes Modul, jede dynamische Tabelle — hohes Regressionsrisiko für Lohnlauf/Import/Posteingang.

---

### Phase D — COOP / COEP

| Header | Vorschlag | Nebenwirkung |
|---|---|---|
| `Cross-Origin-Opener-Policy: same-origin` | Report/testen, dann setzen | `window.open` zu Fremdseiten: kein `opener`; selten relevant |
| `Cross-Origin-Embedder-Policy: require-corp` | **vorerst nicht** | bricht oft CDN/Fonts/PDF-iframe/`blob:` ohne CORP |
| `COEP: credentialless` | optional später testen | besser als require-corp, aber immer noch heikel mit gemischten Origins |

Optional: `Cross-Origin-Resource-Policy: same-origin` auf API-Antworten — separat prüfen (Share-Links, Signatur-`<img>`).

---

### Phase E — Session/Cookies (optional, Architekturentscheid)

Nur wenn Walters Ziel «HttpOnly-Cookie statt localStorage» ist:

- JWT (oder Session-Id) als `Secure; HttpOnly; SameSite=Strict` Cookie.
- CSRF-Schutz für Cookie-Auth nötig (heute Bearer = kein klassisches CSRF).
- SPA-Umbau aller `fetch`-Aufrufe; Impersonation/Import-URL-Token neu denken.

**Nicht** nötig, um Observatory «Cookies»-Zeile zu «füllen» — fehlende Cookies sind hier kein Mangel.

---

## 6. Empfohlene Reihenfolge (nach Freigabe)

1. **A′** Report-Only-Zielbild parallel zur bestehenden Enforce-CSP; 1–2 Wochen Console/Reports sammeln (Test + Prod).
2. **B** Self-host xlsx + pdf.js + Fonts; CSP enforce verschärfen (CDN/Fonts raus); Observatory SRI erwarten besser.
3. **D** nur COOP `same-origin` testen; COEP weglassen.
4. **C** erst nach explizitem Auftrag (Refactor Inline-Handler).
5. Signatur-`AllowAnonymous` und `import.html?token=` als eigene Security-Tickets.

---

## 7. Was bewusst **nicht** empfohlen wird

- Sofort Enforce-CSP **ohne** `'unsafe-inline'` → Login/App tot.
- COEP `require-corp` ohne wochenlange Tests.
- Cookie-Session nur wegen Observatory-Punkt «keine Cookies».
- CSP nur in nginx duplizieren (Header bereits in Kestrel — Doppelung vermeiden).

---

## 8. Freigabe-Checkliste für Walter

- [ ] Phase A/A′ (Report-Only) freigeben?
- [ ] Phase B (Self-host CDN/Fonts) freigeben?
- [ ] COOP testen freigeben?
- [ ] Phase C (Inline-Handler-Refactor) bewusst zurückstellen?
- [ ] Signatur-Endpoint / URL-Token als Folge-Tickets?

**Bis zur Freigabe: kein Deploy dieser Themen.**
