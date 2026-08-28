# Konzept: Swissdec ELM 6.0 — elektronische Lohnmeldung für OneCrew

**Status: ENTWURF v1 (27.08.2026)** — Grundlage: ELM 6.0 «Richtlinien für
Lohndatenverarbeitung» (507 S., Ausgabe 06.03.2026 = aktuelle
Zertifizierungsbasis), «Richtlinien für Lohndatentransmitter» (240 S.),
WSDL/XSD/Beispiel-XMLs (alles unter `docs/swissdec/`), Swissdec-
Testinfrastruktur-Zugang (Benutzer «schaub»).

## 1. Zielbild

OneCrew meldet die Lohndaten der Schaub Restaurants GmbH elektronisch über
den Swissdec-Distributor an alle Empfänger:

| Meldung | Operation | Empfänger | Rhythmus |
|---|---|---|---|
| AHV/ALV-Lohnbescheinigung | DeclareAnnualSalary | GastroSocial (AK) | jährlich |
| Familienzulagen (FAK) | DeclareAnnualSalary | FAK | jährlich |
| UVG / UVGZ / KTG | DeclareAnnualSalary | Versicherer | jährlich |
| BVG (voraussichtliche Löhne) | DeclareAnnualSalary | GastroSocial (PK) | jährlich |
| Lohnausweise (Tax) | DeclareAnnualSalary | Kantonale Steuerverwaltungen | jährlich |
| **Quellensteuer** | **DeclareMonthlySalary** | Kantonale Steuerverwaltungen | **monatlich** |
| Statistik (LSE!) | DeclareMonthlySalary | BFS | monatlich/periodisch |
| Ein-/Austritte/Mutationen | NotifyChanges | AK / FAK / BVG (+QST via Monatsmeldung) | laufend |
| Versicherungsprofile | SubscribeOrganization | UVG/UVGZ/KTG/BVG | bei Anmeldung |

Wichtige Grundsätze aus den Richtlinien:

- **Meldeeinheit = Rechtseinheit (UID)**, nicht Filiale. Schaub Restaurants
  GmbH ist EIN Arbeitgeber; die 6 Filialen werden als «Workplace»-Elemente
  geführt. Das deckt sich mit unserer Konvention «EIN AHV-Arbeitgeber»
  (Dezember-Jahresausgleich) und dem Konzept
  `sv-saetze-rechtseinheiten-konzept.md`.
- **Adressierung neu in ELM 6.0** über `Job/Addressee` mit
  `AddresseeIdentification` (z.B. Ausgleichskassen-Nr., Versicherer-Nr.) —
  die Empfänger-Nummern kommen aus der «Liste der Lohndatenempfänger»
  (Infopoint).
- **Protokoll pro Meldung (3 Schritte):** Declare… (Übermittlung) →
  GetStatus… (Credentials) → Synchronize… (Prozesssteuerung, Quittung,
  evtl. DialogMessage/Completion). Identisch für Jahres- und Monatsmeldung.
- **SUA-Zertifikat** (Unternehmensauthentifizierung) ist NUR Pflicht, wenn
  Versicherungsprofile digital bezogen werden — für den Einstieg optional.
- **Rundung:** kaufmännische 5er-Rundung — machen wir bereits (Round05).
- Das **ELM-Lohnraster** (Kapitel 5 Musterlohnarten) ist exakt der Katalog,
  den wir am 17.08.2026 als `elm_lohnraster` übernommen haben — unsere
  Codes UND die produktiven Katalog-Flag-Basen sind schon ELM-konform.
- **Kapitel 10 (QST)** deckt sich mit unserer QST-Arbeit: Konfession,
  Halbfamilie, Ehepartner-/Kinder-Angaben, Tarifcodes, satzbestimmender
  Lohn, Kantonswechsel, SSL-Nummer (haben wir: `company_profile_ssl`).
  Monatliche QST-Meldung inkl. EMA konsolidiert.

## 2. Architektur in OneCrew (neu zu bauen)

```
Services/Elm/
  ElmDeclarationBuilder.cs   ← baut AnnualSalaryDeclaration / MonthlySalary-
                               Declaration aus PayrollSnapshots + Saldi +
                               Stammdaten (pro Rechtseinheit, Staff aus allen
                               Filialen, SalaryTotals + SalaryCounters)
  ElmXmlValidator.cs         ← XSD-Validierung gegen SalaryDeclaration.xsd
                               (Schemas liegen in docs/swissdec/…/schema)
  ElmTransmitterClient.cs    ← SOAP-Client aus SalaryDeclarationService.wsdl
                               (Ping, CheckInteroperability, Declare*, GetStatus*,
                               Synchronize*), Ziel per Konfiguration:
                               Refapps Receiver (Test) / Distributor (Prod)
  ElmQuittungStore           ← Tabelle elm_meldung: Job-ID, Domäne, Status,
                               Quittungen, DialogMessages (Audit + UI)
Controllers/ElmController.cs ← Vorschau (XML anzeigen/validieren), Senden,
                               Status-Synchronisation, Quittungen
wwwroot/js/elm.js            ← UI im HR-Bereich: «Elektronische Lohnmeldung»
```

Fehlende Stammdaten (kleine Vorab-Etappe): pro Rechtseinheit UID-BFS,
AHV-Abrechnungsnummer/Kassen-Nr., UVG/UVGZ/KTG-Vertragsnummern +
Versicherer-Nr., BVG-Vertragsnr. — Erfassung in den Systemeinstellungen
(passt zum offenen Rechtseinheiten-Konzept).

## 3. Etappenplan

| # | Etappe | Inhalt | Ergebnis |
|---|---|---|---|
| **E1** | «Hallo Distributor» | ElmTransmitterClient minimal: **Ping** + **CheckInteroperability** gegen den **Refapps Receiver** der Testinfrastruktur | Verbindung/SOAP/TLS bewiesen |
| **E2** | XML-Generator AHV | AnnualSalaryDeclaration für die Domäne AHV aus **Testinstanz-Kunstdaten** (test.onecrew.ch — dafür gebaut!), XSD-valid | valides Jahresmeldungs-XML |
| **E3** | Stammdaten Rechtseinheit | UID, Kassen-/Versicherer-/Vertragsnummern erfassen (Systemeinstellungen), Empfängerliste abgleichen | Adressierung komplett |
| **E4** | Erste echte Übermittlung (Test) | DeclareAnnualSalary (AHV) an Refapps Receiver, 3-Schritt-Protokoll, Quittung + DialogMessages im UI | Ende-zu-Ende-Zyklus |
| **E5** | Alle Jahres-Domänen | FAK, UVG, UVGZ, KTG, BVG, Lohnausweis (Tax) — Basen haben wir flag-getrieben | vollständige Jahresmeldung |
| **E6** | QST-Monatsmeldung | DeclareMonthlySalary (TaxAtSourceSalaries) inkl. EMA-konsolidiert, Korrektur-/Ersatzmeldung | ELM-QST testfähig |
| **E7** | EMA + Statistik | NotifyChanges (AHV/FAK/BVG als «Aufgabe» im UI) · StatisticSalaries (ersetzt langfristig unseren LSE-Export!) | Zusatz-Domänen |
| **E8** | Musterfälle + Quality Tool | Swissdec-Testfirmen (OpenProject) auf der Testinstanz nachbauen, Läufe im **Quality Tool** (API-Schlüssel QT), Abweichungen fixen | zertifizierungsreif |
| **E9** | Zertifizierung + Produktion | Zertifizierung (durch Suva im Auftrag Swissdec; Umfang/Kosten vorab mit Swissdec klären), dann produktiver Distributor | scharfe Meldungen |

Zeithorizont (realistisch, neben dem Tagesgeschäft): E1–E4 Herbst 2026,
E5–E8 Winter 2026/Frühjahr 2027, Zertifizierung im Lauf von 2027.
**Erste scharfe Jahresmeldung: Lohnjahr 2027 (Übermittlung Januar 2028).**
QST 2027 läuft bis zur Zertifizierung weiter über die bisherigen kantonalen
Kanäle (unsere QST-Abrechnung/Anmeldungs-PDFs) — ELM-QST kommt, sobald
zertifiziert.

## 4. Was uns entgegenkommt (Bestandsaufnahme)

- ELM-Lohnraster-Codes + flag-getriebene SV-Basen (17./18.08.2026) ✔
- QST komplett nach KS 45 inkl. Tarifautomatik, SSL-Nummern, Kantonswechsel ✔
- Kalendermonats-Perioden, Round05, Dezember-Jahresausgleich, Snapshots als
  eingefrorene Wahrheit ✔
- Testinstanz mit Kunstdaten als gefahrlose Übungsumgebung ✔
- Lohnausweis-Logik (Form 11) vorhanden — ELM-Tax ist derselbe Inhalt als XML ✔
- Beispiel-XMLs + WSDL + XSDs liegen lokal ✔

## 5. Offene Punkte / Risiken

1. **Zertifizierungspflicht & Kosten:** produktive PIV-Übermittlung setzt
   zertifizierte Software voraus. Umfang, Dauer und Kosten der Erst-
   zertifizierung (inkl. allfälliger Swissdec-Auflagen für Hersteller) früh
   mit Swissdec klären — Kontakt via Testinfrastruktur/OpenProject.
2. **Anhänge 3/4/6** der Richtlinien erscheinen erst im Lauf von 2026 —
   Release-Watch via Infomail (ERP-Hersteller) abonnieren.
3. **BVG-Domäne** ist die komplexeste (Vertragssteuerung im Lohnartenstamm,
   Beitragsänderungs-Prozess) — bewusst ans Ende von E5.
4. **SUA/Versicherungsprofile**: erst nach dem Grundgerüst (optional).
5. Übermittlung erfolgt vom Server (VPS) — ausgehende TLS-Verbindungen zum
   Distributor/Refapps freigeben (Firewall prüfen).
6. Statistik-Domäne ersetzt den LSE-Export erst, wenn BFS-seitig alles über
   ELM läuft — bis dahin beide Wege pflegen.

## 6. Endpoints (Stand 27.08.2026)

| Umgebung | ELM-v6-Endpoint |
|---|---|
| **Refapps Receiver (Test)** — unser Übungsplatz | `https://test.swissdec.ch/refapps/stable/receiver/services/elm/SalaryDeclaration/V6` |
| Produktiver Distributor | `https://distributor.swissdec.ch/services/elm/SalaryDeclaration/V6` |

E1-Erkenntnisse (27.08.2026, live getestet):
- **Ping gegen Prod: ERFOLGREICH** — PingResponse mit Distributor-UserAgent
  («swissdec distributor 2026.08 PROD») + Systemzeit. Verbindung, SOAP 1.1,
  ELM-6.0-Namespaces und TLS vom VPS damit bewiesen.
- **CheckInteroperability gegen Prod UND Refapps: Fault `Client.security`** —
  «rejected … non-certified transmitter or has not been signed». Ab
  CheckInteroperability verlangen BEIDE Umgebungen WS-Security-signierte
  Nachrichten (Antworten kommen ebenfalls signiert, X509).

**Zertifikatsfrage geklärt (SecurityTransmitter_d.pdf, Version 2024.05, liegt
in `docs/swissdec/`):**
- Das **ERP-/Transmitter-Zertifikat wird erst NACH erfolgreicher
  Zertifizierung von Swissdec ausgestellt** (Swissdec = eigene CA/RA, Kap.
  3.1/3.2). Es gibt KEINEN öffentlichen Test-Keystore zum Herunterladen; für
  die Entwicklung führt der Weg über die Swissdec-Erstberatung/
  Zertifizierungsvereinbarung (dort wird der Entwicklungs-Zugang geregelt).
- Zusätzlich zur Signatur muss **jede Operation ausser Ping VERSCHLÜSSELT**
  werden (WS-Encryption mit dem Public Key des Distributors; Reihenfolge:
  zuerst signieren, dann verschlüsseln — Kap. 3.3). Algorithmen: Signatur
  X.509v3/BinarySecurityToken, Verschlüsselung rsa-oaep-mgf1p +
  aes256-cbc. Der signierte Umfang ist immer Body + Timestamp.
- **Offizieller Übungsweg ohne eigenes Zertifikat = Refapps-TRANSMITTER**
  (Web-Applikation der Testinfrastruktur, RefApps_Schnelleinstieg.pdf):
  fertiges XML hochladen → DeclareSalary → GetStatus; die Refapps signiert/
  verschlüsselt selbst. E2–E4 laufen darüber — **nicht blockiert**.
- Die «Certificates»-Tabelle + RegisterOrganizationAuthentication im
  Refapps-Transmitter betreffen das **SUA-Zertifikat** (Unternehmens-Ausweis,
  «Default Refapps SUA Certificate») — NICHT das Transmitter-Zertifikat.
- **Konsequenz für E1:** Ping = erledigt (beide Umgebungen grün).
  CheckInteroperability aus OneCrew heraus (= E1b: WS-Security Signierung +
  Verschlüsselung im ElmTransmitterClient) wird NACH Erhalt des
  Entwicklungs-Zertifikats gebaut — Zertifikat via Erstberatung anfragen
  (Punkt 1 der offenen Punkte, ohnehin Pflichtweg für E9).

## 7. Nächster konkreter Schritt

**E2 ERLEDIGT (28.08.2026):** Jahresmeldung AHV aus Kunstdaten
(5 Kunst-MA, Übungsfiliale, Lohnlauf Juli 2026 auf test.onecrew.ch)
erzeugt, XSD-valid, im Refapps-Transmitter hochgeladen (dort als valide
elm-v6-DeclareAnnualSalary erkannt) und übermittelt:
**DeclareAnnualSalary → SUCCESS** (JobKey vergeben) ·
**GetStatus → Sending State ACCEPTED** (Swissdec Jackpot Institution,
Domäne AHV-AVS, Adressat 001.234-Platzhalter) ·
**Completion-Link → «Successfully released Completion!»** — der volle
3-Schritt-Zyklus Declare→GetStatus→Synchronize inkl. Completion-Freigabe
ist einmal komplett durchgespielt. Erkenntnis: Business State
`COMPLETION_RELEASE_MISSING` = Empfänger verlangt Browser-Freigabe über
den mitgelieferten Completion-Link (key+password in URL, Testsystem).

**Filial-Mitgliednummern (Walter-Info 28.08.2026):** JEDE Filiale hat bei
der AHV-Ausgleichskasse UND bei allen anderen Lohndatenempfängern eine
EIGENE Mitgliednummer (Mirus: «Mitgliednummer» pro Betrieb; in OneCrew
bereits korrekt abgebildet im Empfänger-Katalog `LohndatenEmpfaenger` +
`CompanyProfileEmpfaenger.Mitgliednummer/Subnummer`). ELM unterstützt das:
mehrere Addressees/Institutions pro Domäne (maxOccurs unbounded, inkl.
AK-CC-SubNumber), jeder MA-Lohnblock verweist per `addresseeIDRef` auf
seine Nummer. **Konsequenz für E5:** Der Generator stellt von der einen
elm_stammdaten-Abrechnungsnummer (E2/E3-Vereinfachung) auf den
Empfänger-Katalog um — ein Addressee pro Filial-Mitgliednummer, Personen
nach Filiale zugeordnet (Mehrfilialen-MA = mehrere Lohnblöcke).
Erstberatungs-/GastroSocial-Frage: Meldung pro Mitgliednummer wie heute
abgerechnet — oder konsolidiert?

**Filial-UIDs (Walter-Info 28.08.2026):** Jede Filiale hat als
Zweigniederlassung ihre EIGENE UID (z.B. Sursee CHE-300.834.691,
Oftringen CHE-262.373.037) — bereits in den Filial-Stammdaten erfasst.
Für die Meldung gilt: CompanyDescription/UID-BFS = UID der RECHTSEINHEIT
(Hauptsitz der GmbH — via E3-Karte erfassen, NICHT der Filial-Fallback!);
die Filialen erscheinen als Workplaces, dort ist die BUR-Nummer die
vorgesehene Kennung (CompanyProfile.BurNummer vorhanden, z.B. Oftringen
A63837147 — Workplace um BUR-REE-Number ergänzen = kleiner E5-Punkt).
Zweigniederlassungs-UIDs braucht die Lohnmeldung selbst nicht.
Hauptsitz Schaub Restaurants GmbH = **Meggen** (Hauptsitz-UID → E3-Feld).

**Design-Vorgabe MULTI-RECHTSEINHEIT (Walter 28.08.2026, fürs
Lizenz-Produkt):** Es gibt Franchisenehmer mit ZWEI Hauptsitzen (= zwei
GmbHs/Rechtseinheiten) mit je 3–4 Filialen. Zielbild ab E5:
`Rechtseinheit` als eigenes Objekt (Name, Hauptsitz-UID, Sitzadresse),
`company_profile.rechtseinheit_id` als Zuordnung, `elm_stammdaten` +
Meldungserzeugung PRO Rechtseinheit (eine Installation = n Meldungen).
Die heutige Eine-Zeile-Lösung ist der Spezialfall n=1 und bleibt bis E5.

Nächste Schritte:

1. **E3:** Stammdaten Rechtseinheit (UID, AK-Nummer GastroSocial,
   Versicherer-/Vertragsnummern) erfassen — ersetzt die Platzhalter.
2. **Zertifikat anstossen:** Erstberatung Swissdec (Termin vereinbart,
   Unterlagen: docs/Swissdec-Erstberatung-OneCrew.docx) → danach E1b
   (WS-Security Signierung + Verschlüsselung, CheckInteroperability grün).
3. **E4:** Declare/GetStatus/Synchronize direkt aus OneCrew (braucht E1b).
