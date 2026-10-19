# Swissdec-Lohnstamm — Ziel (Walter 19.10.2026)

**Nur noch Swissdec-Lohnarten.** Keine OneCrew-Erfindungen, keine Mirus-Nr. als `code`.
Mirus nur in `mirus_code` (versteckt, Parallelvergleich). Quelle Stamm: `Assets/Swissdec/showcase_wagetypes_2.0.9.xlsx` (~597).

**Nicht wegwerfen:** Rechenlogik (FLEX/MTP/FIX), Fibu-Konten, **Lohnbeleg mit allen Saldi**.
**Wegwerfen:** Hybrid-Stamm (`110.1`, `180.3`, `600.x`, `901`… als «Swissdec-Nr.»).

---

## 1. Was Prod wirklich braucht

Aus **Mirus-Lohnkonten** (alle Filialen 2025/26) + **OneCrew-Snapshots Prod** (Stand 19.10.2026, ~38 Belege).

### A) Lohnzeilen → echte Swissdec-Nr.

| Bedarf (Schaub) | Swissdec | Bezeichnung (Stamm) | Mirus früher (nur Alias) |
|---|---|---|---|
| Festlohn / Monatslohn / Ferien-Anteil FIX / Feiertag-Anteil / Zusatzstd. | **1000** | Monatslohn | 10.1–10.4 |
| Stundenlohn / Umsatz / MTP-Mehrstunden | **1005** | Stundenlohn | 20, 20.x, 22, 55.3 |
| Überstunden 25 % | **1061** | Ueberstunden 125% | 55.1 |
| Überstunden ohne Zuschlag | **1065** | Ueberzeit | 55.2 |
| Nacht-Kompensation | **1076** | Nachtzulage | 55.10 |
| Ferien-Topf FLEX/MTP (Zuwachs/Auszahlung) | **1160** | Ferienvergütung Stundenlohn | 40.1, 195.1/3/5/6 |
| Feiertag-% | **1161** | Feiertagsentschädigung Stundenlohn | 195.2, 195.4, 50.1 |
| Ferien FIX Split (Geld) | **1166** | Ferienauszahlung | 40.1 |
| Ferien Austritt | **1168** | Ferienauszahlung nach Austritt | 40.1 |
| 13. ML (Zuwachs + Auszahlung) | **1200** | 13. Monatslohn | 180.1, 180.2 |
| 13. ML auszahlen (Auslöser, intern) | **180.3** (UI: Betrieb) → ELM **1200** | Auslöser, kein Fake-Swissdec in der Stamm-Liste | 180.3 |
| McBonus | **1210** | Bonuszahlung | 200.5, 205 |
| Unfall Karenz | **1300** | Lohn bei Unfall Stundenlohn | 60.2 |
| Krankheit Karenz | **1301** | Lohn bei Krankheit Stundenlohn | 70.1 |
| UVG-Taggeld 80 % | **2030** | Unfall-Taggeld | 60.3 |
| KTG-Taggeld 80 % | **2035** | Kranken-Taggeld | 70.2 |
| Korrektur Taggelder (KTG/UVG/EO/…) | **2050** | Korrektur Taggelder | 65.x, 75.x, 85.x, 95.x, 125.x |
| EO Militär/Zivilschutz | **2000** | EO-Taggeld | 80.x, 90.x |
| Mutterschaft EO | **2001** | Mutterschaftsentschädigung | 120.1 |
| Vaterschaft EO | **2002** | Vaterschaftsentschädigung | 120.2 |
| Kinderzulage | **3000** | Kinderzulage | 190.1 |
| Ausbildungszulage | **3010** | Ausbildungszulage | 190.2 |
| Geburtszulage | **3034** | Geburtszulage | 190.3 |
| Diverse Spesen | **6070** | Uebrige Pauschalspesen | 200.1 |

### B) SV-/QST-Abzüge → Swissdec (Beleg + ELM)

| Bedarf | Swissdec | Mirus früher |
|---|---|---|
| AHV/IV/EO | **5010** | 103 / Kategorie AHV |
| ALV | **5012** | 104 |
| ALVZ | **5014** | — |
| NBU | **5020** | 110 |
| UVGZ | **5030** | — |
| KTG | **5042** | 107 |
| BVG (Uno + Zusatz als **eine** Nr., Text unterscheidet) | **5050** | 111, 130, 131, 550, 590 |
| QST-Betrag | **5100** | 120 / Kategorie QST / 560 |
| QST-Korrektur | **5102** | 565 |
| BVG-Korrektur Vorjahr (falls nötig) | **5212** | 600.5 |

### C) Keine Swissdec-Lohnart — **nicht** im Stamm als Nr.

| Thema | Wo es lebt | Beleg |
|---|---|---|
| LGAV-Beitrag | Engine / Filial-Einstellung (interner Key `LGAV`) | eigene Abzugszeile **ohne** Swissdec-Nr., Label «LGAV-Beitrag» |
| Uniform-Depot | Engine (intern `DEPOT`) | Abzugs-/Zulagenzeile ohne Swissdec-Nr. |
| Vorschuss | manuell / Akonto (intern `VORSCHUSS`) | Extra-Abzug ohne Swissdec-Nr. |
| Diverse Abzüge (210) | manuell, intern oder nächste passende Swissdec | nur wenn Treuhänder eine Nr. bestätigt |
| Unbezahlter Urlaub 110.1 / 115 | bis Klärung: **kein** Fake-Code; Stundeninfo ggf. 1320 nur Statistik | Beleg-Bemerkung / Absenz, nicht als Bastel-Nr. |
| Saldo-Vorträge 901–906 | **Saldo-Vortrag-UI / payroll_saldo**, nie Lohnart | erscheinen **nur** in der Saldi-Tabelle |

---

## 2. Lohnbeleg (Pflicht — nicht opfern)

Struktur bleibt / wird geschärft:

1. **Lohnzeilen** — `code` = Swissdec-Nr., Bezeichnung verständlich (Kindesname, «Probezeit», …)
2. **Abzüge** — SV/QST mit Swissdec-Nr. (5010…), Betrieb (LGAV/Depot) **ohne** Nr. oder mit klarem Label
3. **Netto / Auszahlung** inkl. Empfänger
4. **Stundenblock** (Soll/Ist/Absenz/Saldo) — modellabhängig
5. **Saldi detailliert** (Vormonat | Zuwachs | Bezug | Saldo neu):
   - Stunden-Saldo (MTP/FIX)
   - Nacht-Saldo
   - Ferien Tage (+ Wochen)
   - Ferien Geld (FLEX/MTP)
   - Feiertag Tage
   - 13. ML (MTP/FIX/FIX-M; FLEX Probezeit-Pot)
   - Darlehen Rest

Saldi sind **keine** Lohnarten. Vorträge landen in `payroll_saldo`, nicht in `lohnposition`.

---

## 3. Umsetzungsschritte

1. **Aktiven Stamm** auf Liste A+B reduzieren (alle anderen `is_active=false`).
2. `code` nur noch Werte aus Showcase; Guard beim Speichern.
3. UI: Spalte «Nr.» = Swissdec; Mirus nur Admin-Hinweis «früher».
4. Engine: Slip-`code` immer Swissdec; Alias nur Lesen alter Daten.
5. 180.3 / 130 / 131 / 120 / 600.x / 901–906 aus aktivem Stamm entfernen (Mapping oben).
6. Beleg-PDF/HTML: prüfen, dass Saldi-Block vollständig bleibt.
7. Prod-Snapshots nach Umstellung **neu rechnen** (alte Slips haben noch `20`/`10.1`/`195.x`).

---

## 4. Prod-Ist (Snapshots, noch Alt-Codes auf dem Beleg)

Häufigste Slip-Codes vor Neu-Rechnung: `180.1`, Kategorie AHV/ALV/BVG/KTG/NBU/QST, `20`, `195.1`/`195.2`, `10.1`, `40.1`, `190.1`, `70.2`, `600.24`/`600.32`, …

→ Nach Engine-Umstellung und Neu-Calculate: `1200`, `1005`, `1160`, `1161`, `1000`, `5010`… statt Mirus.
