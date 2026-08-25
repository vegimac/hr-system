# Konzept: Konkubinatspartner & QST (H1/A0)

**Status: FREIGEGEBEN (Walter 25.08.2026) — Umsetzung gemäss diesem Papier.**
(Ausgangsfall: Droghe Stingaciu / Fischer AG — als «Ehepartner» erfasst,
obwohl Konkubinat.)

## 1. Fachliche Regeln (KS 45, Walter-Vorgaben 25.08.2026)

- **Konkubinat ≠ Ehe.** Die QST-Befreiung über den Partner (CH-Bürger oder
  C-Ausweis) gilt NUR bei Ehe/eingetragener Partnerschaft. Ein
  Konkubinatspartner mit C oder CH befreit den MA **nicht**.
- **Das Konkubinat interessiert die QST nur über das gemeinsame Kind.**
  Entscheidtabelle (Haushalts-Kinder = QST-berechtigte Kinder im selben
  Haushalt):

  | Situation | Tarif | Einkommensfrage |
  |---|---|---|
  | K-Partner, kein Kind im Haushalt | A0 (wie ledig) | nein |
  | K-Partner + Kind im Haushalt, NICHT gemeinsam | H (MA alleinerziehend) | nein |
  | K-Partner + GEMEINSAMES Kind im Haushalt | MA verdient mehr → **H1**, sonst → **A0** | **ja, Pflicht** |
  | kein K-Partner + Kind im Haushalt | H wie bisher | nein |

- **Nie beide H1** (der besser verdienende Elternteil = Hauptunterhalt → H1,
  der andere A0).
- **Partner nicht erwerbstätig** (Walter 25.08.2026, AG/ESTV-Praxis «beide
  erwerbstätig» als Voraussetzung): hat der K-Partner kein Erwerbseinkommen,
  ist der MA zwangsläufig Hauptunterhaltsträger → **automatisch H1**, auch
  ohne beantwortete Einkommensfrage (keine W6-Warnung).
- **Gemischter Fall (gemeinsame UND nicht-gemeinsame Kinder im Haushalt):
  KEIN Automatismus** — das System macht keinen Tarifvorschlag, sondern zeigt
  die Meldung **«Mit QST-Behörde abklären»** (Walter 25.08.2026).
- **Offene Fragen = konservativ:** Solange die Gemeinsam-/Einkommensfrage
  unbeantwortet ist, wird **A0** vorgeschlagen (Walter-Praxis: lieber zu viel
  abziehen und zurückzahlen) + orange Warnung, die Frage zu beantworten.

## 2. Neuer Familientyp «Konkubinatspartner/in»

- Neuer Wert `Konkubinatspartner` im Typ-Dropdown (`member_type` ist freier
  String — kein Constraint).
- **Einkommensfrage direkt am Partner (Walter v2):** Segment-Pille «Hat der/die
  MA das höhere Bruttoeinkommen als der Partner? – / Ja / Nein» — neues Feld
  `employee_family_member.ma_hat_hoeheres_einkommen` (BOOLEAN NULL = offen).
  Nur bei Typ Konkubinatspartner sichtbar.
- **Erwerbstätigkeit-Block** sichtbar wie beim Ehepartner. Erwerbstätigkeit +
  Einkommensfrage werden erst **relevant/angemahnt** (orange Warnung, KEIN
  Lohnlauf-Block), wenn ein gemeinsames Kind (Flag «Ja», siehe 3.) existiert
  (Walter v3: «Erwerbstätigkeit des K-Partners interessiert nur mit
  gemeinsamem Kind»).
- Aufenthalt/Bewilligung/Nationalität erfassbar = reine Doku, **ohne** jede
  Pflicht-Prüfung.
- **Familie-Tab:** Badge «Konkubinat» auf der Karte.
- **Ausdrücklich KEINE Wirkung auf:** QST-Befreiung (nur Ehepartner +
  verheiratet), Partner-Pflicht `QST_PARTNER_DATEN_FEHLEN` (nur Ehepartner),
  Ehepartner-Felder der QST-Anmeldung.

## 3. Gemeinsames-Kind-Flag am KIND

- Neues Feld `employee_family_member.gemeinsames_kind_mit_partner`
  (BOOLEAN NULL = Frage offen). Segment-Pille im Kind-Modal:
  «Gemeinsames Kind mit dem Konkubinatspartner? – / Ja / Nein».
- **Sichtbar nur**, wenn beim MA ein Konkubinatspartner erfasst ist
  (bei verheirateten MA erscheint nichts). Bewusst nur Ja/Nein-Flag,
  keine Eltern-Verknüpfung (Walter 25.08.2026: «nur ja/nein Flag»).

## 4. Brücke Familie-Tab → QST (Familie = einzige Erfassungsstelle)

- Ist ein Konkubinatspartner erfasst, werden die QST-Modal-Checkboxen
  «Konkubinat» + «Höh. Einkommen als Partner» **aus dem Familie-Tab befüllt
  und gesperrt** (read-only, Hinweis «aus Familie-Tab»). Beim Speichern
  persistiert die Erfassung die abgeleiteten Werte wie bisher in
  `employee_quellensteuer` → alle nachgelagerten Konsumenten (Vorschlag,
  Warnungen, QST-Anmeldung `HoeheresBruttoEinkommenJaNein`, KonfessionSync)
  bleiben unverändert. Server-Guard im Save: bei vorhandenem K-Partner
  überschreibt der Server die zwei Flags aus dem Familie-Tab.
  Gleiches Muster wie `EpHatErwerbJaNein` ← `spouse.Erwerbstaetig`.
- OHNE K-Partner bleiben die Checkboxen editierbar (Alt-Fälle).
- Ändert die Familie-Antwort NACH einer aktiven Erfassung: Erfassung wird NICHT
  still mutiert (Versionierung/Lohn-Sperre) — `qstRecheckNachAenderung` schlägt
  an, W3 meldet die Inkonsistenz bis zur neuen Erfassung.
- **Tarifvorschlag** (`QstTarifVorschlagLogic`): setzt die Entscheidtabelle aus
  Abschnitt 1 um; liest K-Partner-/Kind-Flags live, Fallback auf die
  gespeicherten QST-Flags für Alt-Fälle ohne K-Partner-Eintrag.

## 5. Warnungen (QST-Tab + Dashboard-Sweep `qst_tarif_warnung`, alle orange, keine Blocks)

| # | Situation | Meldung |
|---|---|---|
| W1 (existiert) | Tarif H + Konkubinat + Einkommens-Flag fehlt | «Konkubinat-H nur mit ‹höheres Einkommen›» |
| W2 | Tarif A + gemeinsames Kind + Einkommensfrage = Ja | «H1 prüfen — MA hat das höhere Einkommen» |
| W3 | K-Partner erfasst, aktive Erfassung hat andere Konkubinat-/Einkommens-Flags | Inkonsistenz — neue Erfassung nötig |
| W4 | Typ «Ehepartner» erfasst, MA aber NICHT verheiratet/eingetr. Partnerschaft | «Konkubinatspartner gemeint? Typ umstellen» (findet Alt-Fälle wie Stingaciu) |
| W5 | K-Partner + Kind im Haushalt, Gemeinsam-Frage offen (NULL) | «Frage ‹gemeinsames Kind› beim Kind beantworten» |
| W6 | Gemeinsames Kind = Ja, Einkommensfrage beim K-Partner offen (NULL) | «Einkommensfrage beim Konkubinatspartner beantworten» |
| W7 | Gemischter Fall (gemeinsame + nicht-gemeinsame Kinder im Haushalt) | **«Mit QST-Behörde abklären»** — kein Tarifvorschlag |

## 6. Migration / bestehende Fälle

- Kein Daten-SQL nötig (nur zwei neue NULL-Spalten, idempotent beim Start).
  Falsch erfasste «Ehepartner» bei ledigen MA werden durch W4 sichtbar und
  von Hand umgestellt.

## 7. Etappe 2 (bewusst ZURÜCKGESTELLT)

- «Nie beide H1»-Kreuzkontrolle, wenn BEIDE Partner bei Schaub angestellt sind.

## 8. Umsetzungsstellen

- Migration idempotent Program.cs + `migrations-archive/add_konkubinat_felder.sql`
- `Models/EmployeeFamilyMember.cs` + `Data/AppDbContext.cs` + Familien-Controller
- `wwwroot/index.html` (Typ-Dropdown, Pillen, Sektions-Label «Partner/in»)
- `wwwroot/employees.js` (Sichtbarkeiten, Badges, Save, QST-Modal-Sperre)
- `Services/QstTarifVorschlagService.cs`/`-Logic` (Entscheidtabelle, Abklären-Fall)
- `Services/QstPflichtCheckService.cs` (W2–W7)
- `Controllers/EmployeeQuellensteuerController.cs` (Server-Guard: Flags aus Familie)
- Tests: `QstTarifVorschlagLogicTests` — Konkubinat-Fälle aus der Entscheidtabelle
