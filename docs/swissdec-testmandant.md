# Swissdec-Testmandant «Muster AG» auf test.onecrew.ch

Stand 07.09.2026 · Walter-Entscheid: Testinstanz komplett leeren und mit den
Swissdec-Testdaten neu aufbauen. Nur der Admin (walter.schaub@gmail.com), keine
weiteren Zugänge.

## Ausgangsmaterial

`SWISSCEC/Testmandant/` (Export aus Swissdecs Referenz-Lohnprogramm «SalaryCalculator»):

| Datei | Inhalt |
|---|---|
| `company_export.csv` | Muster AG (UID CHE-999.999.996), 6 Workplaces LU/BE/VD/TI/AG/ZG mit BUR- und Gemeindenummern, AHV/FAK/UVG/UVGZ/KTG/BVG/QST-Verhältnisse für 2024–2026 (AK-Wechsel Spida → kantonal per 2025) |
| `testcases_export.csv` | 44 Testfälle TF01–TF44, Personenstammdaten als JSON (110 Felder). **Achtung:** JSON-Spalte fehlerhaft quotiert — `""`→`"` ersetzen, führendes `"{`/schliessendes `}"` abschneiden, dann parsebar |
| `testcase_differences_export.csv` | 647 Mutationen entlang der Zeitachse (Austritte, Stunden, QST-Code-Wechsel, Umzüge, Zivilstand, Bewilligung, Nachzahlungen) |
| `wagetypes_export.csv` | 65 Referenz-Lohnarten (1000er-Schema) × Monate 2024-11 bis 2026-02 pro Testfall |

Soll-Ausgabe bisher nur für **Januar 2025** vorhanden (DeclareMonthlySalary QST BE/VD/TI + Statistik, von Walter am 07.09. geliefert). Bei Swissdec nachfragen, ob alle Monate/Jahresmeldungen exportierbar sind.

## Schritt 0 — Reset der Testinstanz (`Scripts/testmandant-reset.sh`)

Läuft auf dem VPS. Baut `hr_system_test` frisch auf (Schema-Bootstrap aus Prod, Erststart mit Seeds + Erst-Admin) und lädt danach NUR die Konfigurationstabellen aus `Scripts/testmandant-konfig-tabellen.txt` aus Produktiv: Lohnpositionen/ELM-Raster/Kontoplan-Mapping/Lohnschema, SV-Sätze, FZ-Tarife, Mindestlöhne, Absenz-/Zulagentypen, Kataloge (Nationalitäten, Bewilligungen, PLZ, Ausbildung, Job-Gruppen, Dokumenttypen, Banken, Behörden, Lohndatenempfänger), Texte/Vorlagen, Versandkategorien, app_setting (ohne Integrations-/Secret-Schlüssel).

Nicht übernommen (bewusst): alles mit Personenbezug, alle Schaub-Filialen/Hauptsitze/Filialzuordnungen, elm_stammdaten, Integrationen (easy@work, d.velop, eCall, SMTP), Benutzer ausser Walter, Dokumente auf der Platte.

Sicherheitsschalter: Versand komplett auf Umleitung, Integrations-Tabellen leer, Admin = HR-Team. Legt Hauptsitz «Muster AG» + Filiale «Hauptsitz Luzern» (Code LU) an, damit das Dashboard reagiert.

Aufruf:
```bash
scp Scripts/testmandant-reset.sh Scripts/testmandant-konfig-tabellen.txt ubuntu@83.228.209.119:~/
ssh ubuntu@83.228.209.119 'bash ~/testmandant-reset.sh'      # fragt «MUSTER» ab
```
Jederzeit wiederholbar (Reset-Knopf). **Erstmals erfolgreich gelaufen 07.09.2026 20:04** — 35 Tabellen geladen (`lohn_zulag_typ` existiert auf Prod nicht → wird übersprungen; `lse_code_mapping`/`lse_lohnart_mapping` sind auch auf Prod leer). Lehren aus dem Lauf: (1) das Kataloge-TRUNCATE CASCADE leert auch `app_user` → Erst-Admin wird beim Start neu angelegt, HR-Team-Flag erst danach setzen; (2) `session_replication_role` darf nur postgres setzen → Laden läuft als Hausmeister; (3) `company_profile.bur_nr` war varchar(8), Swissdec-BUR hat 9 Zeichen → Program.cs/AppDbContext auf 20 erweitert (Prod beim nächsten Deploy). Vorher wird die alte Test-DB nach `/var/backups/hr-system-test/db-vor-reset-*.dump` gesichert.

**Regel-Präzisierung (Runbook «kein pg_dump prod→test»):** Die Regel schützt Personendaten. Mandantenneutrale Konfiguration (Lohnstammdaten, Kontoplan, Kataloge) DARF übernommen werden — Tabellenliste ist die Whitelist; alles andere bleibt tabu.

## Importer (gebaut 07.09.2026)

`Controllers/SwissdecTestmandantController.cs` (`api/swissdec/testmandant`, admin, **403 ohne INSTANCE_LABEL** = nur Testinstanz) + Karte «Testmandant Muster AG» auf page-swissdec (`tmInit/tmSchritt` in swissdec.js, erscheint nur auf Test). CSVs liegen deploybar unter `Assets/Swissdec/Testmandant/`. Jeder Schritt: `GET schrittN/vorschau` (schreibt nichts) / `POST schrittN/anlegen` (idempotent: bestehende Datensätze werden aktualisiert). **Grundsatz Walter: keine Test-only-Strukturen** — der Importer nutzt nur normale Stammdaten (Hauptsitz, CompanyProfile …).

**Schritt 1 (gebaut, auf Test ausgeführt 07.09.2026 ~21:10 — Hauptsitz + 6 Filialen exakt angelegt, Filial-Selektor zeigt AG/BE/LU/TI/VD/ZG):** Hauptsitz «Muster AG» (UID, Sitzadresse) + 6 Filialen exakt nach `company_export.csv`: RestaurantCode = Kantonskürzel (#LU → LU), CompanyName «Muster AG», BranchName = Designation («Hauptsitz», «Werkhof/Büro», «Verkauf», «Beratung»), Strasse/Nr. getrennt, PLZ/Ort/Kanton, BurNummer, UidNummer, **BfsGemeindeNr (neues Feld `company_profile.bfs_gemeinde_nr`, Stammdaten-Maske «Betriebsangaben»)**, 42 Wochenstunden, Telefon/E-Mail an der Hauptsitz-Filiale. ELM-Generator: Workplace jetzt mit `ComplementaryLine` (= BranchName), Strasse+Nr., Country, Canton, MunicipalityID; workplaceID = «#»+RestaurantCode; Sitzadresse aus dem Hauptsitz. Hinweis: ZG hat in den Testdaten PLZ 6003 — bewusst nicht korrigiert. **BFS-Gemeindenummer-Quelle:** `swiss_location.bfs_nr` (haben wir für alle Ortschaften) — der Filial-PLZ-Lookup füllt das Feld automatisch (bei mehreren Ortschaften derselben Gemeinde ebenfalls), der ELM-Generator leitet bei leerem Feld über PLZ+Ort ab; das Feld selbst ist nur für Übersteuerungen (Testdaten, Mehrgemeinde-PLZ).

## Schritte 1–5 — Muster AG einladen (je ein kleiner Importer, prüfbar im UI)

1. **Filialen** BE/VD/TI/AG/ZG als weitere Filialen des Hauptsitzes Muster AG (BUR-Nr., Gemeinde-Nr., Kanton, Arbeitszeitmodelle 42/40 h, Lektionen) aus `company_export.csv`.
2. **Versicherer/Kassen** in den Empfänger-Katalog + Filial-Mitgliednummern + QST-Kundennummern (`CustomerIdentity` BE 9217.8 / VD 23.957.55.6 / TI 83189.7) + UVG-Betriebsteile/Prämiensätze + FAK-Ansätze pro Kanton + BVG-Codes. AK-Wechsel per 1.1.2025 abbilden (Gültig-ab im Katalog).
3. **44 Mitarbeiter** aus `testcases_export.csv`: Stammdaten, Adresse (inkl. Wochenaufenthalt), Bewilligung, Zivilstand, Ehepartner, Kinder (Start/End = QST ab/bis), Vertrag (Modell aus `PersonContract*`, Pensum, Wochenstunden, Lohn), QST-Erfassung (Code, Kanton, Gemeinde, Berechnungsmodell, Konfession, Nebenerwerb), UVG/UVGZ/KTG/BVG-Codes, Statistik-Felder (Ausbildung, Kader, Beruf, Ferien).
4. **Mutationen** aus `testcase_differences_export.csv` als datierte Änderungen (Austritt, QST-Versionen, Umzüge, Bewilligungswechsel…).
5. **Lohnläufe** 2024-11 … 2026-02 aus `wagetypes_export.csv`: Referenz-Lohnart → OneCrew-Lohnposition (Mapping-Tabelle, Lücken werden gemeldet), Perioden anlegen, Snapshots einfrieren.

Danach: E6 (QST-Monatsmeldung) und E7 (Statistik) gegen die Soll-XML Januar 2025 vergleichen; Abweichungen = unsere Arbeitsliste.

## Grundsatz: Eingabedaten sind massgebend, die Soll-XML NICHT nachbauen (Walter 07.09.2026)

Swissdec baut **absichtlich Fehler/Fallen** in die Testdaten ein, um sicherzustellen, dass die Software die Meldungen selbst aus ihren Daten erzeugt und nicht Kopien der Testdatensätze sendet. Darum: Import nimmt IMMER die CSV-Eingabedaten; unsere XML entsteht aus unserem Datenmodell + unserer Logik; die Soll-XML dient nur zum Aufspüren von Abweichungen — wo sie eine Falle enthält (z.B. PLZ «3008.00», doppeltes `Contractual13th`, ZG-BUR A38197423 statt CSV A38197421), muss unsere Ausgabe KORREKT sein und DARF abweichen. Niemals Werte «in Richtung XML» hinbiegen.

**Bewusste Fach-Abweichungen** (AHV 21, Ferien-Tage, Rundung, CSV vor XML-Falle): `docs/swissdec-abweichungsprotokoll.md`. Quality-Tool-Differenzen zuerst dort nachschlagen — Status BEWUSST = nicht fixen.

## Bekannte Widersprüche zwischen CSV und Soll-XML (vermutlich absichtlich)

- **ZG BUR-Nummer:** `company_export.csv` = A38197421, Soll-XML Januar 2025 = A38197423. Import übernimmt den CSV-Wert (bleibt so).
- **ZG PLZ:** 6003 statt 6300 in beiden Quellen — bewusst so übernommen.

## Bekannte Lücken (aus der ersten Sichtung)

- **QST-Tarife:** `Assets/Quellensteuer/` enthält nur 2026 für AG/BE/LU/SO/ZH. Für die Muster AG brauchen wir **2024 + 2025 für LU, BE, VD, TI** (ESTV-Tariftabellen).
- **BFS-Gemeindenummern** (MunicipalityID) fehlen im Datenmodell (nur PLZ/Ort).
- **Lektionenlohn, 14. Monatslohn, Kassenwechsel unter dem Jahr, Verwaltungsrat/administrativeBoard, NoTimeConstraint-Verträge** sind nicht vorgesehen.
- **Statistik-Stammdaten** (Ausbildung, Kaderstufe, Berufsbezeichnung) nur teilweise vorhanden; **Lohnausweis-Sonderfelder** (Expatriate-Ruling, Mitarbeiterbeteiligungen, Geschäftswagen-Optionen) fehlen.
- Wie viel davon Pflicht ist → Frage 7 im Gesprächsleitfaden (Pflichtumfang).

## Schritt 2 · Lohndaten-Empfänger (gebaut 07.09.2026)

`GET/POST api/swissdec/testmandant/schritt2/{vorschau|anlegen}` (`SwissdecTestmandantController.Schritt2.cs`).
Quelle `company_export.csv`, Spalten 2024/2025/2026:

| Empfänger | Nummern (Testdaten) | Zuordnung | Gültig |
|---|---|---|---|
| Spida Ausgleichskasse (079) | Kunde 7019.2 | alle 6 Filialen | bis 31.12.2024 |
| Ausgleichskasse Luzern (003) | Kunde 100-9976.9 | alle 6 | ab 01.01.2025 |
| Spida FAK (079) | Kunde 5676.3 | alle 6 | bis 31.12.2024 |
| FAK LU 003 / BE 002 / VD 022 / TI 021 / AG 019 / ZG 009 | 100-9976.70 / 100-2136.90 / 100-7766.80 / 100-5467.80 / 5050.1 / 100-3146.90 | je eigene Kanton-Filiale | ab 01.01.2025 |
| UVG Backwork S1000 (CHE-999.999.973) | Kunde 12577.2 · Vertrag 125 | alle 6 | ab 01.01.2021 |
| UVGZ Backwork S1000 | 7651-873.1 · 4566-4 | alle 6 | offen |
| KTG Backwork S1000 | 7651-873.1 · 4567-4 | alle 6 | offen |
| BVG Pensionskasse Oldsoft L1200 (CHE-999.999.967) | 1099-8777.1 · 4500-0 | alle 6 | ab 01.01.2021 |
| QST LU / BE / VD / TI | SSL 158.87.6 / 9217.8 / 23.957.55.6 / 83189.7 | alle 6 | offen |
| Lohnausweis BE / TI | 9217.8 / 83189.7 | alle 6 | offen |

Nicht in Schritt 2 (Tarif-Schritt): FAK-Ansätze, IKV (ZG→LU, TI), UVG-Lösungen A/B/P…, UVGZ/KTG-Codes 10/11/12, BVG-Codes 11/21/22/K2010, BVG-Reglement gültig ab (2021/2022/2023), Statistik #BFS.
Namen, die nicht in der CSV stehen (AK Luzern, Steuerverwaltungen), sind Platzhalter — im XML zählen Nummern/Kanton.

## Schritt 3a · AHV/ALV + FAK-Ansätze (gebaut 07.09.2026)
`schritt3/{vorschau|anlegen}` (`SwissdecTestmandantController.Schritt3.cs`): globale SV-Zeilen AHV 18–64 / 65+ (Freibetrag 16'800/12)
und ALV 18–64 (Höchstlohn 148'200/12) auf Testwerte setzen, ValidFrom ≤ 01.01.2024; FAK-Tarife LU/BE/VD/TI/AG ab 01.01.2024
(LU 200/210 ab 12/250 + GZ 1000; VD 300/380 ab 3. Kind, 360/440, GZ 1500), überlappende Prod-Kopien werden deaktiviert.
Offen → 3b: ALVZ 0.5 % (148'200–370'500) braucht Lohnband-Modell; Versicherungs-Lösungen mit Codes; ZG-FAK (IKV LU, keine Ansätze in CSV).

## Schritt 4a · Personen + Verträge (gebaut 07.09.2026)
`schritt4/{vorschau|anlegen}?nur=TF01,TF02` (`SwissdecTestmandantController.Schritt4.cs`), Quelle `testcases_export.csv`
(JSON pro Testfall, `""`→`"`, Excel-Seriendaten wie 48852.0 → Datum). Legt an: Employee (Personalnummer = Swissdec-Nr.),
Employment (FIX für Monatslohn, FLEX für Stundenlohn; manuell + easy@work-Block; Lektionenlohn), Zusatzadresse
«Wochenaufenthalt», Kinder/Ehepartner (QST-Abzugszeitraum), Versicherungs-Codes (UVG/UVGZ/KTG/BVG inkl. Zweitcode 12,
BVG-Eintrittsgrund/Arbeitsfähigkeit/manuelle Basis), QST-Erfassung (Kanton, Code, ab, Gemeinde, Nebenbeschäftigung,
Halbfamilie, Grenzgänger-Steuer-ID/Geburtsort/ab, Wochenaufenthalt). Nicht angelegt (Hinweise): Monatslohn-Beträge
(Lohnart 1000 → Schritt 5), Lohnausweis-Angaben, AHV/ALV-Sonderfall, Meldeverfahren-Bewilligungen, Ausbildungsstufen.
Neue Produktfelder dafür: `employee_quellensteuer.grenzgaenger_*`, `employee_versicherung_code.bvg_*`,
`employment.lesson_rate/weekly_lessons`; Vertrags-Pillen «easy@work»/«manuell»; Handerfassung setzt Block automatisch.

## Schritt 4b/4c (gebaut 07.09.2026, 4c noch nicht deployed/angelegt)
- 4b `schritt4b`: 65 Swissdec-Lohnarten der Testfälle via ELM-Lohnraster → Lohnpositionen (angelegt auf Test).
- 4c `schritt4c?monat=YYYY-MM&nur=TFxx` (`SwissdecTestmandantController.Schritt4c.cs`): 647 Mutationen →
  Personalstamm-Korrekturen, Wohnort-/Zivilstand-Historie, Partner/Kind, Austritt (ExitDate/KuendigungPer,
  Vertragsende), neuer Vertragsabschnitt ab Monatsanfang (Vorgänger endet Vortag), Versicherungs-Codes ab
  Monatsanfang (Mehrfachcodes 11+12), QST-Eintrag ab «gültig ab» (Vorgänger endet Vortag; Kopie aller Felder).
  Monatswerte (Stunden, Lektionen, Arbeitstage CH/effektiv, Nachzahlung nach Austritt, BVG-Basis, Telearbeit,
  AHV-Splitting, Rektifikat) nur angezeigt → Schritt 5. Chronologisch anlegen (leer = alle Monate).
- Offen vor Schritt 5: Ferienregelung Muster AG (4 Wo = 8.33 %, 25/30 Tage ab 50/60) als Filial-Einstellung;
  Monatslohn/Stunden/Zulagen aus wagetypes_export.csv in den Lohnlauf; QST-Jahresmodell TI/VD; Tarifdateien
  2024/2025 LU/BE/VD/TI.
