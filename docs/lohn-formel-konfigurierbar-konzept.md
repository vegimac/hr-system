# Konzept: Konfigurierbare Lohnberechnung (Zielschema)

**Walter 19.10.2026** · Diskussion, noch nicht gebaut.

**Leitlinie:** Lohnposition = Swissdec-Stamm + SV-Häkchen + (optional) Berechnungsdefinition.  
**Nicht:** Freitext-Programmiersprache in der DB.  
**Kern-Engine bleibt** für Modelle, Saldi-Ketten, QST, BVG-Pflicht, Karenz-Orchestrierung.

Anschluss an Ist: `lohnposition.formel_id` + `Services/LohnFormelwerk` (Registry, kein Interpreter) + `vertragsmodell_lohnschema` (wann).

---

## 1. Drei Schichten

| Schicht | Verantwortung | Wo |
|---|---|---|
| **A · Stamm** | Swissdec-Nr., Typ, SV/QST-Flags, Basis-Flags, Mirus-Alias | `lohnposition` (schon da) |
| **B · Zeilenformel** | Woher Betrag/Anzahl/Satz, Formel, Rundung, Bedingung, Version | neu: `lohnposition_formel` (+ Parameter) |
| **C · Kern** | FLEX/MTP/FIX-Zweige, Saldo führen, QST/BVG/Karenz, Dezember | `PayrollCalculationEngine` |

Die Engine lädt für die Periode die **gültige Formel-Version** der Position und wertet nur erlaubte Bausteine aus. Fehlt eine Formel → manuell / Schema «ereignis» wie heute.

---

## 2. Zielschema (DB)

### 2.1 `lohnposition_formel` (versioniert)

| Spalte | Typ | Bedeutung |
|---|---|---|
| `id` | serial | PK |
| `lohnposition_id` | int FK | welche Position |
| `gueltig_ab` | date | Versionierung (wie SV-Sätze) |
| `gueltig_bis` | date null | |
| `berechnungsart` | text | `formel` \| `fest` \| `manuell` \| `kern` \| `sv_satz` |
| `formel_id` | text null | Baustein-Id aus Registry (`StundenlohnBetrag`, …) **oder** später sichere Ausdrucks-Id |
| `ausdruck` | text null | nur erlaubte Tokens, z.B. `{satz} * {stunden}` — **kein** freies C# |
| `rundung` | text | `rappen` \| `0.05` \| `keine` (Default `0.05`) |
| `bedingung` | text null | z.B. `modell in (FLEX,MTP)` — Whitelist-Parser |
| `schema_art` | text null | `automatisch` \| `ereignis` \| `saldo` \| `austritt` (Abgleich Lohnschema) |
| `reihenfolge` | int | Abhängigkeit: kleinere zuerst |
| `ist_aktiv` | bool | |

**Regel Walter (versionierte Stammdaten):** nur Zukunft frei änderbar; aktuelle Version nur wenn noch kein Lohnlauf sie genutzt hat.

### 2.2 `lohnposition_formel_param` (Datenquellen)

| Spalte | Typ | Bedeutung |
|---|---|---|
| `id` | serial | |
| `formel_id` | FK → formel | |
| `name` | text | Platzhalter im Ausdruck, z.B. `satz`, `stunden` |
| `quelle` | text | Whitelist, siehe §3 |
| `feld` | text | Whitelist-Feld innerhalb der Quelle |
| `aggregation` | text null | `sum` \| `first` \| … für Stempel/Absenz |
| `filter` | jsonb null | z.B. `{ "bezahlt": true }` |

### 2.3 Bewusst **kein** neues Feld für SV

AHV/ALV/… bleiben die Häkchen an `lohnposition`. Die Formel liefert nur den **Brutto-/Abzugsbetrag der Zeile**; die Engine wendet die Flags an (wie heute).

---

## 3. Quellen-Whitelist (Formeleditor)

Nur diese `quelle`-Werte — Erweiterung nur per Release, nie freier String:

| Quelle | Beispiele Felder | Aggregation |
|---|---|---|
| `vertrag` | `hourly_wage`, `monthly_wage`, `guaranteed_hours_week`, `pensum` | — |
| `mitarbeiter` | `birth_date` (Alter), … sparsam | — |
| `filiale` | `ferien_percent`, `feiertag_percent`, `thirteenth_percent` | — |
| `stempel` | `bezahlt_stunden`, `nacht_stunden` | Summe Periode |
| `absenz` | `stunden_krank`, `tage_ferien`, … | Summe Periode |
| `saldo_vormonat` | `ferien_geld`, `ferien_tage`, `hour`, `thirteen` | — |
| `slip_basis` | `basis_ferien`, `basis_13ml` (aus bereits gebuchten Zeilen + Flags) | — |
| `konstante` | Wert in Param oder Ausdruck | — |
| `eingabe` | manueller Betrag der Zulage | — |
| `sv_regel` | Satz/Basis aus `DeductionRule` (nur für SV-Zeilen) | — |

Alles andere = Kern (nicht im Editor).

---

## 4. Drei Beispiele

### 4.1 Swissdec **1005** Stundenlohn — **konfigurierbar**

```
berechnungsart = formel
bedingung      = modell in (FLEX, MTP)   // FIX nutzt 1000
ausdruck       = {satz} * {stunden}
rundung        = 0.05

param satz:     quelle=vertrag,  feld=hourly_wage
param stunden:  quelle=stempel,  feld=bezahlt_stunden, aggregation=sum
```

**Kern bleibt:** Was «bezahlt» ist (Absenz-Gutschrift, Zeitgutschrift Krank, MTP Soll vs Ist) — die Quelle `stempel.bezahlt_stunden` liefert einen **von der Engine vorbereiteten** Wert, nicht Roh-Stempel blind.

**Ergebnis:** Zeile 1005 auf dem Beleg; SV über Häkchen der Position.

---

### 4.2 Swissdec **1160** Ferienvergütung (Topf) — **teils konfigurierbar**

```
berechnungsart = formel
bedingung      = modell in (FLEX, MTP)
ausdruck       = {basis} * {pct}
rundung        = 0.05

param basis: quelle=slip_basis, feld=basis_ferien
param pct:   quelle=filiale,    feld=ferien_percent
```

**Kern bleibt (nicht im Ausdruck):**

- Pott-Cap (kein Ferien-Vorbezug)
- Vormonat-Saldo + Zuwachs − Auszahlung → `ferienGeldSaldoNeu`
- FIX: anderer Pfad (1000-Split / 1166), nicht diese Formel
- Probezeit / Austritt / Auszahlung aus Topf (eigene Schema-Art `saldo` / `austritt`)

Die Formel erklärt nur den **Monats-Zuwachs**. Die Saldo-Tabelle auf dem Beleg bleibt Engine.

---

### 4.3 Swissdec **5010** AHV-Beitrag — **Kern + Katalog, keine Zeilenformel**

```
berechnungsart = sv_satz   // oder kern
formel_id       = null
```

**Warum nicht «AHV = Brutto × 5.3 %» im Stamm:**

- Basis = Summe aller Zeilen mit `ahv_alv_pflichtig` (Flags), nicht eine Formel pro Position
- Freibetrag 65+, kumuliert, Verzicht MA
- ALV-Höchstlohn / Dezember-Jahresausgleich (andere Codes, gleiche Maschinerie)
- Sätze leben in `social_insurance_rate` (versioniert)

**Konfigurierbar am Stamm:** Häkchen der *Lohn*-Positionen (was in die Basis fliesst).  
**Konfigurierbar an SV-Sätzen:** Prozent, Caps, Freibetrag.  
**Nicht** als Ausdruck an 5010: sonst doppelte Wahrheit und Swissdec-Risiko.

Auf dem Beleg: Zeile mit `code=5010`, Betrag aus `BuildResult`.

---

## 5. Was die UI zeigt

**Lohnposition bearbeiten**

1. Stamm (Nr., Bezeichnung, Flags) — wie heute  
2. Reiter **Berechnung** (neu):
   - Art: manuell / Formel-Baustein / Kern / SV-Satz  
   - Wenn Formel: Ausdruck + Parameter-Tabelle (Quelle → Feld)  
   - Bedingung, Rundung, gültig ab  
   - Vorschau mit Testwerten (ohne DB-Schreiben)

**Entwicklung → Lohn-Formeln:** Liste der Registry-Bausteine (Ist) bleibt; später = Katalog der erlaubten `formel_id` / Funktionen.

---

## 6. Migrationspfad (ohne Big-Bang)

| Etappe | Inhalt |
|---|---|
| **0** (Ist) | Registry + `formel_id` an Position; Engine hardcodiert Aufrufe |
| **1** | Tabelle `lohnposition_formel` + Params; **1005** und **1000** als erste datengetriebene Zeilen |
| **2** | %-Zuwachs **1160 / 1161 / 1200** (nur Zuwachs-Formel; Saldo weiter Kern) |
| **3** | Weitere «Zeile = f(Felder)» (1061, 1210, Spesen 6070, …) |
| **nie als Ausdruck** | QST, BVG-Pflicht, Karenz-Ketten, Dezember-Ausgleich, Zeitgutschrift-Regeln |

Jede Etappe: Unit-Tests + ein Prod-Beleg neu rechnen (Mirus-Parallel).

---

## 7. Entscheidfragen an dich

1. **Ausdruck:** nur `{a} * {b} + {c}` mit Whitelist-Ops — oder vorerst nur `formel_id` aus Registry (kein Parser)?  
2. **1005 «bezahlt_stunden»:** Engine-vorbereitet (empfohlen) oder Editor darf Roh-Stempel + Absenz-Filter bauen?  
3. **5010:** einverstanden, dass SV **nicht** in die Zeilenformel wandert?

Empfehlung Agent: Etappe 1 mit **nur Registry-`formel_id` + Parameter-Bindung** (kein Parser) — gleiches Zielbild, weniger Risiko für Zertifizierung.
