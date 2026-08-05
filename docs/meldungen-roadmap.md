# Meldungen an Versicherungen & Behörden — was Schaub Restaurants wirklich braucht

Stand 05.08.2026 · 7 Filialen · Go-live Lohn 1.1.2027 (bis dahin meldet Mirus).
Grundsatz: Wir sind EIN Arbeitgeber — keine Swissdec-Zertifizierung nötig.
Alle Empfänger nehmen Listen/Portal-Eingaben an. OneCrew muss die ZAHLEN
liefern, nicht das ELM-XML.

---

## Wichtig fürs Timing

Das Jahr 2026 meldet **Mirus** (letztes produktives Lohnjahr). OneCrew muss
erstmals liefern: **monatlich ab Januar 2027**, **Jahresmeldungen im Januar 2028**.
Es ist also Zeit — aber die Monats-Pflichten (QST!) müssen am 1.1.2027 stehen.

---

## 1. GastroSocial Ausgleichskasse (AHV/IV/EO · ALV · FAK · Familienzulagen)

**Wann:** Jahresmeldung im Januar (Lohnbescheinigung per 31.12.); unterjährig
nur Akonto-Rechnungen der Kasse (keine MA-Meldung nötig).

**Was:** Pro MA: AHV-Nr., Name, Beschäftigungsdauer von–bis, AHV-pflichtiger
Jahreslohn, ALV-pflichtiger Lohn (gedeckelt 148'200). Erfassung im
GastroSocial-Portal (partnerweb) oder als Liste.

**OneCrew-Stand:** Daten vollständig in den Snapshots (`SvBasisAhv` pro Monat,
Deckelung rechenbar). **Zu bauen: Report «AHV-Lohnbescheinigung Jahr»**
(Liste pro Filiale + Gesamt, Excel + PDF). Aufwand: klein.

## 2. GastroSocial Pensionskasse (BVG «Uno»)

**Wann:** Laufend Ein-/Austritte + per 1.1. Jahresmeldung der versicherten Löhne.

**OneCrew-Stand:** BVG-Basen pro MA vorhanden (Koordination, Schwelle, Staffeln).
**Zu bauen: Report «BVG-Lohnliste per 1.1.»** (gemeldeter Jahreslohn pro
versichertem MA). Ein-/Austritte weiterhin manuell im Portal. Aufwand: klein.

## 3. UVG-Versicherer (BU/NBU)

**Wann:** Jahres-Lohnsummendeklaration im Januar (definitive Lohnsumme pro
Betrieb/Betriebsteil, NBU-Lohn gedeckelt).

**OneCrew-Stand:** NBU-Basen in jeder Abrechnung. **Zu bauen: Report
«UVG-Lohnsummen Jahr»** (pro Filiale: Summe versicherter Lohn BU/NBU).
Aufwand: klein.

## 4. KTG-Versicherer

**Wann:** Jahres-Lohnsummendeklaration (analog UVG).
**Zu bauen:** gleiche Mechanik wie UVG-Report — zusammen bauen. Aufwand: klein.

## 5. Quellensteuer — pro Kanton (AG, BE, LU, SO …)

**Wann: MONATLICH** (Abrechnung + Ablieferung, mit Bezugsprovision) — das ist
die einzige Meldung, die am **1.1.2027 zwingend bereit** sein muss.

**Was:** Pro Kanton und Monat: Liste der QST-pflichtigen MA (AHV-Nr., Tarifcode,
QST-Basis, Satz, Betrag), Total, abzüglich Bezugsprovision. Eingabe in den
kantonalen Portalen oder als Liste.

**OneCrew-Stand:** Beträge/Tarife stecken in jedem Lohnzettel; An-/Abmeldungen
(QST-Anmeldung PDF) existieren. **Zu bauen: Report «QST-Monatsabrechnung pro
Kanton»** (+ Jahres-Rekapitulation). Aufwand: mittel — wichtigster offener Report.

## 6. Lohnausweise (ESTV Form 11) an alle MA

**Wann:** Januar (fürs Vorjahr).
**OneCrew-Stand:** Einzel-Lohnausweis fertig (Form 11 mit Barcode).
**Zu bauen: Bulk-Generierung** «alle MA eines Jahres» + Ablage/Versand
(Postfach — nach Go-live unkritisch). Aufwand: klein (war ohnehin Phase 2).

## 7. BFS-Statistik

**LSE** (Lohnstrukturerhebung, alle 2 Jahre, Referenzmonat Oktober):
**Export existiert bereits** (LSE-CSV). ✓
**BESTA/übrige:** nur falls das BFS uns in eine Stichprobe zieht — dann ad hoc.

## 8. L-GAV Kontrollstelle

Keine periodische Meldung — nur bei Kontrolle. Stundenkontrollblatt mit
Ruhetag-Klassifizierung (1.0/0.5 Frei) ist dafür bereits gebaut. ✓

---

## Reihenfolge (Vorschlag)

1. **QST-Monatsabrechnung pro Kanton** — bis Dezember 2026 (Muss für 1.1.27);
   lässt sich im Parallelbetrieb gegen die Mirus-QST-Abrechnungen testen.
2. **Jahres-Reports AHV / UVG / KTG / BVG** — im Lauf von 2027, gegen die
   Mirus-Jahresmeldung 2026 plausibilisierbar.
3. **Lohnausweis-Bulk** — bis Ende 2027.

Alles aus vorhandenen Snapshot-Daten — kein neues Rechenwerk, nur Auswertungen.
Swissdec/ELM bleibt Fernziel (nur relevant, falls OneCrew je an Dritte geht).
