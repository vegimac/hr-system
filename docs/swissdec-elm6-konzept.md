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
- **CheckInteroperability gegen Prod: Fault `Client.security`** — «rejected …
  non-certified transmitter or has not been signed». Ab CheckInteroperability
  verlangt der Prod-Distributor WS-Security-signierte Nachrichten von
  zertifizierten Transmittern (Antworten kommen ebenfalls signiert, X509).
  → Übungen laufen auf dem Refapps Receiver; die Signatur-Infrastruktur
  (Transmitter-Zertifikat) ist ein Baustein der Zertifizierungs-Etappen.

## 7. Nächster konkreter Schritt

**E1 bauen:** SOAP-Client-Gerüst aus `SalaryDeclarationService.wsdl`,
Ping + CheckInteroperability gegen den Refapps Receiver (URL aus der
Testinfrastruktur), Ergebnis sichtbar in einer kleinen Admin-Seite
«Elektronische Lohnmeldung (ELM)». Ab dann haben wir einen bewiesenen
Draht zu Swissdec und bauen Etappe für Etappe darauf auf.
