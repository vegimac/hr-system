# SV-Sätze & getrennte Rechtseinheiten — Konzept (Stand 05.08.2026, Nacht)

Ausgangslage: Jede der 7 Filialen ist eine **eigene GmbH** (eigener Arbeitgeber).
OneCrew führt die SV-Sätze heute **global** (eine Tabelle für alle Filialen) und
aggregiert Jahreswerte über alle Filialen eines MA. Das muss vor dem Go-live
1.1.2027 differenziert werden. Dieses Papier sortiert, was global bleiben darf,
was pro Filiale werden muss, und wie wir es bauen.

---

## 1. Welche Sätze sind was?

| Satz | Wer bestimmt ihn | Pro Filiale unterschiedlich? |
|---|---|---|
| **AHV / IV / EO** (5.3%) | Gesetz, schweizweit | **Nein** — bleibt global |
| **ALV** (1.1%, Höchstlohn 148'200) | Gesetz, schweizweit | **Nein** — bleibt global |
| **L-GAV-Beitrag** | L-GAV national | **Nein** — bleibt global |
| **Quellensteuer** | Wohnkanton des MA | Filiale irrelevant (bleibt wie gebaut) |
| **BU** (Berufsunfall, AG-seitig) | UVG-Police pro GmbH | **Möglich** — Walter fragt Broker |
| **NBU** (1.521% AN) | UVG-Police pro GmbH | **Möglich** — Walter fragt Broker |
| **KTG** (2.15 / 2.15) | KTG-Vertrag pro GmbH | **Möglich** — Walter fragt Broker |
| **BVG / BVG-Zusatz** (GastroSocial Uno) | Anschlussvertrag pro GmbH | Vermutlich gleicher Plan, aber **eigene Anschlussnummer** pro GmbH — Broker fragen |
| **FAK** (heute global 1.635%) | AHV-Kasse + **Standort-Kanton** | **Ja, sehr wahrscheinlich** — AG/BE/LU haben unterschiedliche FAK-Ansätze (war in CLAUDE.md schon als offen markiert) |
| **Verwaltungskosten AHV** (falls je gebucht) | Kasse, pro Abrechnungsnummer | vermutlich gleich (alle GastroSocial) |

Deckt sich mit Walters Einschätzung: **BU, NBU, KTG** (+ FAK) sind die Kandidaten.
AHV/IV/EO/ALV/BVG-*Prozentsätze* bleiben aller Voraussicht nach identisch — bei
BVG geht es eher um die getrennten **Anschlüsse/Meldungen** als um andere Zahlen.

## 2. Fragen an den Versicherungsbroker (morgen)

1. **UVG:** Eine Sammel-Police für alle GmbHs oder eine pro GmbH? Sind die
   BU-/NBU-Prämiensätze überall identisch? Policen-Nummern pro GmbH?
2. **KTG:** dito — ein Vertrag oder pro GmbH? Sätze identisch? Aufteilung AG/AN?
3. **BVG (GastroSocial):** Ein Anschluss **pro GmbH**? Überall derselbe Plan
   («Uno Basis» + Zusatz)? Anschlussnummern für die Jahresmeldung?
4. **AHV (GastroSocial):** Eine Abrechnungsnummer pro GmbH? → bestimmt, wie
   viele Lohnbescheinigungs-Listen wir im Januar liefern.
5. **FAK:** Welche FAK-Ansätze gelten pro GmbH am jeweiligen Standort-Kanton
   (AG / BE / LU)?
6. **Filialwechsel eines MA zwischen euren GmbHs:** Behandeln Kassen/Versicherer
   das als Austritt+Eintritt (neue Eintrittsmeldung BVG, AHV-Lohnbescheinigung
   in beiden GmbHs) — und dürfen Ferien-/13.-Saldi arbeitgeberübergreifend
   mitgenommen werden, oder ist beim Wechsel eine Schlussabrechnung nötig?

## 3. Bauplan (Vorschlag, in Etappen)

**E1 — Filial-Override für SV-Sätze (klein, zuerst):**
`social_insurance_rate` bekommt eine nullable Spalte `company_profile_id`.
NULL = globaler Satz (alles bleibt wie heute). Gesetzt = Override für genau
diese Filiale. Die Engine wählt pro Abzug: Filial-Zeile vor Global-Zeile
(gleiche Versionierung «Neu ab», gleiche Sperr-Logik). UI: SV-Sätze-Seite
bekommt eine Filial-Spalte/Auswahl «Alle Filialen (Standard)» + Overrides.
→ Damit sind BU/NBU/KTG/FAK pro Filiale abbildbar, ohne dass Walter für
identische Sätze 7× pflegen muss.

**E2 — FAK pro Filiale:** nutzt E1 — pro Filiale eine FAK-Zeile mit dem
kantonalen Satz (Antwort aus Frage 5). Fibu-Buchung bleibt unverändert.

**E3 — Jahres-Aggregation pro Arbeitgeber:** Der Dezember-Jahresausgleich
(ALV/NBU-Höchstlohn) summiert heute die Monate über ALLE Filialen des MA.
Neu: Gruppierung pro `company_profile_id` (= pro GmbH). Betrifft nur MA mit
Filialwechsel im Jahr; testbar im Dezember-Parallellauf gegen Mirus.
Gleiches Prinzip später für Lohnausweis (einer pro GmbH und Jahr) und die
Jahresmeldungen (Meldungen-Roadmap: Listen pro GmbH statt gesamthaft).

**E4 — Filialwechsel-Prozess:** je nach Antwort auf Frage 6 —
Variante A (Kassen führen durch): Wechsel bleibt wie heute, nur Meldungen
pro GmbH. Variante B (echter AG-Wechsel): beim Wechsel automatische
Schlussabrechnung in der alten Filiale (die Austritts-Schlussabrechnung
existiert schon!) + frischer Start in der neuen. Entscheid nach Broker-Input.

**Nicht nötig:** Trennung von AHV/ALV/L-GAV-Sätzen, QST-Umbau, Kontoplan-
Umbau (Fibu-Journal ist ohnehin pro Filiale).

## 4. Offene Pendenzen (Gesamtliste, Kurzform)

- SMS scharf schalten (eCall-Test-Umleitung leeren) — 2 Klicks
- Lohnbelege-zurückhalten-Schalter (vor MA-Postfach-Rollout) + Aufräumen der Test-Lohnzettel
- Postfach-Upload mit SMS-Benachrichtigung (zurückgestellt)
- Abacus: Rückmeldung Simone zum File mit Buchungsnummer + Rundungs-MWST
- 13.-ML-Auszahlungsmonate Juni+Dezember im Filial-Raster hinterlegen
- 23.9.: Martinas Probezeit-Ende → Nachzahlung gegen Mirus prüfen
- Karteileichen-Aufräumen ausführen (Vorschau → Löschen)
- Julia Sanchez Büchi: Einzel-Sync nach Deploy (ID-Fix) → Nummer nachziehen
- **Dieses Papier: SV-Sätze/Rechtseinheiten E1–E4 nach Broker-Antworten**
- Klein: SV-Sätze-Badge-Überlappung, Ferien-Kürzung 329b bei Austritt,
  eCall-Zustellstatus, Schulungsunterlage Postfach/SMS, Verträge-Seite auf
  Heimatfiliale-Prinzip nachziehen
