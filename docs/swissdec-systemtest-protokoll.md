# Swissdec ELM Systemtest — Protokoll

Was wir zu welchem Prüfpunkt gemacht haben, wo es in OneCrew steht und was offen ist.
**Zweck:** vor jedem Swissdec-Call nachlesen — analog `docs/swissdec-foundation-protokoll.md`.

System: **OneCrew** · Testmandant Muster AG · ELM 6.0 · nur **test.onecrew.ch**
Quality Tool: `https://test.swissdec.ch/qualitytool/stable` → ELM

Quellen (Walter 16.10.2026):
- **Systemtest (ERP):** Run **24364** · `schaub_run24364_de_20261016.pdf` · 123 Prüfpunkte
- **Systemtest (Tech):** Run **26667** · `schaub_run26667_de_20261016.pdf` · 45 Prüfpunkte (Gates)
- Maschinenlesbarer Katalog: `docs/swissdec-systemtest-katalog.json`
- Text-Extrakte: `.tmp/schaub_run24364_de_20261016.txt`, `.tmp/schaub_run26667_de_20261016.txt`

---

## Die drei Säulen im Quality Tool

| QT-Menü | Was es prüft | Protokoll |
|---|---|---|
| **Systemtest** | ERP: Stammdaten, Validierung, Abbildung in der Meldung (Directive 4.x …) | **dieses Dokument · ERP** |
| **Systemtest (Tech)** | Übermittlung, Sync, Fault, Completion, Dialog, Barcode | **dieses Dokument · Tech** |
| **Calculation Test** | Monats-/Jahreslohn vs. Referenz | `docs/swissdec-qualitytool-protokoll.md` |
| Foundation | Verbindung, WS-Security, SUA | `docs/swissdec-foundation-protokoll.md` |

---

## Stand Übersicht

> **Aktuell 17.10.2026 (Nacht):** Katalog aus beiden PDF-Runs extrahiert. ERP: **6/123** gebaut (S01 Unternehmensstammdaten weitgehend). Tech: **0/45** noch nicht systematisch abgearbeitet (vieles überlappt Foundation/Cockpit).

### Systemtest ERP (Run 24364)

| Gruppe | Thema | Checks | gebaut | Stand |
|---|---|---:|---:|---|
| S_ELM_V6_01 | Unternehmen | 15 | **6** | teilweise |
| S_ELM_V6_02 | Angaben zu Datenempfängern | 25 | **4** | AHV/ALV 01–04 |
| S_ELM_V6_03 | Personendaten | 59 | **0** | offen |
| S_ELM_V6_04 | Prozessuale Testfälle | 13 | **0** | offen |
| S_ELM_V6_05 | Cockpit Testfälle | 11 | **0** | offen |
| **Total ERP** | | **123** | **10** | |

### Systemtest Tech (Run 26667)

| Gruppe | Thema | Fälle | Gates | Stand |
|---|---|---:|---:|---|
| ST_ELM_V6_01 | Lohnmeldung | 4 | 8 | offen |
| ST_ELM_V6_02 | Synchronisierung | 7 | 18 | offen |
| ST_ELM_V6_03 | Completion | 3 | 3 | offen |
| ST_ELM_V6_04 | Lohnausweis / Barcode | 2 | 6 | offen |
| ST_ELM_V6_05 | DialogMessage ELM | 2 | 3 | offen |
| ST_ELM_V6_06 | Warnung / Information | 2 | 4 | offen |
| **Total Tech** | | **20** | **42** | QT zeigt 45 |

---

## Oberfläche in OneCrew

### Handbuch für die Prüfung (itserv-Stil, 17.10.2026)

Druckbares Punkt-für-Punkt-Handbuch (Wo in OneCrew · So handhabt OneCrew · Du prüfst), analog Foundation:

- **HTML:** `docs/swissdec-vorfuehrung/systemtest-handbuch.html` (Browser öffnen → Drucken)
- **Neu erzeugen** nach Katalog-Änderung: `python3 Scripts/swissdec-systemtest-handbuch.py`
- Foundation bleibt: `docs/swissdec-vorfuehrung/vorfuehrung.html`

### Wegleitung Systemtest (wie Foundation, 17.10.2026)

Entwicklung → Swissdec → **Systemtest** (`page-swissdec-systemtest`, `js/swissdec-systemtest.js`).

Gleicher Aufbau wie der Kommunikations-Test:
Kopf-Kacheln · Reiter S01–S05 (ERP) + T01–T06 (Tech) · Liste links · Detail rechts
mit Schritten 1–3 · Bewertung Bestanden/Nicht bestanden/Offen.

- Katalog: `GET /api/elm/systemtest/katalog` ← `wwwroot/data/swissdec-systemtest-katalog.json`
- Stand: `GET/PUT /api/elm/systemtest/stand` (Datei neben den Zertifikaten, wie Foundation)
- Pro Punkt: «Öffnen: …» springt in die passende OneCrew-Maske

### Fach-Masken

- **Hauptsitz** (System → Filialen & Benutzer → Hauptsitze): Reiter Hauptdaten · Kontaktperson · Stellvertreter · Vertragsregeln; Arbeitszeitmodelle auf der Karte.
- **Filiale** Stammdaten: BUR / InHouseID, BFS-Gemeindenummer.
- **ELM-Cockpit:** `page-elm-meldungen` / Lohnmeldungen (ELM).
- **Kommunikations-Test:** Foundation (getrennt).
- Code: `Services/Elm/ElmGemeinsam.cs`, `ElmMonthlyDeclarationBuilder.cs`, `HauptsitzController`, `wwwroot/js/hauptsitze.js`.

---

## Systemtest ERP — Prüfpunkte

Arbeitsweise: Punkt öffnen → gebaut? → Lücke schliessen → Kommentar ins QT → hier `stand` + `kommentarQt` nachziehen (auch im JSON).

## S_ELM_V6_01 — Unternehmen

### Unternehmensstammdaten (`01_01`)

#### S_ELM_V6_01_01_01 Name und Adresse des Unternehmens 🔧 gebaut

**Directive** 4.2

**Beschreibung:** Name und Adresse des Unternehmens - Name des Unternehmens - Zusatzzeile für Abteilungsname, Filialbezeichnungen usw. - Zusatzzeile für die Postadresse - Strasse und Hausnummer - Lokalität wie Region, Provinz - Postfach - Postleitzahl - Ort - Land - Gemeindenummer

**Erwartet:** Folgende Adresse muss erfasst werden: Muster AG Bahnhofstrasse 1 6003 Luzern SWITZERLAND - Prüfen ob die Adresse ohne Name des Unternehmens, Postleitzahl oder Ort gespeichert werden kann. - Prüfen ob das Land korrekt im XML abgefüllt wird. Es muss immer der ausgeschriebene Englische Begriff in Grossbuchstaben abgefüllt werden. Die Auswahl vom Land muss via Dropdown erfolgen. - Prüfen ob beim Hauptsitz eine Gemeindenummer hinterlegt werden kann. Die Gemeindenummer muss zwar nicht übermittelt werd…

**Kommentar fürs Quality Tool:**
```
OneCrew: Hauptsitz-Maske (Reiter Hauptdaten) — Name, Zusatzzeilen, Strasse/Postfach, Lokalität, PLZ/Ort/Kanton, Land aus Dropdown (XML: SWITZERLAND), BFS aus Ortschaftsverzeichnis. Speichern ohne Name/PLZ/Ort abgelehnt.
```

**Notiz:** Hauptsitz Hauptdaten; Land=SWITZERLAND; BFS read-only; PLZ 3000 Bern 1 (SchemaStand 80).


#### S_ELM_V6_01_01_02 Allgemeine Kontaktperson für Rückfragen zu Übermittlungen 🔧 gebaut

**Directive** 4.1

**Beschreibung:** Allgemeine Kontaktperson für Rückfragen zu Übermittlungen Im ERP-System muss eine Kontaktperson hinterlegt werden können. Folgende Angaben müssen erfasst werden können: - Name - E-Mail Adresse - Telefonnummer - Mobilnummer Die Elemente "Name" und "Telefonnummer" sind für sämtliche Übermittlungen obligatorisch. Wichtig: Für sämtliche Übermittlungen an das BFS ist zusätzlich die E-Mail-Adresse anzugeben. Andernfalls wird die Meldung vom Distributor abgelehnt.

**Erwartet:** Folgende Angaben müssen erfasst werden: Hans Muster hans.muster@suva.ch 044 444 44 44 079 999 99 99 - Prüfen ob die Angaben erfasst werden können. - Sicherstellen, dass vor oder bei der Übermittlung ans BFS geprüft wird, ob bei der Kontaktperson eine E-Mail Adresse hinterlegt wurde.

**Kommentar fürs Quality Tool:**
```
Kontaktperson am Hauptsitz (Name, E-Mail, Telefon, Mobil). Speichern +41…, ELM-XML national 079…. BFS-Regel 042: E-Mail Pflicht vor Übermittlung.
```

**Notiz:** Reiter Kontaktperson; TelefonFuerSwissdec.


#### S_ELM_V6_01_01_03 UID-BFS 🔧 gebaut

**Directive** 4.2

**Beschreibung:** UID-BFS Die UID-BFS muss für das Unternehmen erfasst werden können. Falls die UID unbekannt ist, kann im ERP- System "unbekannt" ausgewählt werden. Die UID-BFS muss im ERP anhand der Prüfziffer validiert werden.

**Erwartet:** Folgende UID-BFS muss erfasst werden: CHE-999.999.996 - Prüfen das "unbekannt" ausgewählt werden kann - Prüfen ob beim Speichern der UID-BFS "CHE-999.999.997" eine Fehlermeldung angezeigt wird

**Kommentar fürs Quality Tool:**
```
OneCrew Hauptsitz: UID-BFS mit Prüfziffer (CHE-999.999.996 ok, CHE-999.999.997 Fehler). Häkchen «unbekannt» → ELM UID-BFS/Unknown. EHRA entfällt.
```

**Notiz:** UidBfsFehler / UidUnbekannt; ElmUidPruefzifferTests.


#### S_ELM_V6_01_01_04 Arbeitsort 🔧 gebaut

**Directive** 4.2.2

**Beschreibung:** Arbeitsort Es ist erforderlich, mehrere Arbeitsorte zu verwalten. Mindestens ein Arbeitsort muss immer erfasst sein. Jedem Arbeitsort können folgende Informationen zugeordnet werden: - Adressinformationen - Kanton und Gemeindenummer - BUR-Nummer und/oder unternehmensinterne Nummer (InHouseID) - Jedem Arbeitsort muss eine auf Unternehmensebene erfasste Familienausgleichskasse zugeordnet werden können

**Erwartet:** Folgender Arbeitsort muss erfasst werden: Werkhof/Büro Zeughausgasse 9 3011 Bern SWITZERLAND A89058593 - Prüfen ob die Postleitzahl, der Ort und die dazugehörige Gemeindenummer (351) im System plausibilisiert werden und die Adresse mit einem offiziellen Register abgeglichen wird. Es ist wichtig, dass der Ort auch nach der erfolgten Zuweisung noch manuell überschrieben werden kann. - Prüfen ob ein Arbeitsort ohne die zwingenden Adresselemente (Ort, Postleitzahl) gespeichert werden kann. - Prüfen ob beim Speichern der BUR-Nummer "A89058592" eine Fehlermeldung angezeigt wird. - Mit der InHouseID "100_10" prüfen ob anstatt einer BUR-Nummer eine InHouseID erfasst werden kann. - Prüfen ob auf dem Arbeitsort eine Verbindung zur gültigen Familienausgleichskasse besteht.

**Kommentar fürs Quality Tool:**
```
Arbeitsorte = Filialen (Workplace). Muster AG: Werkhof/Büro Zeughausgasse 9, 3011 Bern, BUR A89058593, BFS 351 (AMTOVZ). PLZ→Ort/Kanton/BFS aus Ortschaftsverzeichnis, Ort danach manuell überschreibbar. Speichern ohne PLZ/Ort abgelehnt. BUR A89058592 → Prüfziffer-Fehler. Alternative InHouseID (z.B. 100_10) statt BUR. FAK über Tab «Lohndaten Empfänger» (Art FAK) → Stammdaten zeigt Zuordnung. XML: WorkplaceIdentification BUR-REE oder InHouseID + MunicipalityID.
```

**Notiz:** Filiale Stammdaten + InHouseID; FAK/InHouseID in Stammdaten-Ansicht 17.10.2026.


#### S_ELM_V6_01_01_05 Adressierung von Empfängern - Nullmeldung FAK 🔧 gebaut

**Directive** 8.2.4

**Beschreibung:** Adressierung von Empfängern - Nullmeldung FAK In der Domäne FAK werden mit ELMv6 sämtliche Familienausgleichskassen adressiert, die einem Arbeitsort zugewiesen sind. Dies ist im Gegensatz zu ELMv5 auch dann der Fall, wenn einem Arbeitsort keine Personen zugewiesen sind. Ein ERP-Hersteller muss sicherstellen, dass im XML nur die Institutionen adressiert werden, die auch eine Übermittlung erhalten sollten. Stand heute wird eine Übermittlung ohne Personen ausschliesslich für die Domäne FAK verteilt…

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Sicherstellen, dass das Konzept der Nullmeldungen verstanden wurde und die Adressierung im ERP-System dahingehend angepasst wurde, dass nur noch die Empfänger im Job aufgeführt werden, für die auch eine Übermittlung verteilt werden soll. Diese Logik kann auch über das Element "ProcessByDistributor" (true/false) gesteuert werden.

**Kommentar fürs Quality Tool:**
```
FAK-Zuordnung Filiale → Tab «Lohndaten Empfänger» (Art FAK). Jahres-XML (DeclareAnnualSalary): Job/Institutions/Totals nur für FAKs mit gültiger Zuordnung am 31.12. an einem Workplace der Rechtseinheit — auch ohne Personen dort (Nullmeldung, Totals 0.00). Abgelaufene/nicht zugewiesene Kassen fehlen. ProcessByDistributor=true. Logik ElmFakAdressierung. Monatsmeldung hat noch keine FAK-Domäne.
```

**Notiz:** FAK-Nullmeldung Jahres-XML 17.10.2026 (ElmFakAdressierung + ElmAnnualDeclarationBuilder).


#### S_ELM_V6_01_01_06 Wöchentliche Unternehmensarbeitszeit 🔧 gebaut

**Directive** 4.2.3

**Beschreibung:** Wöchentliche Unternehmensarbeitszeit In der ERP-Lösung müssen Arbeitszeitmodelle unabhängig vom Arbeitsort erfasst werden können. Jedes Arbeitszeitmodell muss mit einer Bezeichnung versehen werden. Es sind folgende Kombinationen möglich: - Eingabe von Stunden z.B. 41.75 - Eingabe von Lektionen z.B. 20.00 - Eingabe von Stunden z.B. 41.75 und Lektionen z.B. 20.00 Zusätzlich können auf den Arbeitszeitmodellen die Ferientage pro Kalenderjahr geführt werden, die den jeweiligen Arbeitnehmern über die …

**Erwartet:** Die verschiedenen Kombinationen und Formate können erfasst werden - Prüfen ob mehrere Arbeitszeitmodelle erfasst werden können. - Prüfen ob die Formatvorgaben eingehalten werden (00.00). - Prüfen was passiert, wenn die Elemente leer gelassen oder mit 0.00 befüllt werden. In diesen Fällen darf eine Übermittlung via ELM nicht zugelassen werden. - Prüfen ob auf dem Arbeitszeitmodell die Anzahl Ferien pro Kalenderjahr geführt werden können.

**Kommentar fürs Quality Tool:**
```
Mehrere Arbeitszeitmodelle am Hauptsitz. Format HH.MM; Löschen gesperrt wenn in Nutzung. ELM CompanyWorkingTime.
```

**Notiz:** AZM-UI 16.10.2026.


#### S_ELM_V6_01_01_07 Angaben zum Stellvertreter z.B. Treuhandbüro 🔧 gebaut

**Directive** 4.2.4

**Beschreibung:** Angaben zum Stellvertreter z.B. Treuhandbüro In der ERP-Lösung müssen folgende Angaben zum Stellvertreter hinterlegt werden können: - Name des Stellvertreters - Firmeninhaber - Zusatzzeile für die Postadresse - Strasse und Hausnummer - Postfach - Lokalität wie Region, Provinz - Postleitzahl - Ort - Land - UID-BFS

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Prüfen ob die Angaben zum Stellvertreter im ERP-System hinterlegt werden könenn.

**Kommentar fürs Quality Tool:**
```
Delegate am Hauptsitz: Name, Adresse, UID-BFS (optional Owner). Keine Kontaktfelder (XSD). Kontakt = ContactPerson.
```

**Notiz:** Reiter Stellvertreter.


### Lohnarten & Domänen-Flags (`01_02`)

#### S_ELM_V6_01_02_01 Lohnartenverwaltung ⬜ offen

**Directive** 5.0

**Beschreibung:** Lohnartenverwaltung In der Lohnarten-Verwaltung ist definiert, wie eine Lohnart im Bereich Versicherung als pflichtig, nicht pflichtig, negativ oder positiv gesteuert ist.

**Erwartet:** Klare Definition der Lohnartensteuerung für den Bereich Versicherung. - Prüfen ob für sämtliche Lohnarten die Steuerung zur pflichtigkeit bzgl. Versicherungen vorhanden ist. - Prüfen ob die Lohnarten als positive oder negative Lohnarten im Lohnartenstamm ausgewiesen sind.


#### S_ELM_V6_01_02_02 BVG ⬜ offen

**Directive** 5.0

**Beschreibung:** BVG Einer Lohnart wird für die Ermittlung der BVG-Basis die "rückwirkende Betrachtung" oder die "voraussichtliche Betrachtung" und diese zusätzlich mit einem Faktor (z.B. x 13) zugeordnet.

**Erwartet:** Klare Definition der Lohnartensteuerung für den Bereich BVG. - Prüfen ob Lohnarten der rückwirkenden oder voraussichtlichen Betrachtung zugeordnet werden können. - Prüfen ob einer Lohnart ein Faktor zugewiesen werden kann oder wie die Aufrechnung im ERP-System erfolgt.


#### S_ELM_V6_01_02_03 Lohnausweis ⬜ offen

**Directive** 5.0

**Beschreibung:** Lohnausweis Die Zuweisung der Lohnarten zu den Ziffern des Lohnausweises sind ersichtlich.

**Erwartet:** Klare Definition der Lohnartensteuerung für den Bereich Lohnausweis. - Prüfen ob eine Lohnart einer Lohnausweis-Ziffer zugeordnet werden kann.


#### S_ELM_V6_01_02_04 Quellensteuer ⬜ offen

**Directive** 5.0

**Beschreibung:** Quellensteuer In der Lohnarten-Verwaltung ist definiert, wie eine Lohnart im Bereich Quellensteuer gesteuert wird. Es werden folgende zwei Steuerungen integriert: - Periodizität: Periodisch/Aperiodisch - Charakter: Monatlich/Jährlich

**Erwartet:** Klare Definition der Lohnartensteuerung für den Bereich Quellensteuer. - Prüfen ob einer Lohnart die Periodizität der Quellensteuer zugewiesen werden kann. - Prüfen ob einer Lohnart der Quellensteuer-Charakter hinterlegt werden kann. Hinweis: Wenn der ERP-Hersteller andere Formulierungen für die Steuerungen verwendet, ist dies für den Testfall okay.


#### S_ELM_V6_01_02_05 Bereich Statistik ⬜ offen

**Directive** 5.0

**Beschreibung:** Bereich Statistik Die Zuweisung der Lohnarten zu den Lohnbasen in der Statistik sind ersichtlich.

**Erwartet:** Klare Definition der Lohnartensteuerung für den Bereich Statistik. - Prüfen ob die Lohnarten den korrekten Statistikbasen (Monthly/Annual) zugeordnet werden können.


#### S_ELM_V6_01_02_06 Lohnartenstamm ⬜ offen

**Directive** 5.1.3

**Beschreibung:** Lohnartenstamm Kurzfassung Lohnartenstamm

**Erwartet:** Darstellung und Export - Prüfen ob der Lohnartenstamm in geeigneter Form dargestellt und als Excel exportiert werden kann.


#### S_ELM_V6_01_02_07 Löschen von Lohnarten ⬜ offen

**Directive** 5.1

**Beschreibung:** Löschen von Lohnarten Verwendete Lohnarten dürfen nicht mehr gelöscht werden

**Erwartet:** Anhand von folgendem Beispiel prüfen, ob bereits verwendete Lohnarten gelöscht werden können: - Verarbeitung Zahltag November 2021 mit Lohnart Monatslohn - Die Lohnart Monatslohn löschen - Fehlermeldung prüfen (Löschen der Lohnart nicht möglich oder ähnliche Reaktion) Falls Lohnarten generell nicht gelöscht werden können, gilt der Test als bestanden.


#### S_ELM_V6_01_02_08 Rückwirkende Lohnartenmutation ⬜ offen

**Directive** 5.1

**Beschreibung:** Rückwirkende Lohnartenmutation Die rückwirkende Mutation einer verwendeten Lohnart ist grundsätzlich nicht erlaubt.

**Erwartet:** Anhand vom Beispiel aus Testfall 12 prüfen, ob bereits verwendete Lohnarten mutiert werden können: - Die Lohnart "Monatslohn" mutieren und von der AHV-pflichtigkeit ausnehmen. - Fehlermeldung prüfen (Mutation der Lohnart nicht möglich oder ähnliche Reaktion) Falls die Lohnart generell nicht mutiert werden kann, gilt der Test bestanden.


## S_ELM_V6_02 — Angaben zu Datenempfängern

### AHV / ALV (`02_01`)

#### S_ELM_V6_02_01_01 Angaben zur AHV-Ausgleichskasse 🔧 gebaut

**Directive** 8.1.3

**Beschreibung:** Angaben zur AHV-Ausgleichskasse Folgende Angaben zur AHV-Ausgleichskasse müssen im ERP-System erfasst werden können: - Nummer der Ausgleichskasse - Mitgliednummer - Subnummer

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Begriffsdefinitionen im ERP-System prüfen. Die Elemente "Nummer der Ausgleichskasse", "Mitgliednummer" und "Subnummer" müssen im ERP-System vorhanden sein und identisch benannt werden. - Anhand vom Beispiel "000.003" prüfen ob die Nummer der Ausgleichskasse plausibilisiert wird. Der erste Block (im Beispiel "003" muss grösser als 0 sein). In diesem Fall muss eine Fehlermeldung angezeigt werden.

**Kommentar fürs Quality Tool:**
```
OneCrew: Lohndaten-Empfänger, Art Ausgleichskasse — Feld «Nummer der Ausgleichskasse». «000.003» wird abgelehnt (erster Block grösser als 0). «003» und «070.000» bleiben gültig. Mitgliednummer und Subnummer an der Filiale, Reiter Lohndaten-Empfänger.
```

**Notiz:** Walter 25.10.2026 bestätigt (⋮ Bearbeiten, 000.003).


#### S_ELM_V6_02_01_02 Beginn und Ende der AHV-Beitragspflicht 🔧 gebaut

**Directive** 8.1.1

**Beschreibung:** Beginn und Ende der AHV-Beitragspflicht 18 = Beginn der Beitragpflicht 65 = Ende der Beitragspflicht für Männer 65 = Ende der Beitragspflicht für Frauen

**Erwartet:** Die Angaben zur Beitragspflicht werden im ERP-System zentral geführt. - Prüfen ob die drei Angaben zur Beitragspflicht in der AHV vorhanden sind. - Prüfen ob im ERP-System mit Übergangslösungen wie bei AHV21 umgegangen werden kann. Im Berechnungstest wird dieser Fall nicht geprüft.

**Kommentar fürs Quality Tool:**
```
OneCrew: SV-Sätze, Leiste «AHV-Beitragspflicht». Zentral Beginn 18, Ende Männer 65, Ende Frauen 65. AHV 21 Übergang Frauen: bis 1960 = 64 Jahre, 1961 + 3 Monate, 1962 + 6, 1963 + 9, ab 1964 = 65. Der Lohnlauf liest diese Werte.
```

**Notiz:** Walter 25.10.2026, Leiste abgenommen. Engine AhvBeitragspflichtStand.


#### S_ELM_V6_02_01_03 AHV-Grundlagen 🔧 gebaut

**Directive** 8.1.1

**Beschreibung:** AHV-Grundlagen AHV-Freigrenze 1'400 Monat oder 16'800 Jahr AHV-Arbeitnehmer-Beiträge 5.3000%

**Erwartet:** Die Angaben zur AHV-Freigrenze und zu AHV-Arbeitnehmer-Beiträgen werden im ERP-System zentral geführt. - Prüfen ob die Werte korrekt hinterlegt sind - Prüfen ob der Beitragssatz mit 4 Nachkommastellen dargestellt wird. Ein ERP-Hersteller muss mit 4 Nachkommastellen umgehen können.

**Kommentar fürs Quality Tool:**
```
OneCrew: SV-Sätze, Zeile AHV / IV / EO (65+). Freibetrag 1'400 pro Monat (16'800 pro Jahr). Arbeitnehmer-Beitrag 5,3000 %, vier Nachkommastellen in der Liste und in der Einzelansicht.
```

**Notiz:** Jahresbetrag ist dieselbe Grenze, geführt als Monat.


#### S_ELM_V6_02_01_04 ALV/ALVZ-Grundlagen 🔧 gebaut

**Directive** 8.1.1

**Beschreibung:** ALV/ALVZ-Grundlagen ALV-Höchstlohn 12'350 pro Monat oder 148'200 pro Jahr ALV-Arbeitnehmer-Beiträge 1.1000% ALVZ-Höchstlohn Von: 12'350 pro Monat oder 148'200 pro Jahr Bis: 26'250 pro Monat oder 315'000 pro Jahr, im Moment ohne Obergrenze ALVZ-Arbeitnehmer-Beiträge 0.5000%

**Erwartet:** Die ALV/ALVZ-Grundlagen werden im ERP-System zentral geführt. - Prüfen ob die ALV-Höchstlöhne und die ALV-Beiträge korrekt hinterlegt sind. Die Beiträge müssen mit 4 Nachkommastellen dargestellt werden. - Prüfen ob die ALVZ-Höchstlöhne, wie auch die ALVZ-Beiträge im Falle von einer erneuten Einführung, im ERP- System hinterlegt werden können. Die Beiträge müssen mit 4 Nachkommastellen dargestellt werden.

**Kommentar fürs Quality Tool:**
```
OneCrew: SV-Sätze. ALV Höchstlohn 12'350 pro Monat, Beitrag 1,1000 %. ALVZ auf derselben Maske: Lohnband ab 12'350, Höchstlohn leer = keine Obergrenze, Beitrag 0,5000 % mit vier Nachkommastellen.
```

**Notiz:** Jahresgrenzen 148'200 / 315'000 = Monat × 12. ALVZ-Obergrenze bleibt leer, bis sie wieder gilt.


### Familienausgleichskasse (`02_02`)

#### S_ELM_V6_02_02_01 Angaben zur Familienausgleichskasse ⬜ offen

**Directive** 8.2.3

**Beschreibung:** Angaben zur Familienausgleichskasse Folgende Angaben zur Familienausgleichskasse müssen im ERP-System erfasst werden können: - Nummer der Familienausgleichskasse - Mitgliednummer - Subnummer

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Begriffsdefinitionen im ERP-System prüfen. Die Elemente "Nummer der Familienausgleichskasse", "Mitgliednummer" und "Subnummer" müssen im ERP-System vorhanden sein und identisch benannt werden. - Anhand vom Beispiel "000.003" prüfen ob die Nummer der Familienausgleichskasse plausibilisiert wird. Der erste Block (im Beispiel "003" muss grösser als 0 sein). In diesem Fall muss eine Fehlermeldung angezeigt werden.


#### S_ELM_V6_02_02_02 Interkantonale Vereinbarungen ⬜ offen

**Directive** 8.2.4.2

**Beschreibung:** Interkantonale Vereinbarungen Eine interkantonale Vereinbarung bedeutet, dass die Zweigniederlassung dem Familienzulagengesetz des Kantons unterstellt wird, in dem der Hauptsitz liegt. Die Familienzulagen werden damit vollständig über die Kasse im Kanton des Hauptsitzes abgerechnet. Beitragssatz und Zulagenhöhe an den Arbeitgeber entsprechen derjenigen des Hauptsitzkantons. Die interkantonalen Vereinbarungen können nur zwischen kantonalen Ausgleichskassen vereinbart werden. In einer ERP-Lösung m…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfung ob bei einer kantonalen Familienausgleichskasse eine interkantonale Vereinbarung hinterlegt werden kann. Ziel davon ist es, die Adressierung dieser Familienausgleichskasse in der Übermittlung an eine andere Familienausgleichskasse umzuleiten.


#### S_ELM_V6_02_02_03 FAK-Grundlagen ⬜ offen

**Directive** 8.2.4

**Beschreibung:** FAK-Grundlagen Es gibt zwei Arten von Familienausgleichskassen. Im ERP-System muss mit Verbandsausgleichskassen, wie auch kantonalen Familienausgleichskassen umgegangen werden können. Wichtig ist dabei, dass das ERP-System automatisch erkennt ob es sich bei der hinterlegten Kasse um eine kantonale Familienausgleichskasse oder eine Verbandsausgleichskasse handelt. Dies kann anhand der auf swissdec.ch publizierten Liste der empfangsbereiten Datenempfänger automatisiert werden. Beginn und Ende der …

**Erwartet:** Folgende Elemente müssen geprüft werden: - Prüfung ob im ERP-System eine Familienausgleichskasse abgebildet werden kann. - Prüfen ob im ERP-System die Erkennung von kantonalen, wie auch Verbandsausgleichskassen automatisiert wurde. Dies kann auch mündlich bestätigt werden, da dies im Berechnungstest geprüft wird. - Prüfung ob mehrere Familienausgleichskassen hinterlegt werden können. Falls in der ERP-Lösung Angaben zu Beginn/Ende der Bezugsberechtigung, Höhe und Art der Zulagen, wie auch der Bei…


### UVG (`02_03`)

#### S_ELM_V6_02_03_01 Angaben zur UVG-Versicherung ⬜ offen

**Directive** 8.3.3

**Beschreibung:** Angaben zur UVG-Versicherung Folgende Angaben zur Unfallversicherung müssen im ERP-System erfasst werden können: - Versicherernummer - Kundennummer - Vertrags-/Subnummer

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Begriffsdefinitionen im ERP-System prüfen. Die Elemente "Versicherernummer", "Kundennummer" und "Vertrags-/Subnummer" müssen im ERP-System vorhanden sein und identisch benannt werden.


#### S_ELM_V6_02_03_02 Angaben zum UVG-Versicherer ⬜ offen

**Directive** 8.1.3

**Beschreibung:** Angaben zum UVG-Versicherer Folgende Angaben zum Unfallversicherer müssen im ERP-System erfasst werden können: - Name des UVG-Versicherers - UID-BFS des UVG-Versicherers Eingabe der UID-BFS oder Auswahl "unbekannt" - Gültig-ab Datum ab wann der UVG-Versicherer zuständig ist

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - UID-BFS: Prüfen ob "unbekannt" ausgewählt werden kann und anhand der UID-BFS "CHE-999.999.997" prüfen, ob eine Fehlermeldung bei einer invaliden Nummer angezeigt wird. - Prüfen ob die zwei Elemente "Name" und "Gültig-ab-Datum" erfasst werden können.


#### S_ELM_V6_02_03_03 Mehrere UVG-Versicherer ⬜ offen

**Directive** 8.3.4

**Beschreibung:** Mehrere UVG-Versicherer Im ERP-System müssen mehrere UVG-Versicherer erfasst werden können. Grund dafür liegt beim Spezialfall von öffentlichen Verwaltungen.

**Erwartet:** Der Anwendungsfall von öffentlichen Verwaltungen wird wie folgt geprüft: - Prüfen ob in der Benutzeroberfläche mehrere UVG-Versicherer erfasst und unterschiedlichen Personen zugewiesen werden können.


#### S_ELM_V6_02_03_04 Angaben zur UVG-Versicherungslösungen ⬜ offen

**Directive** 8.3.2

**Beschreibung:** Angaben zur UVG-Versicherungslösungen UVG-Code mit zwei Stellen, inkl. Beschreibung der Versicherungslösung. 1. Stelle Betriebsteil: Die Auswahl von A - Z ist möglich Beispiel: A Gesamtes Personal

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die erste Stelle vom UVG-Code auf Buchstaben beschränkt ist. (A-Z) - Prüfen ob eine UVG-Versicherungslösung mit einer Beschreibung versehen werden kann.


#### S_ELM_V6_02_03_05 UVG Grundlagen ⬜ offen

**Directive** 8.3.1

**Beschreibung:** UVG Grundlagen UVG-Höchstlohn 12'350 pro Monat oder 148'200 pro Jahr Beitragsssätze müssen pro Versicherungslösung erfasst werden können. Beispiel: - A1 = NBU 1.4600% | BU 1.200% - B1 = NBU 1.6200% | BU 1.300%

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Höchstlöhne im System hinterlegt sind. - Prüfen ob die Beitragssätze pro Versicherungslösung erfasst werden können. - Prüfen ob die Beitragssätze mit 4 Nachkommastellen erfasst werden können.


### UVGZ (`02_04`)

#### S_ELM_V6_02_04_01 Angaben zur UVGZ-Versicherung ⬜ offen

**Directive** 8.4.2

**Beschreibung:** Angaben zur UVGZ-Versicherung Folgende Angaben zur Unfallzusatzversicherung müssen im ERP-System erfasst werden können: - Name des UVGZ-Versicherers - Versicherernummer - Kundennummer - Vertragsnummer

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Begriffsdefinitionen im ERP-System prüfen. Die Elemente "Versicherungsname", "Versicherernummer", "Kundennummer" und "Vertragsnummer" müssen im ERP-System vorhanden sein und identisch benannt werden.


#### S_ELM_V6_02_04_02 Angaben zur UVGZ-Versicherungslösung ⬜ offen

**Directive** 8.4.1

**Beschreibung:** Angaben zur UVGZ-Versicherungslösung UVGZ-Codes mit zwei Stellen, inkl. Beschreibung der Versicherungslösung: 1. Stelle Personengruppe: Die Auswahl von A - Z oder 0 - 9 ist möglich 2. Stelle Kategorie: Die Auswahl von A - Z oder 0 - 9 ist möglich Beispiel der Beschreibung: 10 Personal, nicht versichert 11 Personal, UVG-Lohn A2 Personal, Überschuss-Lohn B1 Geschäftsleitung, UVG-Lohn

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob für die erste Stelle der UVGZ-Codes ausschliesslich Buchstaben (A-Z) und Ziffern (0-9) erfasst werden können. - Prüfen ob eine UVGZ-Versicherungslösung mit einer Beschreibung versehen werden kann.


#### S_ELM_V6_02_04_03 UVGZ-Grundlagen ⬜ offen

**Directive** 8.4.1

**Beschreibung:** UVGZ-Grundlagen UVG-Höchstlöhne Pro Versicherungslösung 10 = 0 11 = 0 - 148'200 12 = 148'200 - 300'000 Beitragsssätze müssen pro Versicherungslösung erfasst werden können: UVGZ-Beiträge Männer pro Versicherungslösung 10 = 0.0000% 11 = 0.2010% 12 = 0.6340% UVGZ-Beiträge Frauen pro Versicherungslösung 10 = 0.0000% 11 = 0.2010% 12 = 0.6340%

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Höchstlöhne im System hinterlegt sind. - Prüfen ob die Obergrenze (300'000) im System hinterlegt werden kann. - Prüfen ob die Beitragssätze pro Versicherungslösung erfasst werden können. - Prüfen ob die Beitragssätze mit 4 Nachkommastellen erfasst werden können.


### KTG (`02_05`)

#### S_ELM_V6_02_05_01 Angaben zur KTG-Versicherung ⬜ offen

**Directive** 8.5.2

**Beschreibung:** Angaben zur KTG-Versicherung Folgende Angaben zur Krankentaggeldversicherung müssen im ERP-System erfasst werden können: - Name des KTG-Versicherers - Versicherernummer - Kundennummer - Vertragsnummer

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Begriffsdefinitionen im ERP-System prüfen. Die Elemente "Versicherungsname", "Versicherernummer", "Kundennummer" und "Vertragsnummer" müssen im ERP-System vorhanden sein und identisch benannt werden.


#### S_ELM_V6_02_05_02 Angaben zur KTG-Versicherungslösung ⬜ offen

**Directive** 8.5.1

**Beschreibung:** Angaben zur KTG-Versicherungslösung KTG-Codes mit zwei Stellen, inkl. Beschreibung der Versicherungslösung: 1. Stelle Personengruppe: Die Auswahl von A - Z oder 0 - 9 ist möglich 2. Stelle Kategorie: Die Auswahl von A - Z oder 0 - 9 ist möglich Beispiel der Beschreibung: 10 Personal, nicht versichert 11 Personal, KTG-Lohn A2 Mutterschaft überobligatorisch, KTG-Lohn B1 Geschäftsleitung, KTG-Lohn

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob für die erste Stelle der KTG-Codes ausschliesslich Buchstaben (A-Z) und Ziffern (0-9) erfasst werden können. - Prüfen ob eine KTG-Versicherungslösung mit einer Beschreibung versehen werden kann.


#### S_ELM_V6_02_05_03 KTG-Grundlagen ⬜ offen

**Directive** 8.5.1

**Beschreibung:** KTG-Grundlagen Für die KTG-Versicherung können neben den UVG-Höchstlöhnen, auch vertraglich definierte Höchstlöhne verwendet werden. Beispiel anhand der UVG-Höchstlöhne: Pro Versicherungslösung 10 = 0 11 = 0 - 148'200 12 = 148'200 - 300'000 Beispiel mit vertraglich definierten Höchstlöhnen: A0 = 0 A1 = 0 - 300'000 B1 = 0 - 400'000 Beitragsssätze müssen pro Versicherungslösung erfasst werden können: KTG-Beiträge Männer pro Versicherungslösung 10 = 0.0000% 11 = 0.2010% 12 = 0.6340% KTG-Beiträge Fr…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Höchstlöhne im System hinterlegt werden können. - Prüfen ob zwischen UVG-Höchstlöhnen und vertraglich definierten Höchstlöhnen unterschieden werden kann. - Prüfen ob die Beitragssätze pro Versicherungslösung erfasst werden können. - Prüfen ob die Beitragssätze mit 4 Nachkommastellen erfasst werden können.


### BVG (`02_06`)

#### S_ELM_V6_02_06_01 Angaben zur BVG-Versicherung ⬜ offen

**Directive** 8.6.3

**Beschreibung:** Angaben zur BVG-Versicherung Folgende Angaben zur BVG-Versicherung müssen im ERP-System erfasst werden können: - Versicherernummer - BVG Kundennummer - BVG Vertragsnummer Es müssen im ERP-System mehrere BVG-Versicherer erfasst werden können.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Begriffsdefinitionen im ERP-System prüfen. Die Elemente "Versicherernummer", "Kundennummer" und "Vertragsnummer" müssen im ERP-System vorhanden sein und identisch benannt werden. - Prüfen ob im ERP-System mehrere BVG-Versicherer hinterlegt werden können.


#### S_ELM_V6_02_06_02 Angaben zum BVG-Versicherer ⬜ offen

**Directive** 8.1.3

**Beschreibung:** Angaben zum BVG-Versicherer Folgende Angaben zum BVG-Versicherer müssen im ERP-System erfasst werden können: - Name des BVG-Versicherers - UID-BFS des BVG-Versicherers Eingabe der UID-BFS oder Auswahl "unbekannt" - Gültig-ab Datum ab wann der BVG-Versicherer zuständig ist

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - UID-BFS: Prüfen ob "unbekannt" ausgewählt werden kann und anhand der UID-BFS "CHE-999.999.997" prüfen, ob eine Fehlermeldung bei einer invaliden Nummer angezeigt wird. - Prüfen ob die zwei Elemente "Name" und "Gültig-ab-Datum" erfasst werden können.


#### S_ELM_V6_02_06_03 Angaben zur BVG-Versicherungslösung ⬜ offen

**Directive** 8.6.2

**Beschreibung:** Angaben zur BVG-Versicherungslösung BVG-Codes mit bis zu 32 Stellen, inkl. Beschreibung der Versicherungslösung: Beispiel: - 1101 Basisplan - 2020 Kaderplan Ein ERP-System muss pro Vertrag mit einer Vorsorgeeinrichtung - also pro Versicherungslösung - eine eigene Lohnartensteuerung unterstützen. Es kann beispielsweise sein, das die Lohnart "Bonus" beim 1101 Basisplan berücksichtigt wird und beim 2020 Kaderplan nicht berücksichtigt wird. Diesem Spezialfall muss im ERP-System Rechnung getragen wer…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob BVG-Codes mit maximal 32 Stellen erfasst werden können. - Prüfen ob eine BVG-Versicherungslösung mit einer Beschreibung versehen werden kann. - Prüfen ob im ERP-System pro BVG-Versicherungslösung eine eigene Lohnartensteuerung unterstützt wird.


### BFS / Statistik (`02_07`)

#### S_ELM_V6_02_07_01 Angaben zum Bundesamt für Statistik (BFS) ⬜ offen

**Directive** 12.2

**Beschreibung:** Angaben zum Bundesamt für Statistik (BFS) Da es im Falle vom BFS nur einen Empfänger gibt, muss die Adressierung nicht separat in einer ERP-Lösung ausgewiesen werden. Wir empfehlen trotzdem, die Angaben zum BFS am gleichen Ort wie die Angaben sämtlicher weiterer Empfänger (Versicherer und Kantone) aufzulisten. Der Grund liegt vor allem in der Anwendung vom Buchungskreis für die Domäne Statistik. Analog den Steuerdomänen muss in einem ERP-System eine Übermittlung ans BFS nicht auf mehrere Buchung…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob ein Buchungskreis strukturiert auf Ebene vom Unternehmen erfasst werden kann.


#### S_ELM_V6_02_07_02 Unternehmensübergreifende Angaben zur Lohnvereinbarung (Auswahl) ⬜ offen

**Directive** 12.2.1

**Beschreibung:** Unternehmensübergreifende Angaben zur Lohnvereinbarung (Auswahl) Auf Ebene der Unternehmung muss die für das Unternehmen gültige Lohnvereinbarung ausgewählt werden können.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: Die Auswahl der gültigen Lohnvereinbarung muss folgende Elemente beinhalten: - Gesamtarbeitsvertrag eines Verbands - Gesamtarbeitsvertrag einer Firma oder einer öffentlichen Verwaltung - Kollektive Lohnvereinbarung ausserhalb eines Gesamtarbeitsvertrags - Einzelarbeitsvertrag


### Quellensteuer-Empfänger (`02_08`)

#### S_ELM_V6_02_08_01 Angaben zur Quellensteuer (pro Kanton) ⬜ offen

**Directive** 10.2

**Beschreibung:** Angaben zur Quellensteuer (pro Kanton) Pro Kanton müssen folgende Angaben erfasst werden können: - Kanton - SSL-Nummer - Buchungskreis

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Es muss ein neuer Kanton auf Basis einer strukturierten Liste hinzugefügt werden können - Pro Kanton muss eine SSL-Nummer hinterlegt werden können - Pro Kanton muss ein Buchungskreis hinterlegt werden können. Der Buchungskreis kann nur einmal auf einem Kanton hinterlegt werden. Eine Unterteilung innerhalb von einem Kanton auf mehrere Buchungskreise darf nicht vorgenommen werden. Siehe Hintergrundinformationen zu detaillierteren Ausführungen.


#### S_ELM_V6_02_08_02 QST-Tarifcodes ⬜ offen

**Directive** 10.5

**Beschreibung:** QST-Tarifcodes Die QST-Tarifcodes müssen mittels der kantonalen Tariftabellen in ein ERP-System importiert werden können. Der Import der Tariftabellen kann durch den ERP-Hersteller selbst oder auch durch manuelle Eingriff vom Anwender vorgenommen werden.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Sicherstellen, dass die QST-Tarifcodes anhand der von der ESTV publizierten Tariftabellen eingelesen werden.


## S_ELM_V6_03 — Personendaten

### Person / Stammdaten (`03_01`)

#### S_ELM_V6_03_01_01 Angaben zur Person ⬜ offen

**Directive** 7.1

**Beschreibung:** Angaben zur Person Im ERP-System gilt es sicherzustellen, dass eine Person nur einmal geführt werden kann. Es wird empfohlen Validierungen anhand der Personendaten im ERP-System einzubauen und damit diese Vorgabe sicherzustellen, bevor eine allfällige Übermittlung ausgelöst wird. Wie immer, gibt es auch hier Spezialfälle, die dieser Regel widersprechen. In diesen Fällen ist es das Ziel von Swissdec, dem Sachbearbeiter eine Warnung anzuzeigen und die Möglichkeit zu geben, die Warnung zu übersteue…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob eine Person mit identischen Persondendaten mehrfach im ERP-System erfasst werden kann. Wenn nein, gilt der Test als bestanden. - Falls ja, gilt es zu prüfen, ob der Anwender zum Zeitpunkt der Übermittlung auf die Anforderung der Eindeutigkeit einer Person im ERP-System hingewiesen wird.


#### S_ELM_V6_03_01_02 AHV-Nummer ⬜ offen

**Directive** 7.2

**Beschreibung:** AHV-Nummer Für jede erfasste Person muss eine AHV-Nummer hinterlegt werden können. Wenn die AHV-Nummer nicht bekannt ist muss "unbekannt" ausgewählt werden können.

**Erwartet:** Folgende AHV-Nummer muss erfasst werden: 756.7865.5342.46 - Prüfen ob "unbekannt" ausgewählt werden kann. - Prüfen ob invalide AHV-Nummern gespeichert werden können (Bsp: 756.7865.4342.46)


#### S_ELM_V6_03_01_03 Personalnummer ⬜ offen

**Directive** 7.2

**Beschreibung:** Personalnummer Für jede erfasste Person muss eine Personalnummer vergeben werden. Diese Nummer muss im ERP-System innerhalb vom Unternehmen eindeutig sein.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Die Personalnummer muss vom System automatisiert vergeben werden - Falls eine Person das Unternehmen verlässt und zu einem späteren Zeitpunkt wieder in das Unternehmen eintritt, muss die gleiche Personalnummer verwendet werden.


#### S_ELM_V6_03_01_04 Name, Vorname und Geschlechtscode ⬜ offen

**Directive** 7.2

**Beschreibung:** Name, Vorname und Geschlechtscode Für jede erfasste Person muss ein Name, Vorname und Geschlechtscode gepflegt werden können.

**Erwartet:** Folgende Kombination aus Vor- , Nachname und Geschlechtscode muss erfasst werden: Felix Müller, M - Prüfen ob die Geschlechtscodes M und F erfasst werden können


#### S_ELM_V6_03_01_05 Geburtsdatum ⬜ offen

**Directive** 7.2

**Beschreibung:** Geburtsdatum Das vollständige Geburtsdatum muss erfasst werden können. (DD.MM.YYYY)

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob ein Geburtsdatum grösser als aktuelles Tagesdatum erfasst werden kann (darf nicht möglich sein)


#### S_ELM_V6_03_01_06 Staatsangehörigkeit ⬜ offen

**Directive** 7.2

**Beschreibung:** Staatsangehörigkeit Für jede Person muss die dazugehörige Staatsangehörigkeit ausgewählt werden können. Für die Übermittlung müssen die Alpha-2 Codes von ISO 3166 verwendet werden. Folgende Auswahl muss zudem möglich sein: 11 = unbekannt 22 = staatenlos

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die ISO 3166 Liste in der ERP-Lösung hinterlegt wurde. - Prüfen ob 11 (unbekannt) und 12 (staatenlos) ausgewählt werden kann


#### S_ELM_V6_03_01_07 Sprachcode ⬜ offen

**Directive** 7.2

**Beschreibung:** Sprachcode Für jede Person muss die Sprache hinterlegt werden, die ein Arbeitgeber für die Kommunikation mit dem Arbeitnehmer verwendet.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die vier möglichen Sprachcodes hinterlegt werden können: DE, FR, IT, EN


#### S_ELM_V6_03_01_08 Aufenthaltskategorie ⬜ offen

**Directive** 7.2

**Beschreibung:** Aufenthaltskategorie Für jede Person muss eine Aufenthaltskategorie hinterlegt werden können. Für qsP ist die Erfassung von einer Aufenthaltskategorie obligatorisch. Schweizern darf keine Aufenthaltskategorie zugeordnet werden können.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: Prüfen ob die möglichen Kategorien ausgewählt werden können: - Kurzaufenthalter (L) - Jahresaufenthalter (B) - Niedergelassene (C) - Aufenthaltsbewilligung mit Erwerbstätigkeit (Ci) - Grenzgänger (G) - Asylsuchender (N) - Schutzbedürftige (S) - Vorläufig Aufgenommene (F) - Meldeverfahren für kurzfristige Erwerbstätigkeit (90 Tage) - Meldeverfahren für kurzfristige Erwerbstätigkeit (120 Tage) - Andere (nicht Schweizer) Sicherstellen, dass einer P…


#### S_ELM_V6_03_01_09 Zivilstand mit Gültig-ab-Datum ⬜ offen

**Directive** 7.2

**Beschreibung:** Zivilstand mit Gültig-ab-Datum Für jede Person muss ein Zivilstand mit einem gültig-ab-Datum erfasst werden. Das Gültig-ab-Datum ist essentiell für die korrekte Interpretation der übermittelten Daten.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: Prüfen ob die möglichen Zivilstände im ERP-System ausgewählt werden können und ob ein Gültig-ab-Datum hinterlegt werden kann: - ledig - verheiratet - geschieden - verwittwet - getrennt - eingetragene Partnerschaft - gerichtlich aufgelöste Partnerschaft - durch Tod aufgelöste Partnerschaft - durch Verschollenerklärung aufgelöste Partnerschaft - unbekannt


#### S_ELM_V6_03_01_10 Angaben zu den Kindern ⬜ offen

**Directive** 10.3.4

**Beschreibung:** Angaben zu den Kindern Für jede Person müssen die Angaben zu den Kindern erfasst werden können. Die Kinder in der ERP-Lösung werden für die Quellensteuer-Übermittlung, wie auch die Ermittlung der Zulagen auf Seiten der Familienausgleichskassen verwendet. Quellensteuer: Für die Übermittlung der Quellensteuer-Monatsmeldung sind Kinder obligatorisch zu erfassen. Es wird empfohlen, die Validierung im ERP-System an den QST-Code zu koppeln. (x1y = 1 Kind, x2y = 2 Kinder, ...).

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: Prüfen ob Kinder mit folgenden Angaben in der ERP-Lösung erfasst werden können: - Vorname des Kindes - Name des Kindes - Geburtsdatum des Kindes - Beginn des Anspruchs auf Kinderabzug - Ende des Anspruchs auf Kinderabzug Prüfen ob in der ERP-Lösung eine Fehlermeldung erscheint, wenn auf einem Kind ein Geburtsdatum erfasst wird, welches grösser ist als das aktuelle Tagesdatum. Prüfen ob die Anzahl erfasster Kinder mit dem QST-Tarifcode validiert …


#### S_ELM_V6_03_01_11 Adresse der Mitarbeitenden ⬜ offen

**Directive** 7.3

**Beschreibung:** Adresse der Mitarbeitenden Für jede Person muss im ERP-System die Wohnadresse erfasst werden können und historisiert werden. Die verwendeten Adressen müssen auch bei einem Umzug im Zugriff bleiben. Im Falle von einem Umzug wird die historisierte Adresse mit einem Wegzugsdatum versehen. Die Postleitzahl, der Ort und die dazugehörige Gemeindenummer müssen im System mittels der von der ESTV publizierten Gemeindeliste plausibilisiert werden. Zusätzlich muss die von der ESTV publizierte Liste der Ita…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: Prüfen ob Wohnadressen für Mitarbeitende mit folgenden Angaben erfasst werden können: - Strasse und Hausnummer - Postfach - Zusätzliche Informaton zur Lokalität - Postleitzahl - Ort - Land (Englisch in Grossbuchstaben bsp. SWITZERLAND) - Zweistelliger ISO-Ländercode, falls Adresse im Ausland - Wohnkanton (zwingende Auswahl CH-Kanton oder EX) - Gemeindenummer - Wegzugsdatum Prüfen ob im Falle von einer Adresse im Ausland der zweistelliger ISO-Län…


#### S_ELM_V6_03_01_12 E-Mail und Telefonnummer des Mitarbeitenden ⬜ offen

**Directive** 7.2

**Beschreibung:** E-Mail und Telefonnummer des Mitarbeitenden Für jede Person muss im ERP-System die E-Mail Adresse, wie auch die Telefonnummern erfasst werden können.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: Prüfen ob folgende Angaben auf einem Mitarbeitenden erfasst werden können: - E-Mail - Telefonnummer - Mobilnummer


#### S_ELM_V6_03_01_13 Arbeitsort ⬜ offen

**Directive** 4.2.2

**Beschreibung:** Arbeitsort Im ERP-System muss jeder Person einer der im Unternehmen hinterlegten Arbeitsorte zugewiesen werden.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob einer Person ein Arbeitsort zugewiesen werden kann. - Sicherstellen, dass mit dieser Auswahl auch die zuständige Familienausgleichskasse zugeordnet wird, die auf dem Arbeitsort hinterlegt ist.


### Arbeitsverträge (`03_02`)

#### S_ELM_V6_03_02_01 Arbeitsverträge ⬜ offen

**Directive** 4.2.3

**Beschreibung:** Arbeitsverträge Im ERP-System muss für jede Person mindestens ein Arbeitsvertrag erfasst werden können. Spezialfall Mehrfachbeschäftigung: Im Falle von mehreren gleichzeitigen Arbeitsverträgen beim gleichen Arbeitgeber (Mehrfachbeschäftigung) können auch mehrere Verträge auf einer Person angelegt werden. Dieser Spezialfall ist kein obligatorischer Teil der Zertifizierung von Swissdec und wird nur bei den ERP-Herstellern geprüft, die diese Funktion unterstützen. Auf dem Vertrag werden die folgend…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: Prüfen ob im ERP-System ein Vertrag erfasst werden kann und ob folgende Angaben auf dem Vertrag hinterlegt werden können: - Wahl des Arbeitszeitmodells aus der Liste der im Unternehmen erfassten Arbeitszeitmodelle - Arbeitsverhältnis (regelmässig/unregelmässig) inkl. Folgeangaben - Eintritts- und Austrittsdatum - Im Unternehmen ausgeübter Beruf - Vertraglicher 13. Monatslohn - Vertraglicher weiterer Monatslohn Prüfen ob die Art der Lohnzahlung a…


#### S_ELM_V6_03_02_02 Arbeitsverträge - Wahl des Arbeitszeitmodells ⬜ offen

**Directive** 7.4

**Beschreibung:** Arbeitsverträge - Wahl des Arbeitszeitmodells Jeder Person muss ein unternehmensübergreifend definiertes Arbeitszeitmodell zugeordnet werden können. Es muss eine Funktion geschaffen werden um ein abweichendes Arbeitszeitmodell für eine Person erfassen zu können.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene vom Vertrag bei einer Person ein Arbeitszeitmodell aus der Liste der unternehmensübergreifend hinterlegten Arbeitszeitmodelle ausgewählt werden kann. - Prüfen ob ein vom unternehmensübergreifenden Modell abweichendes Arbeitszeitmodell hinterlegt werden kann. - Prüfen ob das Format der Wochenstunden/Wochenlektionen eingehalten wird. In der Übermittlung müssen die Werte mit Format x.xx, also mit zwei Nachkommastellen abgebild…


#### S_ELM_V6_03_02_03 Arbeitsverträge - Arbeitsverhältnis (regelmässig/unregelmässig) inkl. ⬜ offen

**Directive** 7.4.1

**Beschreibung:** Arbeitsverträge - Arbeitsverhältnis (regelmässig/unregelmässig) inkl. Folgeangaben Für jede Person muss im ERP-System hinterlegt werden, ob die Person regelmässig oder unregelmässig angestellt ist. Sobald eine Person regelmässig angestellt ist, muss der Beschäftigungsgrad erfasst werden. Falls eine Person unregelmässig beschäftigt ist, muss kein Beschäftsigungsgrad erfasst werden.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Information "Regelmässig/Unregelmässig" im ERP-System auf Ebene vom Vertrag erfasst werden kann. - Prüfen ob ein Beschäftigungsgrad bei regelmässig angestellten Personen hinterlegt werden kann. - Sicherstellen, dass anhand vom Beschäftigungsgrad die für den Vertrag zu übermittelnde Arbeitszeit abgeleitet wird. Testablauf: 1. Arbeitszeitmodell mit 40 Stunden betriebsübliche Arbeitszeit im Unternehmen erfassen 2. Das erstellte Arbe…


#### S_ELM_V6_03_02_04 Arbeitsverträge - Ein- und Austrittsdaten ⬜ offen

**Directive** 7.4.2

**Beschreibung:** Arbeitsverträge - Ein- und Austrittsdaten Ein Vertrag muss im ERP-System immer mit einem Eintrittsdatum und im Falle von einem Austritt zusätzlich mit einem Austrittsdatum versehen werden. Im Falle von einem Wiedereintritt ist es essentiell, dass ein neuer Vertrag auf der gleichen Person erstellt oder ein vergangener Vertrag wieder mit einem neuen Eintritt versehen wird. Die Person darf nicht zwei Mal im gleichen ERP-System geführt werden.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob ein Eintritt / Austritt erfasst werden kann - Prüfen ob sichergestellt ist, dass ein Austritt nicht vor einem Eintritt erfasst werden kann - Prüfen ob ein Wiedereintritt erfasst werden kann, ohne dass eine Person erneut erfasst werden muss.


#### S_ELM_V6_03_02_05 Arbeitsverträge - Im Unternehmen ausgeübter Beruf ⬜ offen

**Directive** 12.3.3

**Beschreibung:** Arbeitsverträge - Im Unternehmen ausgeübter Beruf Für jede Person muss im ERP-System auf dem Vertrag der im Unternehmen ausgeübte Beruf hinterlegt werden.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob ein im Unternehmen ausgeübter Beruf im ERP-System als Freitext erfasst werden kann. Es ist wichtig, dass dieses Element direkt auf einem Vertrag erfasst wird.


#### S_ELM_V6_03_02_06 Arbeitsverträge - Vertraglicher 13. Monatslohn ⬜ offen

**Directive** 12.5

**Beschreibung:** Arbeitsverträge - Vertraglicher 13. Monatslohn Im ERP-System muss für jeden Vertrag hinterlegt werden können, ob die Person einen 13. Monatslohn oder weitere Monatslöhne (14., 15.,...) ausbezahlt erhält. Dabei handelt es sich um die Information auf Ebene vom Vertrag ob eine Person einen 13., 14, ... Monatslohn ausbezahlt erhält und nicht um den effektiven Betrag. Diese Information ist auch unabhängig von der effektiven Auszahlung zu betrachten.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene vom Vertrag bei einer Person ein 13. Monatslohn wie auch weitere Monatslöhne hinterlegt werden können.


#### S_ELM_V6_03_02_07 Arbeitsverträge - Erkennung der Art der Lohnzahlung ⬜ offen

**Directive** 12.5

**Beschreibung:** Arbeitsverträge - Erkennung der Art der Lohnzahlung Das ERP-System muss anhand der auf dem Vertrag hinterlegten Informationen zur Art der Lohnzahlung (TF51 - Beispielsweise "Befristeter Vertrag mit Monatslohn") erkennen, um welche der folgenden Arten der Lohnzahlung es sich handelt: - Lohnzahlung pro Monat - Lohnzahlung pro Stunde/Lektion - Lohnzahlung ohne Zeitbindung Die Arten der Lohnzahlung sind via folgender Tabelle automatisch zu erkennen: Lohnzahlung pro Monat: - Unbefristeter Vertrag mit…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Arten der Lohnzahlung korrekt zugeteilt werden und die zusätzlichen Angaben erfasst werden können.


### AHV/ALV Sonderfälle (`03_03`)

#### S_ELM_V6_03_03_01 AHV/ALV Sonderfall ⬜ offen

**Directive** 8.1.4

**Beschreibung:** AHV/ALV Sonderfall Im ERP-System muss eine Person als AHV/ALV Sonderfall markiert werden können. Diese Funktion muss die vom Lohnsystem ermittelte Beitragspflicht übersteuern und die betroffene Person von der AHV/ALV Beitragspflicht befreien.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person im ERP-System die Information AHV/ALV Sonderfall hinterlegt werden kann. - Prüfen ob eine als AHV/ALV Sonderfall markierte Person von der AHV/ALV-Beitragspflicht ausgenommen wird.


#### S_ELM_V6_03_03_02 Verzicht auf den Freibetrag ⬜ offen

**Directive** 8.1.1

**Beschreibung:** Verzicht auf den Freibetrag Im ERP-System muss für eine Person im Rentenalter hinterlegt werden können, dass auf den Freibetrag verzichtet wird. Diese Funktion muss den vom Lohnsystem ermittelten AHV-Code (Rentenbezüger) übersteuern und die Person trotz Rentenalter als "AHV-pflichtig (aktiv)" behandeln.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person im ERP-System die Information "Verzichtet auf den Freibetrag" hinterlegt werden kann. - Sicherstellen, dass die Funktion nur dann ausgewählt werden kann, wenn die betroffene Person im Rentenalter ist.


#### S_ELM_V6_03_03_03 Verwandtschaftsgrad zum Inhaber (Landwirtschaft) ⬜ offen

**Directive** 8.1.2

**Beschreibung:** Verwandtschaftsgrad zum Inhaber (Landwirtschaft) In für Landwirtschaftsbetriebe spezialisierten ERP-Lösungen muss auf Ebene der Person der Verwandtschaftsgrad zum Inhaber hinterlegt werden können. Folgende Auswahlmöglichkeiten bestehen: - Unbekannt - Nicht verwandt - Ehefrau des Inhabers - Ehemann der Inhaberin - Blutsverwandt mit Inhaber - Geschwister des Inhabers - Pflegekind des Inhabers

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene vom Unternehmen hinterlegt werden kann, dass es sich um einen Landwirtschaftsbetrieb handelt. - Prüfen ob für Landwirtschaftsbetriebe auf Ebene der Person eine der beschriebenen Auswahlmöglichkeiten hinterlegt werden kann.


### Versicherungszuweisung MA (`03_04`)

#### S_ELM_V6_03_04_01 Zuweisung UVG-Versicherungsdaten ⬜ offen

**Directive** 8.3.2

**Beschreibung:** Zuweisung UVG-Versicherungsdaten Im ERP-System muss einer Person eine UVG-Versicherung, wie auch ein UVG-Code zugewiesen werden können.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob einer Person eine UVG-Versicherung zugewiesen werden kann. - Prüfen ob einer Person ein UVG-Code zugewiesen werden kann. - Prüfen ob die erste Stelle auf den auf Stufe Unternehmen hinterlegten Betriebsteilen basiert. - Prüfen ob die zweite Stelle auf die Ziffern 0, 1, 2, 3 beschränkt ist. Die Ziffern 4-9 dürfen nicht erfasst werden können. - Prüfen ob bei fehlendem UVG-Code eine Fehlermeldung angezeigt wird.


#### S_ELM_V6_03_04_02 Zuweisung UVGZ-Versicherungsdaten ⬜ offen

**Directive** 8.4.1

**Beschreibung:** Zuweisung UVGZ-Versicherungsdaten Im ERP-System müssen einer Person mindestens zwei UVGZ-Versicherungslösungen paralell via UVGZ-Codes zugeordnet werden können. Wir empfehlen das ERP-System robust zu bauen und auch mehr als zwei paralelle UVGZ-Codes pro Person zuzulassen. Beispiel: - 11 Personal - A2 Personal, Überschuss-Lohn

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob einer Person eine UVGZ-Versicherungslösung via UVGZ-Code zugewiesen werden kann. - Prüfen ob einer Person mehrere paralelle UVGZ-Versicherungslösungen via UVGZ-Codes zugeordnet werden können.


#### S_ELM_V6_03_04_03 Zuweisung KTG-Versicherungsdaten ⬜ offen

**Directive** 8.5.1

**Beschreibung:** Zuweisung KTG-Versicherungsdaten Im ERP-System müssen einer Person mindestens zwei KTG-Versicherungslösungen paralell via KTG-Codes zugeordnet werden können. Wir empfehlen das ERP-System robust zu bauen und auch mehr als zwei paralelle KTG-Codes pro Person zuzulassen. Beispiel: - A1 Personal, KTG-Lohn - A2 Mutterschaft überobligatorisch, KTG-Lohn

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob einer Person eine KTG-Versicherungslösung via KTG-Code zugewiesen werden kann. - Prüfen ob einer Person mehrere paralelle KTG-Versicherungslösungen via KTG-Codes zugeordnet werden können.


#### S_ELM_V6_03_04_04 Zuweisung BVG-Versicherungsdaten ⬜ offen

**Directive** 8.6.2

**Beschreibung:** Zuweisung BVG-Versicherungsdaten Im ERP-System müssen einer Person mindestens zwei BVG-Versicherungslösungen paralell via BVG-Codes zugeordnet werden können. Wir empfehlen das ERP-System robust zu bauen und auch mehr als zwei paralelle BVG-Codes pro Person zuzulassen. Beispiel: - 1101 Basisplan - 2020 Kaderplan

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob einer Person eine BVG-Versicherungslösung via BVG-Code zugewiesen werden kann. - Prüfen ob einer Person mehrere paralelle BVG-Versicherungslösungen via BVG-Codes zugeordnet werden können.


### Lohnausweis-Angaben (`03_05`)

#### S_ELM_V6_03_05_01 Lohn oder Rente ⬜ offen

**Directive** 9.1.1

**Beschreibung:** Lohn oder Rente Im ERP-System muss unterschieden werden können, ob eine Person einen Lohnausweis oder eine Rentenbescheinigung erhält.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person zwischen Lohnausweis und Rentenbescheinigung unterschieden werden kann.


#### S_ELM_V6_03_05_02 Ziffer F - Unentgeltliche Beförderung zwischen Wohn- und Arbeitsort ⬜ offen

**Directive** 9.2.3

**Beschreibung:** Ziffer F - Unentgeltliche Beförderung zwischen Wohn- und Arbeitsort Im ERP-System muss erfasst werden können ob die Ziffer F im Lohnausweis für eine Person gilt.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person die Ziffer F ausgewählt werden kann.


#### S_ELM_V6_03_05_03 Ziffer G: Kantinenverpflegung / Lunch-Checks ⬜ offen

**Directive** 9.2.3

**Beschreibung:** Ziffer G: Kantinenverpflegung / Lunch-Checks Im ERP-System muss erfasst werden können ob die Ziffer G im Lohnausweis für eine Person gilt.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person die Ziffer G ausgewählt werden kann.


#### S_ELM_V6_03_05_04 Ziffer 14: Weitere nicht bewertete Gehaltsnebenleistungen ⬜ offen

**Directive** 9.2.3

**Beschreibung:** Ziffer 14: Weitere nicht bewertete Gehaltsnebenleistungen Im ERP-System muss eine Eingabemöglichkeit für Freitext im Falle von weiteren Gehaltsnebenleistungen, die vom Arbeitgeber nicht selbst bewertet werden können, integriert werden. Beispiel: - Stark verbilligte Abgabe eines Personenwagens bei Ziffer 14

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person ein Text für weitere nicht bewertete Gehaltsnebenleistungen erfasst werden kann. - Prüfen ob die Angabe zu weiteren nicht bewerteten Gehaltsnebenleistungen auch für das ganze Unternehmen hinterlegt werden kann.


#### S_ELM_V6_03_05_05 Standardbemerkungen - ExpatriateRuling genehmigt durch die ⬜ offen

**Directive** 9.2.3.1.1

**Beschreibung:** Standardbemerkungen - ExpatriateRuling genehmigt durch die Steuerverwaltung Im ERP-System muss ein durch die Steuerverwaltung genehmigtes ExpatriateRuling erfasst werden können. Es muss immer ein Datum und ein Kanton hinterlegt werden können. Beispiel: - Kanton LU - Datum 15.04.2021 Auf dem Lohnausweis muss anhand dieser Angaben die folgende Standardbemerkung in Ziffer 15 dargestellt werden. Falls der Viewgen verwendet wird, wird die Standardbemerkung automatisch anhand vom Element im XML abgefü…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person das durch die kantonale Steuerverwaltung genehmigte ExpatriateRuling samt Datum und Kanton hinterlegt werden kann. - Prüfen ob das durch die kantonale Steuerverwaltung genehmigte ExpatriateRuling samt Datum und Kanton auch für das gesamte Unternehmen hinterlegt werden kann. - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Lohnausweis abgedruckt wird. (Konsolidiert prüfen nachdem alle Standardbemerkun…


#### S_ELM_V6_03_05_06 Standardbemerkungen - Privatanteil Geschäftswagen ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Standardbemerkungen - Privatanteil Geschäftswagen Im ERP-System müssen zwei Standardbemerkungen zum Privatanteil Geschäftswagen ausgewählt werden können. Einerseits muss hinterlegt werden können, dass der Arbeitgeber beträchtliche Kosten vom Geschäftswagen übernimmt und damit der Privatanteil im Veranlagugnsverfahren abgeklärt werden muss. Andererseits muss hinterlegt werden können, dass der Arbeitnehmer einen minimalen Anteil (0.9% des Kaufpreises) übernimmt. Auf dem Lohnausweis muss anhand die…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person die Optionen "Arbeitnehmer übernimmt einen minimalen Anteil (0.9% des Kaufpreises am Geschäftswagen" und "Arbeitnehmer übernimmt beträchtliche Kosten vom Geschäftswagen" hinterlegt werden können. - Prüfen ob die zwei Möglichkeiten auch für das gesamte Unternehmen hinterlegt werden können. - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Lohnausweis abgedruckt wird. (Konsolidiert prüfen nachdem alle S…


#### S_ELM_V6_03_05_07 Spesenreglement - Spesenreglement genehmigt durch die Steuerverwaltung ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Spesenreglement - Spesenreglement genehmigt durch die Steuerverwaltung Im ERP-System muss ein durch die Steuerverwaltung genehmigtes Spesenreglement erfasst werden können. Es muss immer ein Datum und ein Kanton hinterlegt werden können. Beispiel: - Kanton LU - Datum 15.04.2021 Auf dem Lohnausweis muss anhand dieser Angaben die folgende Standardbemerkung in Ziffer 15 dargestellt werden. Falls der Viewgen verwendet wird, wird die Standardbemerkung automatisch anhand vom Element im XML abgefüllt: -…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person das durch die kantonale Steuerverwaltung genehmigte Spesenreglement samt Datum und Kanton hinterlegt werden kann. - Prüfen ob das durch die kantonale Steuerverwaltung genehmigte Spesenreglement samt Datum und Kanton auch für das gesamte Unternehmen hinterlegt werden kann.


#### S_ELM_V6_03_05_08 Spesenreglement - Ziffer 13.1.1 - RZ 52 ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Spesenreglement - Ziffer 13.1.1 - RZ 52 Im ERP-System muss hinterlegt werden können, dass die Bedingungen von RZ 52 der LA-Wegleitung eingehalten wurden. Mit dieser Angabe wird in Ziffer 13.1.1 im Lohnausweis ein Kreuz gesetzt.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person die Information, dass die Bedingungen von RZ52 eingehalten wurde, hinterlegt werden kann. - Prüfen ob die Angabe, dass die Bedingungen von RZ52 eingehalten wurde, auch für das ganze Unternehmen hinterlegt werden kann. - Falls kein Viewgen: Prüfen ob das Kreuz gemäss RZ52 auf dem Lohnausweis abgedruckt wird. (Konsolidiert prüfen nachdem alle Standardbemerkungen im ERP-System geprüft wurden.)


#### S_ELM_V6_03_05_09 Standardbemerkungen - Gesamtzahl der Lohnausweise ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Standardbemerkungen - Gesamtzahl der Lohnausweise Wenn im ERP-System für die selbe Person innerhalb eines Kalenderjahres mehrere Lohnausweise erstellt werden, ist die Anzahl der Lohnausweise in Ziffer 15 zu vermerken. Auf dem Lohnausweis muss anhand dieser Angaben die folgende Standardbemerkung in Ziffer 15 dargestellt werden. Falls der Viewgen verwendet wird, wird die Standardbemerkung automatisch anhand vom Element im XML abgefüllt: «Einer von … Lohnausweisen»

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob im Falle von mehreren Lohnausweisen für die selbe Person innerhalb von einem Kalenderjahr die Gesamtzahl der Lohnausweise automatisiert auf dem Lohnausweis ergänzt wird. - Prüfen das im Standardfall (nur 1 Lohnausweis) der Hinweis zur Gesamtzahl der Lohnausweise in Ziffer 15 nicht abgedruckt wird. - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Lohnausweis abgedruckt wird. (Konsolidiert prüfen nachdem alle Standardbemer…


#### S_ELM_V6_03_05_10 Standardbemerkungen - Mitarbeiterbeteiligungen Verkehrswert genehmigt ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Standardbemerkungen - Mitarbeiterbeteiligungen Verkehrswert genehmigt durch Steuerverwaltung Im ERP-System muss ein durch die Steuerverwaltung genehmigte Verkehrswert der Mitarbeiterbeteiligungen erfasst werden können. Es muss immer ein Datum und ein Kanton hinterlegt werden können. Beispiel: - Kanton LU - Datum 15.09.2010 Auf dem Lohnausweis muss anhand dieser Angaben die folgende Standardbemerkung in Ziffer 15 dargestellt werden. Falls der Viewgen verwendet wird, wird die Standardbemerkung aut…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person der durch die kantonale Steuerverwaltung genehmigte Verkehrswert der Mitarbeiterbeteiligungen samt Datum und Kanton hinterlegt werden kann. - Prüfen ob der durch die kantonale Steuerverwaltung genehmigte Verkehrswert der Mitarbeiterbeteiligungen samt Datum und Kanton auch für das gesamte Unternehmen hinterlegt werden kann. - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Lohnausweis abgedruckt wird. …


#### S_ELM_V6_03_05_11 Standardbemerkungen - Mitarbeiterbeteiligungen noch ohne steuerbare ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Standardbemerkungen - Mitarbeiterbeteiligungen noch ohne steuerbare Einkommen Im ERP-System muss hinterlegt werden können, dass es sich bei den gewährten Mitarbeiterbeteiligungen noch um kein steuerbares Einkommen handelt. Im ERP-System müssen bei Auswahl von dieser Option eine oder auch mehrere der folgenden Gründe ausgewählt werden können: - Gesperrte Mitarbeiteroptionen - Nicht börsenkotierte Mitarbeiteroptionen - Anwartschaften auf Mitarbeiteraktien - Unechte Mitarbeiterbeteiligungen Auf dem…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person die Standardbemerkung "Mitarbeiterbeteiligungen noch ohne steuerbare Einkommen" hinterlegt werden kann. - Prüfen ob eine oder auch mehrere Optionen der vier definierten Gründe ausgewählt werden können. - Prüfen ob die Standardbemerkung "Mitarbeiterbeteiligungen noch ohne steuerbare Einkommen" auch für das gesamte Unternehmen hinterlegt werden kann. - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Loh…


#### S_ELM_V6_03_05_12 Standardbemerkungen - Besoldungsnachgenuss ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Standardbemerkungen - Besoldungsnachgenuss Wird die Lohnfortzahlung im Todesfall an die Hinterbliebenen ausgerichtet, muess im ERP-System möglich sein, den Betrag des Besoldungsnachgenusses in Ziffer 4 und den Begünstigten in Ziffer 15 zu vermerken. Folgende Angaben müssen zur begünstigten Person erfasst werden können: - Name der begünstigten Person - Vorname der begünstigten Person - Adresse der begünstigten Person Auf dem Lohnausweis muss anhand dieser Angaben die folgende Standardbemerkung in…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person der Besoldungsnachgenuss ausgewählt werden und die begünstigte Person mit sämtlichen Angaben hinterlegt werden kann. - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Lohnausweis abgedruckt wird. (Konsolidiert prüfen nachdem alle Standardbemerkungen im ERP-System geprüft wurden.)


#### S_ELM_V6_03_05_13 Standardbemerkungen - Kinderzulagen direkt durch Aus-gleichskasse ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Standardbemerkungen - Kinderzulagen direkt durch Aus-gleichskasse ausbezahlt Im ERP-System muss der Fall, dass Kinderzulagen direkt von der Ausgleichskasse ausbezahlt werden, hinterlegt werden können. Auf dem Lohnausweis muss anhand dieser Angaben die folgende Standardbemerkung in Ziffer 15 dargestellt werden. Falls der Viewgen verwendet wird, wird die Standardbemerkung automatisch anhand vom Element im XML abgefüllt: «Kinderzulagen im LA nicht enthalten. Auszahlung durch Ausgleichskasse»

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person die Standardbemerkung "Kinderzulagen direkt durch Ausgleichskasse bezahlt" hinterlegt werden kann. - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Lohnausweis abgedruckt wird. (Konsolidiert prüfen nachdem alle Standardbemerkungen im ERP-System geprüft wurden.)


#### S_ELM_V6_03_05_14 Standardbemerkungen - Umzugskosten ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Standardbemerkungen - Umzugskosten Im ERP-System muss es möglich sein, den Fall das berufsbedingte Umzugskosten durch den Arbeitgeber beglichen wurden, im Lohnausweis mit dem entsprechenden Betrag (z.B. 4'251.00) zu hinterlegen. Auf dem Lohnausweis muss anhand dieser Angaben die folgende Standardbemerkung in Ziffer 15 dargestellt werden. Falls der Viewgen verwendet wird, wird die Standardbemerkung automatisch anhand vom Element im XML abgefüllt: «Umzugskosten von CHF ... bezahlt»

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person die Standardbemerkung "Berufsbedingte Umzugskosten, die der Arbeitgeber begleicht" mitsamt einem Betrag hinterlegt werden kann. - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Lohnausweis abgedruckt wird. (Konsolidiert prüfen nachdem alle Standardbemerkungen im ERP-System geprüft wurden.)


#### S_ELM_V6_03_05_15 Individuelle Kontaktpersonen ⬜ offen

**Directive** 9.2.3.3

**Beschreibung:** Individuelle Kontaktpersonen Im Lohnausweis muss eine individuelle Kontaktperson hinterlegt werden können, damit die zuständige kantonale Steuerverwaltung weiss, auf wen sie im Falle von Rückfragen zugehen können. Die Mindestanforderung liegt darin, im ERP-System eine individuelle Kontaktperson für Rückfragen zum Lohnausweis hinterlegt werden kann. Pro Arbeitnehmer kann aber auch eine separate Kontaktperson hinterlegt werden. Die Idee von dieser Funktion liegt darin zu ermöglichen, dass beispiel…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob unternehmensübergreifend eine Kontaktperson für den Lohnausweis mit den spezifizierten Angaben im ERP-System hinterlegt werden kann. - Falls kein Viewgen: Prüfen ob die Kontaktperson auf dem Lohnausweis abgedruckt wird.


#### S_ELM_V6_03_05_16 Standardbemerkungen - Abrechnungsbeleg für Quellensteuerpflichtige ⬜ offen

**Directive** 9.2.3.1.2

**Beschreibung:** Standardbemerkungen - Abrechnungsbeleg für Quellensteuerpflichtige Bei quellensteuerpflichtigen Arbeitnehmern muss im ERP-System automatisiert eine Standardbemerkung erstellt und in Ziffer 15 abgefüllt werden. Auf dem Lohnausweis muss anhand dieser Angaben die folgende Standardbemerkung in Ziffer 15 dargestellt werden. Falls der Viewgen verwendet wird, wird die Standardbemerkung automatisch anhand vom Element im XML abgefüllt: «Quellensteuerpflichtige Personen können schriftlich und begründet bi…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Standardbemerkung für Quellensteuerpflichtige automatisiert vom ERP-System im Lohnausweis abgefüllt wird. - Prüfen ob die Sprache den übrigen Texten auf dem Lohnausweis entspricht - Falls kein Viewgen: Prüfen ob die Standardbemerkung auf dem Lohnausweis abgedruckt wird. (Konsolidiert prüfen nachdem alle Standardbemerkungen im ERP-System geprüft wurden.)


### Statistik MA (`03_06`)

#### S_ELM_V6_03_06_01 Ausbildung des Arbeitnehmers ⬜ offen

**Directive** 12.3.1

**Beschreibung:** Ausbildung des Arbeitnehmers Im ERP-System muss für jeden Arbeitnehmer die höchste abgeschlossene Ausbildung aus folgender Liste hinterlegt werden können: - Doktorat, Habilitat - Uni, ETH, Master - Uni, ETH, Bachelor - Fachhochschule (FH), Pädagogische (PH), Master - Fachhochschule (FH), Pädagogische (PH), Bachelor - Höhere Berufsausbildung, höhere Fachschule - Höhere Berufsbildung Master - Höhere Berufsbildung Bachelor - Lehrpatent - Matura - Abgeschlossene Berufsausbildung - Ausschliesslich un…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob alle Ausbildungsarten ausgewählt werden können.


#### S_ELM_V6_03_06_02 Berufliche Stellung des Arbeitnehmers ⬜ offen

**Directive** 12.3.2

**Beschreibung:** Berufliche Stellung des Arbeitnehmers Im ERP-System muss für jeden Arbeitnehmer die berufliche Stellung hinterlegt werden können: - Oberes Kader - Mittleres Kader - Unteres Kader - Unterstes Kader - Ohne Kaderfunktion

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob alle Möglichkeiten für die berufliche Stellung ausgewählt werden können.


#### S_ELM_V6_03_06_03 Kennzeichnung von erweiterten Merkmalen auf Ebene vom Arbeitnehmer für ⬜ offen

**Directive** 12.3.5

**Beschreibung:** Kennzeichnung von erweiterten Merkmalen auf Ebene vom Arbeitnehmer für die Statistik Auf Ebene vom Arbeitnehmer müssen folgende Elemente hinterlegt werden können: - Kennzeichnung von verliehenem Personal - Kennzeichnung von Stammpersonal in öffentlichen Verwaltungen - Flexibles Feld für das Profiling

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob ein Arbeitnehmer als "verliehenes Personal" gekennzeichnet werden kann. - Prüfen ob ein Arbeitnehmer als "Stammpersonal in öffentlichen Verwaltungen" gekennzeichnet werden kann. - Prüfen ob auf dem Arbeitnehmer ein flexibles Feld für das Profiling verfügbar ist.


#### S_ELM_V6_03_06_04 Ferienanspruch ⬜ offen

**Directive** 12.3.4

**Beschreibung:** Ferienanspruch Im ERP-System muss für jeden Arbeitnehmer die Anzahl der nach Arbeitsvertrag gutgeschriebenen Ferientage pro Jahr hinterlegt werden können. Wie die Ferientage im System hinterlegt werden können, ist Sache vom ERP- Hersteller. Wichtig ist, dass daraus die von der Domäne Statistik geforderte Anzahl Ferientage pro Kalenderjahr abgeleitet werden können. Es geht nicht darum, die tatsächlichen Ferientage anzugeben, die auf der Grundlage des Beschäftigungsgrads und der Dauer der Beschäft…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Ferien manuell hinterlegt werden können oder ob eine automatisierte Ermittlung aufgrund von Umsystemen (bsp. Zeiterfassungssysteme) möglich ist. - Prüfen ob dem ERP-Hersteller die Verbindung zwischen der Anzahl Ferientage und den vertraglichen Ferienentschädigungen für die Domäne Statistik klar ist. Es ist essentiell, dass aus den ERP-Systemen keine Übermittlungen an das BFS ausgelöst werden, die sich in sich widersprechen. Eine …


### Quellensteuer MA (`03_07`)

#### S_ELM_V6_03_07_01 Konfession (Quellensteuer) ⬜ offen

**Directive** 10.3.1

**Beschreibung:** Konfession (Quellensteuer) Bei der erstmaligen Anmeldung einer qsP benötigt die KSTV die Angaben zur Konfession. Dies ist notwendig, um die Quellensteuer bei kirchensteuerpflichtigen Personen der entsprechenden Landeskirche zuzuteilen. Folgende Auswahlen müssen dafür ins ERP-System integriert werden: - Römisch-katholische Kirche - Christ-katholische Kirche - Evangelisch-reformierte Kirche - Israelitische Kultusgemeinschaft - Andere/Keine

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob auf Ebene der Person oder auch direkt bei der erstmaligen Übermittlung einer Person an die zuständige KSTV die Konfession ausgewählt werden kann und ob alle Möglichkeiten im ERP-System abgebildet sind.


#### S_ELM_V6_03_07_02 Weiterer Arbeitgeber oder Ersatzeinkünfte ⬜ offen

**Directive** 10.6.4.1

**Beschreibung:** Weiterer Arbeitgeber oder Ersatzeinkünfte Im Falle von weiteren Arbeitgebern oder Ersatzeinkünften muss im ERP-System der kumulierte Gesamtbeschäftigungsgrad bei allen weiteren Arbeitgebern hinterlegt werden können. Wenn für eine qsP kein Arbeitspensum ermittelt werden kann, das Gesamtbruttoeinkommen (QST-Lohn) nicht bekannt ist und der quellensteuerpflichtige Lohn (QST-Lohn) tiefer ist als der Medianwert vom zuständigen Kanton, kommt der Medianwert zur Anwendung. Damit dies erkannt werden kann,…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob im Falle von weiteren Arbeitgebern oder Ersatzeinkünften die erforderlichen drei Angaben im ERP- System hinterlegt werden können. - Prüfen ob bei Auswahl "Kumulierter Gesamtbeschäftigungsgrad (in%) bei allen weiteren Arbeitgebern bekannt" der kumulierte Gesamtbeschäftigungsgrad (in %) bei allen weiteren Arbeitgebern auf Ebene der Person für die Quellensteuer hinterlegt werden kann. - Prüfen ob bei Auswahl "Gesamtbruttoeinkommen (QST-…


#### S_ELM_V6_03_07_03 Halbfamilie ⬜ offen

**Directive** 10.3.2

**Beschreibung:** Halbfamilie Bei Halbfamilien handelt es sich um Alleinstehende, welche mit Kindern oder unterhaltspflichtigen Personen im gleichen Haushalt zusammenleben, für deren Unterhalt sie zur Hauptsache aufkommen. In der Quellensteuer haben diese qsP einen der Tarifcodes H, P oder U. Für qsP mit einem der drei Codes, muss im ERP-System die Angabe zur familiären Situtation ausgewählt werden können. Folgende Angaben müssen ausgewählt werden können: - Kein Konkubinat - Konkubinat - Alleiniges Sorgerecht - G…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Angaben zur familiären Situation (Konkubinat/Kein Konkubinat) und die Folgeangaben im ERP- System auf einer qsP hinterlegt werden können, die den Tarifcode H, P oder U zugewiesen hat.


#### S_ELM_V6_03_07_04 Bestimmung von QST-Kanton und Gemeinde ⬜ offen

**Directive** 10.4

**Beschreibung:** Bestimmung von QST-Kanton und Gemeinde Das ERP-System muss anhand der erfassten Adressdaten bestimmen können, wo die qsP Quellensteuer entrichten muss. Folgende Regeln sind in ein ERP-System zu integrieren. In den Klammern ist immer die Adresse hinterlegt, die für die Bestimmung vom QST-Kanton verwendet werden muss: - Wohnsitz in der Schweiz (Adresse vom Wohnsitz) - Wohnsitz im Ausland -- Tagesaufenthalter mit täglicher Rückkehr an den Wohnort im Ausland (Adresse vom Arbeitsort) -- Wochenaufenth…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob bei einer Adesse der qsP in der Schweiz die Adresse vom Wohnort für die Ermittlung vom QST- Kanton berücksichtigt wird. - Prüfen ob im Falle von einem Wohnsitz im Ausland hinterlegt werden kann, ob die qsP täglich an den Wohnort im Ausland zurückkehrt oder über eine Wochenaufenthaltsadresse in der Schweiz verfügt. - Prüfen ob bei Auswahl von "Tagesaufenthalter" die Adresse vom Arbeitsort für die Ermittlung vom QST-Kanton berücksichti…


#### S_ELM_V6_03_07_05 Angaben zum Ehepartner ⬜ offen

**Directive** 10.3.3

**Beschreibung:** Angaben zum Ehepartner Für die Ermittlung des korrekten QST-Tarifcodes müssen folgende Angaben im ERP-System erfasst werden, sofern der Zivilstand der qsP verheiratet oder in eingetragener Partnerschaft lautet. Partnerdaten: - AHV-Nummer oder unbekannt - Name - Vorname - Geburtsdatum - Separate Adresse (wenn andere Adresse als qsP) - Wohnsitz - In der Schweiz (Kanton auswählen) - Im Ausland (zweistelliger ISO-Ländercode) Zusätzliche Angaben, sofern der Partner Einkommen/Ersatzeinkünfte erziehlt:…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die Partnerdaten im ERP-System vollständig erfasst werden können. - Prüfen ob im ERP-System eine logische Verbindung zwischen dem Zivilstand einer qsP und der Verpflichtung die Partnerdaten zu erfassen existiert. - Prüfen ob im ERP-System hinterlegt werden kann, ob ein Ehepartner Einkommen/Ersatzeinkünfte erziehlt. - Prüfen ob die zusätzlichen Angaben zu Einkommen/Ersatzeinkommen vom Ehepartner vollständig erfasst werden können.


#### S_ELM_V6_03_07_06 Angaben zu den Kindern ⬜ offen

**Directive** 10.3.4

**Beschreibung:** Angaben zu den Kindern Für die Ermittlung des korrekten QST-Tarifcodes müssen folgende Angaben im ERP-System erfasst werden, sofern die qsP minderjährige oder in Erstausbildung stehende volljährige Kinder hat: - Nachname - Vorname - Geburtsdatum - Beginn des Anspruchs auf Kinderabzug in der Quellensteuer - Ende des Anspruchs auf Kinderabzugs in der Quellensteuer

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob im ERP-System die Angaben zu Kindern für die Quellensteuer komplett erfasst werden können. - Prüfen ob im ERP-System die Angaben zu den Kindern anhand vom QST-Tarifcode validiert werden. Sobald ein QST-Tarifcode mit Kindern bei einer qsP hinterlegt wird, müssen auch die Angaben zu den Kindern zwingend übermittelt werden.


#### S_ELM_V6_03_07_07 QST-Tarif ⬜ offen

**Directive** 10.5

**Beschreibung:** QST-Tarif Im ERP-System müssen zwei Arten von QST-Tarifen erfasst werden können: - QST-Code - Vordefinierte Kategorie QST-Code: Der QST-Code setzt sich aus den drei Elementen Tarifgruppe, Kinderabzug und Kirchensteuer zusammen. Für die Zertifizierung ist es wichtig offenzulegen, wie er im ERP-System ermittelt wird. - Vorgeschlagen durch das System - Manuelle Eingabe des Anwenders Vordefinierte Kategorie: Für Personen, die aufgrund ihrer Eigenschaft als qsP keinen QST-Tarifcode haben, kann eine d…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die zwei Arten von QST-Tarifen im ERP-System ausgewählt werden können. - Prüfen ob der QST-Code vom System vorgeschlagen wird oder ob die Angabe manuell durch den Anwender erfasst werden muss und zusätzlich den Ablauf im ERP-System in ein paar Sätzen in den Kommentaren beschreiben. - Prüfen ob bei Auswahl "vordefinierte Kategorie" sämtliche möglichen vordefinierten Elemente im ERP-System abgebildet sind. - Prüfen ob bei Auswahl "offe…


#### S_ELM_V6_03_07_08 Speziell bewilligter QST-Tarifcode ⬜ offen

**Directive** 10.5.1.4

**Beschreibung:** Speziell bewilligter QST-Tarifcode Bei besonderen Verhältnissen kann die KSTV einen speziellen QST-Tarifcode bewilligen (z. B. einen weiteren Kinderabzug infolge von Alimentenzahlungen). Im ERP-System muss die Möglichkeit geschaffen werden, die Information zu erfassen, dass es sich beim übermittelten QST-Code um einen speziell bewilligten QST-Tarifcode handelt.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob im ERP-System erfasst werden kann, dass es sich beim übermittelten QST-Code um einen speziell bewilligten QST-Tarifcode handelt.


#### S_ELM_V6_03_07_09 Anzahl Arbeitstage CH ⬜ offen

**Directive** 10.6.3.1

**Beschreibung:** Anzahl Arbeitstage CH In bestimmten Konstellationen ist es möglich, dass nur ein Teil des QST-Lohnes in der Schweiz quellen- steuerpflichtig ist. Im ERP-System muss es möglich sein, den Anteil der Arbeitstage in der Schweiz zu erfassen, damit der QST-Lohn anteilsmässig auf den Anteil, der in der Schweiz geleistet wurde, reduziert werden kann. Für diese Ausscheidung ist mit 20 Arbeitstagen pro Monat als Basiswert zu rechnen. Beispiel: - XX Tage von 20 Tagen pro Monat

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob im ERP-System die Anzahl Arbeitstage in der Schweiz hinterlegt werden kann. - Prüfen ob im ERP-System als Default Wert automatisch 20 Tage (20/20 Tage) hinterlegt wird.


### Grenzgänger (`03_08`)

#### S_ELM_V6_03_08_01 Grenzgängermeldung Italien ⬜ offen

**Directive** 11.5

**Beschreibung:** Grenzgängermeldung Italien Im ERP-System muss es möglich sein, eine Person als vom Datenaustausch mit Italien betroffene Person zu markieren. Zusätzlich muss es für die Meldung an die zuständigen kantonalen Steuerverwaltung möglich sein, im ERP- System folgende zusätzlichen Angaben zu hinterlegen: - Geburtsort/Geburtsstaat - Italienische Steuernummer - Grenzgänger-Datum

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob im ERP-System eine Person als vom Datenaustausch mit Italien betroffene Person markiert werden kann. - Prüfen ob die zusätzlichen Angaben im ERP-System für vom Datenaustausch mit Italien betroffene Personen hinterlegt werden können und ob die Felder im ERP-System zu Pflichtfeldern werden, sobald eine Person entsprechend markiert wurde. - Prüfen ob die Italienische Steuernummer im ERP-System validiert wird. - Prüfen ob die Übermittlun…


#### S_ELM_V6_03_08_02 Grenzgängermeldung SFN ⬜ offen

**Directive** 11.3

**Beschreibung:** Grenzgängermeldung SFN Im ERP-System müssen alle qsP mit Tarifcode SFN automatisiert für die Grenzgängermeldung SFN berücksichtigt werden. Es müssen keine zusätzlichen Angaben hinterelegt werden.

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Prüfen ob die Abhängigkeit zum QST-Tarifcode SFN klar ist und die Übermittlung separat zu den weiteren Grengzängermeldungen ausgelöst werden kann.


#### S_ELM_V6_03_08_03 Grenzgängermeldung Frankreich ⬜ offen

**Directive** 11.4

**Beschreibung:** Grenzgängermeldung Frankreich Im ERP-System müssen alle Arbeitnehmer, die in Frankreich ansässig sind und deren gewöhnlicher Arbeitsort in der Schweiz liegt, automatisiert für die Grenzgängermeldung Frankreich berücksichtigt werden. Ausgenommen sind Arbeitnehmer mit Schweizer Staatsangehörigkeit, die für öffentlich-rechtliche Arbeitgeber tätig sind. Zusätzlich muss es für die Meldung an die zuständigen kantonalen Steuerverwaltung möglich sein, im ERP- System folgende zusätzlichen Angaben zu hint…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Sicherstellen, dass er spezifizierte Personenkreis automatisiert erkannt wird. - Prüfen ob der Anteil Telearbeit in Prozent im ERP-System für die Grenzgängermeldung Frankreich hinterlegt werden kann und ob das neue Element im ERP-System zu einem Pflichtfeld wird, sobald die Person entsprechend erkannt wurde. - Prüfen ob die Übermittlung separat zu den weiteren Grengzängermeldungen ausgelöst werden kann.


## S_ELM_V6_04 — Prozessuale Testfälle

### EMA-Meldungen (`04_01`)

#### S_ELM_V6_04_01_01 EMA-Meldungen Domäne AHV ⬜ offen

**Directive** 8.1.9

**Beschreibung:** EMA-Meldungen Domäne AHV Die EMA-Meldungen in der AHV umfassen folgende Ereignisse: - Eintritt in das Unternehmen - Austritt aus dem Unternehmen Die zwei Anwendungsfälle müssen automatisiert im ERP-System erkannt und für die Übermittlung aufbereitet werden. Die vorbereitete EMA-Meldung muss in den Aufgaben visualisiert und dem Anwender zur Übermittlung vorgeschlagen werden.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die AHV EMA-Meldungen automatisiert erstellt werden. - Prüfen ob die vorbereiteten EMA-Meldungen vom Anwender zur Übermittlung freigegeben werden können. - Prüfen ob die Angaben in der EMA-Meldung an das Eintritts- wie auch das Austrittsdatum gekoppelt sind. Hinweis: Im Systemtest muss ausschliesslich auf die Visualisierung eingegangen werden. Im Berechnungstest werden sämtliche Fälle geprüft.


#### S_ELM_V6_04_01_02 EMA-Meldungen Domäne FAK ⬜ offen

**Directive** 8.2.7

**Beschreibung:** EMA-Meldungen Domäne FAK Die EMA-Meldungen in der FAK umfassen folgende Ereignisse: - Eintritt - Austritt ...mit den Gründen: - Eintritt in das Unternehmen - Austritt in das Unternehmen - Kantonswechsel Die Anwendungsfälle müssen automatisiert im ERP-System erkannt und für die Übermittlung aufbereitet werden. Die vorbereitete EMA-Meldung muss in den Aufgaben visualisiert und dem Anwender zur Übermittlung vorgeschlagen werden.

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die FAK EMA-Meldungen automatisiert und mit der korrekten Begründung erstellt werden. - Prüfen ob die vorbereiteten EMA-Meldungen vom Anwender zur Übermittlung freigegeben werden können. - Prüfen ob die Angaben in der EMA-Meldung an das Eintritts-, Austritts- oder das Datum vom Arbeitsortwechsel gekoppelt sind. Hinweis: Im Systemtest muss ausschliesslich auf die Visualisierung eingegangen werden. Im Berechnungstest werden sämtliche F…


#### S_ELM_V6_04_01_03 EMA-Meldungen Domäne BVG ⬜ offen

**Directive** 8.6.8

**Beschreibung:** EMA-Meldungen Domäne BVG Die EMA-Meldungen in der BVG umfassen folgende Ereignisse und Begründungen: Eintritt: - Eintritt Vorsorgeplan - Beginn nach Arbeitsunterbruch Zusätzlich muss bei sämtlichen Eintrittsmeldungen übermittelt werden können, ob ein Arbeitnehmer 100% Arbeitsfähig ist. Mutationen: - Lohnänderung - Vorsorgeplan, Betrieb, Kategorie - Wohnadresse - Zivilstandsänderung - Teilpensionierung - Arbeitsunfähigkeit - vollständig Arbeitsfähig Austritt: - Austritt Vorsorgeplan - Pensionieru…

**Erwartet:** Die Erfassungsmaske muss wie folgt geprüft werden: - Prüfen ob die BVG EMA-Meldungen automatisiert und mit der korrekten Begründung erstellt werden. - Prüfen ob die vorbereiteten EMA-Meldungen vom Anwender zur Übermittlung freigegeben werden können. - Prüfen ob die Angaben in der EMA-Meldung an die korrekten Daten vom Auslöser gebunden sind. Hinweis: Im Systemtest muss ausschliesslich auf die Visualisierung eingegangen werden. Im Berechnungstest werden sämtliche Fälle geprüft.


### Lohnausweis DocID / Korrektur (`04_02`)

#### S_ELM_V6_04_02_01 Umgang mit der DocID auf dem Lohnausweis ⬜ offen

**Directive** 9.1.5

**Beschreibung:** Umgang mit der DocID auf dem Lohnausweis Mit der UUID (eine weltweit eindeutige Identifikationsnummer) als DocID wird der Lohnausweis auch im Lohnstandard als weltweit einzigartige Urkunde behandelt. Dieser “Urkunden-Charakter” hat zur Folge, dass in einem ERP-System Prozesse geschaffen werden müssen, die der neuen Anforderung gerecht werden.

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Sicherstellen, dass der Begriff UUID klar ist und vom ERP-Hersteller entsprechend umgesetzt wurde. - Prüfen ob im ERP-System ein Lohnausweis als Entwurf angezeigt werden kann und ob die DocID in diesem Fall mit den drei Begriffen «Entwurf – Brouillon – Bozza» befüllt wird. - Prüfen ob im ERP-System ein Lohnausweis finalisiert werden kann und damit den "Urkunden-Charakter" erhält. Zu diesem Zeitpunkt muss die DocID samt Erstellungsdatum vergeben und…


#### S_ELM_V6_04_02_02 Korrektur von Lohnausweisen ⬜ offen

**Directive** 9.1.6

**Beschreibung:** Korrektur von Lohnausweisen Für Korrekturen von fehlerhaften Lohnausweisen müssen im ERP-System zwei Möglichkeiten umgesetzt werden: - Korrektur eines Lohnausweises von einem einzelnen Arbeitnehmer (Rektifikat) - Korrektur der Lohnausweise für alle Arbeitnehmer (Ersatzmeldung mit Rektifikaten für den gesamten Bestand) Rektifikat: Wurde im Lohnausweis eines einzelnen Arbeitnehmers ein Fehler festgestellt, kann dies mittels einer Einzelmeldung (Rektifikat) mit Angabe der ursprünglichen Identifikat…

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Prüfen ob im ERP-System ein einzelner Lohnausweis korrigiert und als Rektifikat erneut übermittelt werden kann. - Prüfen ob beim Rektifikat eine neue DocID vergeben wurde und in Ziffer 15 auf die korrekte DocID vom zu korrigierenden Lohnausweis verwiesen wird. - Prüfen ob eine Ersatzmeldung für das gesamte Personal übermittelt werden kann und ob dabei für alle Arbeitnehmer neue Lohnausweise, mit neuen DocIDs und den korrekten Referenzen auf die zu …


### QST Ein-/Austritt (`04_03`)

#### S_ELM_V6_04_03_01 Eintritt, Mutation, Austritt in der Quellensteuer ⬜ offen

**Directive** 10.7

**Beschreibung:** Eintritt, Mutation, Austritt in der Quellensteuer Im ERP-System müssen die Stammdaten der letzten QST-Abrechnung automatisiert mit den Stammdaten des aktuellen Monats abgeglichen werden. Wenn Differenzen bestehen zwischen diesen Werten, müssen die definierten EMA-Gründe automatisiert durch das ERP-System erkannt und inkl. Gültigkeitsdatum (ValidAsOf) im XML abgefüllt werden. Folgende Gründe müssen erkannt werden: Eintritt: - Eintritt in das Unternehmen - Kantonswechsel - Andere (z.B. Trennung od…

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Sicherstellen, dass die EMA-Gründe samt Gültigkeitsdatum im ERP-System automatisiert erkannt werden und kein manueller Input von einem Anwender notwendig ist. (Das XML muss nicht geprüft werden, dieser Teil folgt im Berechnungstest) - Sicherstellen, dass der Umstand, dass QST-Mutationen immer per Folgemonat gültig sind, dem ERP-Hersteller bekannt ist und für das Gültigkeitsdatum berücksichtigt wird.


### Korrekturen & Historisierung (`04_04`)

#### S_ELM_V6_04_04_01 Rückwirkende Korrekturen nach der Lohnverarbeitung ⬜ offen

**Directive** 8.7.7

**Beschreibung:** Rückwirkende Korrekturen nach der Lohnverarbeitung Besteht die Möglichkeit, nach der Lohnverarbeitung rückwirkende Korrekturen vorzunehmen? Wie erfolgt der Belegnachweis?

**Erwartet:** Beschreibung des Ablaufs


#### S_ELM_V6_04_04_02 Jahresendverarbeitung ⬜ offen

**Directive** 8.7.7.3

**Beschreibung:** Jahresendverarbeitung Zu welchem Zeitpunkt erfolgt die Jahresendverarbeitung, nach der keine rückwirkende Korrekturen mehr möglich sind?

**Erwartet:** Beschreibung des Ablaufs


#### S_ELM_V6_04_04_03 Nachzahlung fürs Vorjahr ⬜ offen

**Directive** 8.7.5.2

**Beschreibung:** Nachzahlung fürs Vorjahr Wie behandelt das System eine Nachzahlung bei Austritt im Vorjahr?

**Erwartet:** Beschreibung des Ablaufs


#### S_ELM_V6_04_04_04 Nachzahlung nach Austritt im Vorjahr ⬜ offen

**Directive** 8.7.5.2

**Beschreibung:** Nachzahlung nach Austritt im Vorjahr Es muss im ERP-System sichergestellt werden, dass bei Nachzahlungen nach Austritt im Vorjahr mit den Lohndaten der korrekten Periode gerechnet wird. Zudem gilt es sicherzustellen, dass Korrekturen in der Quellensteuer (Leistungsanspruch mit dem Austritt) auch eine Korrektur am Lohnausweis zur Folge haben.

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Prüfen ob im Falle einer Nachzahlung nach Austritt im Vorjahr die AHV-/ALV-Beitragssätze des Austrittsjahres verwendet werden. - Prüfen ob bei einer Quellensteuer Korrektur (Old/New) je nach Charakter (RL 9.6.5) die QST-Tarifcodes, wie auch die generellen Verhältnisse des Austrittsmonats oder des Auszahlungsmonat für die Berechnung berücksichtigt werden. - Prüfen ob bei einer Quellensteuer Korrektur mit Leistungsanspruch mit dem Austritt der Anwend…


#### S_ELM_V6_04_04_05 Datenhaltung und Historisierung ⬜ offen

**Directive** 8.7.6

**Beschreibung:** Datenhaltung und Historisierung Können die Monats- und Jahresdokumente auch für das Vorjahr angezeigt werden? - Lohnarten-Rekapitulationen - Lohnkonti mit Total - Jahresabrechnungen

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Sicherstellen, dass bereits abgeschlossene Jahre jederzeit aufgerufen und ausgedruckt werden können.


#### S_ELM_V6_04_04_06 Historisierung von Firmen- und Personendaten ⬜ offen

**Directive** 8.7.6

**Beschreibung:** Historisierung von Firmen- und Personendaten Damit jederzeit während mindestens 5 Jahren allfällige Mutationen in allen relevanten Firmendaten nach- vollzogen werden können, müssen diese auf Papier ausgedruckt oder elektronisch historisiert werden. Die für alle Mitarbeiter gültigen Firmendaten können in einem Dokument zusammengefasst werden.

**Erwartet:** Das ERP-System muss wie folgt geprüft werden: - Sicherstellen, dass Vorgaben zur Historisierung eingehalten werden können.


#### S_ELM_V6_04_04_07 Mehrere Zahltagsverarbeitungen ⬜ offen

**Beschreibung:** Mehrere Zahltagsverarbeitungen - Sind pro Monat mehrere Zahltagsverarbeitungen möglich, wird der AHV-Freibetrag bei jeder Verarbeitung berechnet. Zusammen darf er die AHV-Freigrenze von CHF 1'400/Monat nicht überschreiten. - Sind pro Monat mehrere Zahltagsverarbeitungen möglich, werden die Versicherungshöchstlöhne bei jeder Verarbeitung berechnet. Daten gemäss Referenz Systemtest (beim Swissdec-Experten verlangen)

**Erwartet:** Korrekte Berechnung


## S_ELM_V6_05 — Cockpit Testfälle

### Cockpit Jahresmeldung (`05_01`)

#### S_ELM_V6_05_01_01 Cockpit Jahresmeldung - Generell ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit Jahresmeldung - Generell Im Übermittlungscockpit für die Jahresmeldung müssen im ERP-System folgende Domänen übermittelt werden können: - AHV - FAK - UVG - UVGZ - KTG - BVG - Lohnausweise (inkl. Rentenbescheinigungen und Mitarbeiterbeteiligungen) - Grenzgänger SFN - Grenzgänger Frankreich - Grenzgänger Italien Es empfiehlt sich die einzelnen Domänen im ERP-System thematisch zu gruppieren. Beispielsweise wie folgt: Ausgleichskassen: In diesem Bereich können die Jahresmeldungen für die Dom…

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob die Adressierung der einzelnen Übermittlungen klar ausgezeichnet wird. Der Anwender muss zu jedem Zeitpunkt wissen, an wen seine Daten übermittelt werden. - Prüfen ob sämtliche Domänen der Jahresmeldung im ERP-System übermittelt werden können. - Prüfen ob die Aufbereitung der Daten für die voraussichtliche BVG Jahresmeldung im Cockpit gestartet werden kann. - Prüfen ob die Übersicht über die voraussichtlichen Lohnsummen über de…


#### S_ELM_V6_05_01_02 Cockpit Jahresmeldung - Aktualisieren ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit Jahresmeldung - Aktualisieren Die technischen Prozesse vom Lohnstandard-CH (ELM) dürfen für den Endbenutzer nicht sichtbar sein. Sämtliche Prozessschritte müssen im ERP-System über eine "Aktualisieren" Funktion vorgenommen werden können.

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob im ERP-System eine "Aktualisieren" Funktion eingebaut wurde. - Prüfen ob die "Aktualisieren" Funktion sämtliche technischen Prozesse aktualisiert. Es dürfen im ERP-System keine Funktionen zum abholen vom Status (GetStatus) oder zur manuellen Synchronisierung visualisiert werden. Sämtliche Prozesse müssen über die eine "Aktualisieren" Funktion abgewickelt werden können.


#### S_ELM_V6_05_01_03 Cockpit Jahresmeldung - Testmeldungen ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit Jahresmeldung - Testmeldungen Im Übermittlungscockpit für die Jahresmeldung muss im ERP-System die Möglichkeit integriert werden, eine Übermittlung als "Testmeldung" zu markieren. Das Element muss im ERP-System gemäss den übersetzten Begriffen im Anhang x benannt werden.

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob im ERP-System die Möglichkeit eine Übermittlung als "Testmeldung" zu markieren umgesetzt wurde. - Prüfen ob die Funktion analog der Spezifikation im Anhang x benannt wurde.


#### S_ELM_V6_05_01_04 Cockpit Jahresmeldung - "Freigabe im Portal gewünscht" ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit Jahresmeldung - "Freigabe im Portal gewünscht" Im Übermittlungscockpit für die Jahresmeldung muss im ERP-System die Möglichkeit integriert werden, eine Übermittlung an die Versicherungsdomänen (UVG, UVGZ, KTG, BVG) mit der Information "Freigabe im Portal gewünscht" zu kennzeichnen. Das Element muss im ERP-System gemäss den übersetzten Begriffen im Anhang x benannt werden.

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob im ERP-System die Möglichkeit eine Übermittlung an die Versicherungsdomänen mit der Information "Freigabe im Portal gewünscht" zu kennzeichnen, umgesetzt wurde. - Prüfen ob die Funktion analog der Spezifikation im Anhang x benannt wurde.


#### S_ELM_V6_05_01_05 Cockpit Jahresmeldung - Mitteilungsübersicht ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit Jahresmeldung - Mitteilungsübersicht In der Mitteilungsübersicht müssen folgende Angaben zur letzten Übermittlung visualisiert werden: - Status (success, pending, error) - Übermittlungszeitpunkt - DeclarationID - Domäne - Empfänger - Benachrichtigungen (Notifications) - Aktionen (Freigabe starten, DialogMessage abholen, Quittung anzeigen)

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob die Mitteilungen der letzten Übermittlung direkt nach dem Klick auf Übermitteln angezeigt werden. Dies kann in der gleichen Benutzeroberfläche dargestellt werden oder auch via PopUp oder ähnlichen Funktionalitäten sichergestellt werden. - Prüfen ob die vorgegebenen Angaben im ERP-System in der Mitteilungsübersicht visualisiert werden. - Prüfen ob die einzelnen Elemente analog der Spezifikation im Anhang x benannt wurden. (Statu…


#### S_ELM_V6_05_01_06 Cockpit Jahresmeldung - Mitteilungshistorie ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit Jahresmeldung - Mitteilungshistorie In der Mitteilungshistorie müssen sämtliche vorgenommenen Übermittlungen visualisiert und ausgewählt werden können. Sobald eine Übermittlung ausgewählt wird, müssen die identischen Angaben zur Mitteilungsübersicht dargestellt werden. Über diese Übersicht können zudem Ersatzmeldungen für bereits vorgenommene Übermittlungen ausgelöst werden.

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob in der Mitteilungshistorie vergangene Übermittlungen angezeigt werden können. - Prüfen ob der Informationsgehalt identisch ist, wie in der Mitteilungsübersicht. - Prüfen ob aus der Mitteilungshistorie heraus direkt Ersatzmeldungen vorgenommen werden können.


### Cockpit Monatsmeldung (`05_02`)

#### S_ELM_V6_05_02_01 Cockpit Monatsmeldung - Generell ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit Monatsmeldung - Generell Im Übermittlungscockpit für die Monatsmeldung müssen im ERP-System folgende Domänen übermittelt werden können: - Quellensteuer - Statistik - UVG Spezialfall TariTemp (Suva) Quellensteuer: - Jeder adressierte Kanton muss einzeln für die Übermittlung ausgewählt werden können.

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob die Adressierung der einzelnen Übermittlungen klar ausgezeichnet wird. Der Anwender muss zu jedem Zeitpunkt wissen, an wen seine Daten übermittelt werden. - Prüfen ob sämtliche Domänen der Monatsmeldung im ERP-System übermittelt werden können. - Prüfen ob im ERP-System für die Domäne Quellensteuer die Kantone einzeln für die Übermittlung selektiert werden können.


#### S_ELM_V6_05_02_02 Cockpit Monatsmeldung - Weitere Tests ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit Monatsmeldung - Weitere Tests Die folgenden Funktionalitäten müssen im Cockpit der Monatsmeldung identisch dargestellt werden, wie auch im Cockpit der Jahresmeldung: - Aktualisieren - Testmeldungen - Mitteilungsübersicht - Mitteilungshistorie

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob die erwähnten Funktionalitäten identisch umgesetzt wurden.


### Cockpit SUA / Konfiguration (`05_03`)

#### S_ELM_V6_05_03_01 Cockpit SUA - Generell ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit SUA - Generell Die Swissdec Unternehmens-Authentifizierung (SUA) muss massentauglich ins ERP-System integriert werden: Da es sich bei SUA um einen standard- und domänenübergreifenden Baustein handelt, wird von Swissdec empfohlen, direkt im Cockpit an prominenter Stelle eine kurze Beschreibung zu SUA mitsamt einem Absprung auf eine "Konfigurationsseite" einzubauen. Die Beschreibung kann beispielsweise wie folgt lauten: "Die Möglichkeiten von Swissdec Standards umfassen Prozesse, die direk…

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen und im Systemtest notieren, wo im ERP-System die "Konfiguration" der SUA- und Anmeldeprozesse integriert wrude. - Prüfen ob eine allgemeine Beschreibung zu SUA und dem Zweck im ERP-System visualisiert wird. Es ist wichtig, dass in der Beschreibung darauf eingegangen wird, dass nur ein Zertifikat bestellt werden muss und dies für sämtliche Swissdec Standards gültig ist.


#### S_ELM_V6_05_03_02 Cockpit - Konfigurationsseite ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit - Konfigurationsseite Im ERP-System muss der gesamte SUA-Prozess (RegisterOrganizationAuthentication) und zusätzlich der Anmeldungsprozess (SubscribeOrganization) visualisiert werden. Swissdec empfiehlt diese Prozesse auf einer Art "Konfigurationsseite" darzustellen, die vom Übermittlungscockpit aus einfach zu erreichen ist. Es gilt die neuen Prozesse massentauglich ins ERP-System zu integrieren. Ziel muss sein, dass ein Benutzer die Prozesse selbständig bedienen kann und durch das Syste…

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob der SUA-Prozess massentauglich ins ERP-System integriert wurde. Dazu gehört wie bei den Jahres- und Monatsmeldung die Vorgabe, dass die Technik für die Anwender nicht sichtbar sein darf und der Prozess soweit beschrieben wird, dass er vom Benutzer selbständig durchgeführt werden kann. - Prüfen ob bei der Übermittlung der SUA-Bestellung die Angaben zum Unternehmen vorgängig visualisiert werden. Ziel davon ist es sicherzustellen,…


#### S_ELM_V6_05_03_03 Cockpit - Aufgabenhandling ⬜ offen

**Directive** Showcase

**Beschreibung:** Cockpit - Aufgabenhandling Im ERP-System muss an geeigneter Stelle die Möglichkeit geschaffen werden, dem Anwender Aufgaben anzuzeigen. Ziel davon ist es, die von aussen ausgelösten Prozesse dem Anwender in geeigneter Form zu visualisieren und daraus die Folgeaktivitäten im ERP-System zu starten. Auch hier ist es essentiell, dass die Technik für den Anwender nicht sichtbar sein darf. Die Synchronisation im ERP-System muss einmal täglich automatisiert (Teil der Basisdienste Zertifizierung) und zu…

**Erwartet:** Das Cockpit im ERP-System muss wie folgt geprüft werden: - Prüfen ob im ERP-System Aufgaben visualisiert werden können. - Prüfen wie im ERP-System die Aufgaben "aktualisiert" werden können. - Prüfen ob die integrierte Aufgabenübersicht aus Sicht Swissdec massentauglich ins ERP-System integriert wurde.


---

## Systemtest Tech — Prüfpunkte (Run 26667)

Technische Abläufe: Declares, Sync-Status, Fault-Anzeige, Completion, Lohnausweis/Barcode, DialogMessages, Warnungen.
Vieles kann bereits über Foundation + ELM-Cockpit abgedeckt sein — pro Gate prüfen und Kommentar setzen.

### ST_ELM_V6_01 — Lohnmeldung

#### ST_ELM_V6_01_01 Jahresmeldung übermitteln ⬜ offen

**Beschreibung:** Eine Jahresmeldung wird ausgelöst.

**Erwartet:** Der Übermittlungsprozess kann ausgelöst werden. Der Status der Übermittlung wird dem Benutzer dargestellt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_01_01_1` | DECLARE | Data Der Übermittlungsprozess der Jahresmeldung wird im ERP korrekt dargestellt. | offen |
| `ST_ELM_V6_01_01_2` | SYNCHRONIZE | Data Im Anschluss an die Übermittlung ist eine Synchronisierung möglich. | offen |


#### ST_ELM_V6_01_02 Jahresmeldung mit SwissdecFault ⬜ offen

**Beschreibung:** Eine Jahresmeldung wird mit einem SwissdecFault beantwortet.

**Erwartet:** Die Fehlermeldung wird dem Benutzer klar verständlich dargestellt. Die Meldung wird zurückgewiesen.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_01_02_1` | FAULT_DISPLAY | Data Die zurückgegebene Fehlermeldung (SwissdecFault) wird im ERP dargestellt. | offen |
| `ST_ELM_V6_01_02_2` | ABORT | Data Die Übermittlung wird abgebrochen. | offen |


#### ST_ELM_V6_01_03 Monatsmeldung übermitteln ⬜ offen

**Beschreibung:** Eine Monatsmeldung wird ausgelöst.

**Erwartet:** Der Übermittlungsprozess kann ausgelöst werden. Der Status der Übermittlung wird dem Benutzer dargestellt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_01_03_1` | DECLARE | Data Der Übermittlungsprozess der Monatsmeldung wird im ERP korrekt dargestellt. | offen |
| `ST_ELM_V6_01_03_2` | SYNCHRONIZE | Data Im Anschluss an die Übermittlung ist eine Synchronisierung möglich. | offen |


#### ST_ELM_V6_01_04 Monatsmeldung mit SwissdecFault ⬜ offen

**Beschreibung:** Eine Monatsmeldung wird mit einem SwissdecFault beantwortet.

**Erwartet:** Die Fehlermeldung wird dem Benutzer klar verständlich dargestellt. Die Meldung wird zurückgewiesen.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_01_04_1` | FAULT_DISPLAY | Data Die zurückgegebene Fehlermeldung (SwissdecFault) wird im ERP dargestellt. | offen |
| `ST_ELM_V6_01_04_2` | ABORT | Data Die Übermittlung wird abgebrochen. | offen |


### ST_ELM_V6_02 — Synchronisierung

#### ST_ELM_V6_02_01 Synchronisierung – Accepted ⬜ offen

**Beschreibung:** Erfolgreiche Synchronisierung einer beliebigen Domäne. Der Receiver liefert den ReceivedState „Accepted" zurück.

**Erwartet:** Die erfolgreiche Rückmeldung wird dem Benutzer angezeigt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_02_01_1` | SYNCHRONIZE | Data Die Synchronisierung einer beliebigen Domäne wird durchgeführt. | offen |
| `ST_ELM_V6_02_01_2` | RECEIVED_STATE | Data Der ReceivedState „Accepted" ist im ERP klar ersichtlich. | offen |


#### ST_ELM_V6_02_02 Synchronisierung – CompletionReleaseIsMissing ⬜ offen

**Beschreibung:** Synchronisierung einer beliebigen Domäne mit fehlender Freigabe. Der Receiver liefert den ReceivedState „CompletionReleaseIsMissing" zurück.

**Erwartet:** Die Antwort <CompletionReleaseIsMissing> wird dem Benutzer deutlich dargestellt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_02_02_1` | SYNCHRONIZE | Data Die Synchronisierung einer beliebigen Domäne wird durchgeführt. | offen |
| `ST_ELM_V6_02_02_2` | RECEIVED_STATE | Data Der ReceivedState „CompletionReleaseIsMissing" ist im ERP klar ersichtlich. | offen |


#### ST_ELM_V6_02_03 Synchronisierung – DialogMessage ⬜ offen

**Beschreibung:** Synchronisierung einer beliebigen Domäne, die eine offene DialogMessage enthält. Der Receiver liefert den ReceivedState „DialogMessage" zurück.

**Erwartet:** Die Antwort <Processing> wird dem Benutzer dargestellt. Falls vorhanden, wird der Zeitpunkt der Verfügbarkeit der Daten angezeigt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_02_03_1` | SYNCHRONIZE | Data Die Synchronisierung einer beliebigen Domäne wird durchgeführt. | offen |
| `ST_ELM_V6_02_03_2` | RECEIVED_STATE | Data Der ReceivedState „DialogMessage" ist im ERP klar ersichtlich. | offen |


#### ST_ELM_V6_02_04 Synchronisierung – Processing ⬜ offen

**Beschreibung:** Synchronisierung einer beliebigen Domäne. Der Receiver liefert den ReceivedState „Processing" zurück.

**Erwartet:** Der Status der Übermittlung wird dargestellt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_02_04_1` | SYNCHRONIZE | Data Die Synchronisierung einer beliebigen Domäne wird durchgeführt. | offen |
| `ST_ELM_V6_02_04_2` | RECEIVED_STATE | Data Der ReceivedState „Processing" ist im ERP klar ersichtlich. | offen |
| `ST_ELM_V6_02_04_3` | AVAILABILITY_TIME | Data Falls vom Receiver geliefert, wird der Zeitpunkt der Verfügbarkeit der Daten dem Benutzer angezeigt. | offen |


#### ST_ELM_V6_02_05 Synchronisierung – Finished ⬜ offen

**Beschreibung:** Synchronisierung einer beliebigen Domäne. Der Receiver liefert den ReceivedState „Finished" zurück und signalisiert damit den Abschluss des Falls.

**Erwartet:** Der Abschluss des Falls wird dargestellt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_02_05_1` | SYNCHRONIZE | Data Die Synchronisierung einer beliebigen Domäne wird durchgeführt. | offen |
| `ST_ELM_V6_02_05_2` | RECEIVED_STATE | Data Der ReceivedState „Finished" ist im ERP klar ersichtlich. | offen |
| `ST_ELM_V6_02_05_3` | PROCESS_FINISHED | Data Der Prozess ist abgeschlossen. Der Fall kann nicht weiter bearbeitet werden. | offen |


#### ST_ELM_V6_02_06 Synchronisierung – Rejected ⬜ offen

**Beschreibung:** Synchronisierung einer beliebigen Domäne. Der Receiver liefert den ReceivedState „Rejected" zurück und signalisiert die Rückweisung des Falls.

**Erwartet:** Die Rückweisung des Falls ist klar ersichtlich.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_02_06_1` | SYNCHRONIZE | Data Die Synchronisierung einer beliebigen Domäne wird durchgeführt. | offen |
| `ST_ELM_V6_02_06_2` | RECEIVED_STATE | Data Der ReceivedState „Rejected" ist im ERP klar ersichtlich. | offen |
| `ST_ELM_V6_02_06_3` | PROCESS_FINISHED | Data Der Prozess ist abgeschlossen. Der Fall kann nicht weiter bearbeitet werden. | offen |


#### ST_ELM_V6_02_07 Synchronisierung – SwissdecFault ⬜ offen

**Beschreibung:** Eine Synchronisierung wird mit einem SwissdecFault beantwortet.

**Erwartet:** Die Fehlermeldung wird dargestellt. Für den Benutzer ist klar ersichtlich, wie das weitere Vorgehen ist.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_02_07_1` | SYNCHRONIZE | Data Die Synchronisierung wird durchgeführt und der Receiver antwortet mit einem SwissdecFault. | offen |
| `ST_ELM_V6_02_07_2` | FAULT_DISPLAY | Data Die Fehlermeldung wird im ERP dargestellt und das weitere Vorgehen ist für den Benutzer klar. | offen |
| `ST_ELM_V6_02_07_3` | PROCESS_FINISHED | Data Der Prozess ist abgeschlossen. Der Fall kann nicht weiter bearbeitet werden. | offen |


### ST_ELM_V6_03 — Completion

#### ST_ELM_V6_03_01 Completion durchführen ⬜ offen

**Beschreibung:** Eine Completion kann erfolgreich durchgeführt werden.

**Erwartet:** Die Completion kann erfolgreich durchgeführt werden. Die Felder für Key und Passwort sind vorbefüllt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_03_01_1` | COMPLETION | Data Eine beliebige Completion wird ausgeführt; die vorbefüllten Felder Key und Passwort werden geprüft. | offen |


#### ST_ELM_V6_03_02 Completion mit zusätzlichen Parametern ⬜ offen

**Beschreibung:** Eine Completion-URL mit zusätzlichen Parametern kann erfolgreich aufgerufen werden, inklusive Vorbefüllung der Felder.

**Erwartet:** Die Completion kann erfolgreich durchgeführt werden. Die Felder für Key und Passwort sind vorbefüllt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_03_02_1` | COMPLETION | Data Eine beliebige Completion mit zusätzlichen URL- Parametern wird ausgeführt; die vorbefüllten Felder Key und Passwor… | offen |


#### ST_ELM_V6_03_03 Completion mit maximaler Länge ⬜ offen

**Beschreibung:** Completion-URLs mit maximalen Längen in den Parametern oder den Credentials können erfolgreich aufgerufen werden, inklusive Vorbefüllung der Felder.

**Erwartet:** Die Completion kann erfolgreich durchgeführt werden. Die Felder für Key und Passwort sind vorbefüllt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_03_03_1` | COMPLETION | Data Eine beliebige Completion mit Parametern und Credentials in maximaler Länge wird ausgeführt; die vorbefüllten Felde… | offen |


### ST_ELM_V6_04 — Lohnausweis / Barcode

#### ST_ELM_V6_04_01 Lohnausweis mit Viewgen ⬜ offen

**Beschreibung:** Generierung eines Lohnausweises mittels Viewgen. Die zu verwendenden Testdaten richten sich nach den jeweiligen Berechnungstests. Aktuelle Viewgen-Version: 2022.05.

**Erwartet:** Der Lohnausweis kann generiert werden und die Viewgen-Version ist in der Fusszeile ersichtlich.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_04_01_1` | GENERATE | Data Ein Lohnausweis wird mit Viewgen generiert. | offen |
| `ST_ELM_V6_04_01_2` | VIEWGEN_VERSION | Data Die Viewgen- Version wird in der Fusszeile des Lohnausweises überprüft. | offen |


#### ST_ELM_V6_04_02 Eigener Lohnausweis und Barcode ⬜ offen

**Beschreibung:** Ist der Transmitter nicht mit Viewgen realisiert, erfolgt die Analyse von Lohnausweis und Barcode manuell.

**Erwartet:** Die Barcodes können alle ausgelesen werden und der Fachexperte bestätigt die Gültigkeit der Inhalte.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_04_02_1` | GENERATE | Data Ein Lohnausweis wird generiert. | offen |
| `ST_ELM_V6_04_02_2` | SELECT_PERSONS | Data Die zu prüfenden Personen werden gemäss Fachinfo ausgewählt. | offen |
| `ST_ELM_V6_04_02_3` | SCAN_BARCODE | Data Die Barcodes werden mit dem Handscanner ausgelesen. | offen |
| `ST_ELM_V6_04_02_4` | FORWARD_CONTENT | Data Die Barcode- Inhalte werden an den Fachexperten weitergeleitet, der die Gültigkeit der Inhalte bestätigt. | offen |


### ST_ELM_V6_05 — DialogMessage ELM

#### ST_ELM_V6_05_01 Standard-Dialog ⬜ offen

**Beschreibung:** Verarbeitung einer StandardDialogMessage.

**Erwartet:** Die verschiedenen Felder der Standardmeldung müssen korrekt dargestellt werden können.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_05_01_1` | GET_DIALOG | Data Der TX führt eine Dialog- Abfrage durch und empfängt eine CompleteDialogMessage. | offen |
| `ST_ELM_V6_05_01_2` | UI | Data Die verschiedenen Felder der CompleteDialogMessage werden dem Benutzer korrekt dargestellt. | offen |


#### ST_ELM_V6_05_02 BESTA-Dialog ⬜ offen

**Beschreibung:** Die BESTA-ELM-DialogMessage muss korrekt dargestellt und verarbeitet werden.

**Erwartet:** Der Dialog kann korrekt dargestellt, ausgefüllt und beantwortet werden.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_05_02_1` | UI | Data Der BESTA-Dialog wird im ERP korrekt dargestellt, ausgefüllt und beantwortet. | offen |


### ST_ELM_V6_06 — Warnung / Information

#### ST_ELM_V6_06_01 Warnungen und Informationen anzeigen ⬜ offen

**Beschreibung:** Anzeige aller Warnings und Informations, global und pro Institution.

**Erwartet:** Alle Warnungen und Informationen werden angezeigt.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_06_01_1` | INFO | Data Eine Information (Info) wird im ERP angezeigt. | offen |
| `ST_ELM_V6_06_01_2` | WARN | Data Eine Warnung (Warn) wird im ERP angezeigt. | offen |


#### ST_ELM_V6_06_02 Ungültige AccountingPeriod ⬜ offen

**Beschreibung:** Versuch, Daten eines ungültigen Geschäftsjahres abzurufen. Eine AccountingPeriod, die nicht dem aktuellen oder dem vergangenen Jahr entspricht oder formal ungültig ist, muss zurückgewiesen werden.

**Erwartet:** Eine ungültige AccountingPeriod wird vom System als ungültig erkannt und entsprechend zurückgewiesen.

| Gate | Name | Erwartet | Stand |
|---|---|---|---|
| `ST_ELM_V6_06_02_1` | OUT_OF_RANGE | Data Eine AccountingPeriod von 2003 wird übermittelt; sie wird als ungültig erkannt, da sie weder dem aktuellen noch dem… | offen |
| `ST_ELM_V6_06_02_2` | MALFORMED | Data Eine AccountingPeriod von 20010 wird übermittelt; sie wird als formal ungültig erkannt. | offen |


---

## Nächste Schritte

1. Offene S01_01_05 (Nullmeldung FAK) und S01_02 (Lohnarten) durchgehen.
2. S02 Datenempfänger (AHV/FAK/UVG/…) gegen Hauptsitz-/Versicherungs-Masks.
3. S03 Personendaten — grosser Block, viel schon aus Personaldaten/QST vorhanden.
4. Tech-Gates gegen bestehendes Cockpit/Foundation mappen.
5. Optional später: UI-Kachel «Systemtest» wie Kommunikations-Test (Katalog-JSON).
