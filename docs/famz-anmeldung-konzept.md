# Anmeldung Familienzulagen (GastroSocial FOR 0402) — Walter 11.10.2026

Walter: «alle infos die wir brauchen in der kinder maske einbauen. beim partner auch ergänzen
was wir brauchen. auch beim ma ist der zivilstand sehr wichtig … dieses formular übernehmen und
durch onecrew erstellen lassen, so wie wir schon die anmeldung ahv machen.»
Ergänzung: «einkommen höher ist wichtig bei der familienzulage. hat unser ma ein tieferes
einkommen als der partner, erhält im normalfall der partner das kindergeld.»

## Was OneCrew macht

- Seite **HR-Hub → Behörden-Korrespondenz → Anmeldung Familienzulagen** (`page-famz-anmeldung`,
  `js/famz-anmeldung.js`) und Knopf «📄 Anmeldung Familienzulagen» im Familie-Tab (nur mit Kindern).
- MA wählen → Kinder wählen (max. 5 pro Formular, Vorgabe alle bis 25) → Vorbefüllung → alles
  editierbar → PDF im Vorschaufenster. Es wird **nichts gespeichert**; fehlende Angaben gehören
  in die Familie-Maske.
- Backend: `GET api/famz-anmeldung/{empId}/prefill?kinder=1,2`, `POST …/pdf`
  (`FamzAnmeldungController`). Das PDF füllt die AcroForm-Felder des Originals
  `Assets/Forms/FamzAnmeldung_GastroSocial_0402.pdf` und macht sie flach
  (`FamzAnmeldungPdfService`). Feldnamen/Zustände sind im Service kommentiert.

## Woher die Angaben kommen

| Formular | Quelle |
|---|---|
| Antragsteller/in | MA-Stammdaten; Zivilstand + «seit» aus Zivilstand / getrennt seit / Zivilstand-Historie |
| Arbeitgeber, Abrechnungsnummer | Filiale des aktuellen Vertrags; Nummer = Empfänger FAK, sonst Ausgleichskasse |
| Beschäftigt von/bis | nahtlose Vertragskette in derselben Filiale |
| Bruttolohn, Einkommen > 7'560 | Vertrag + mutmasslicher Jahreslohn (`BvgPflichtService.VorschlagAsync`) |
| Weitere Arbeitgeber | Tabelle `weitere_arbeitgeber` (nur Info, wie bisher) |
| Leistungsbezug | Absenzen heute (Unfall/Krankheit/Mutterschaft) |
| Anderer Elternteil | am Kind gewählt (`anderer_elternteil_id`), häufigster der gewählten Kinder |
| Aktuelle/r Ehe-/Lebenspartner/in | nie der andere Elternteil; Ehepartner (nur solange MA verheiratet/getrennt/eingetr. Partnerschaft) vor Konkubinatspartner, jüngster Eintrag (Walter 11.10.2026: «wenn neuer Lebenspartner vorhanden, dann diese Info»). Erstanspruch-Hinweis nur beim anderen Elternteil bzw. Ehepartner |
| Kinder | Familie: Beziehung, Geschlecht, erwerbsunfähig; Wohnadresse nur wenn nicht im Haushalt |
| Sorge / Kinder leben bei | Kind-Angaben; gemischt ⇒ Text in der Bemerkung |

Hinweise (gelb) und Beilagenliste rechnet der Server mit (z.B. Ausländerausweise, Ausbildungsbestätigung
ab 15/16, Erstanspruch beim Partner). Was in OneCrew wirklich fehlt (anderer Elternteil, «verdient mehr?»,
Beziehung zum Kind, Wohnadresse ausserhalb des Haushalts) liefert der Server getrennt als `fehlt` (rot).

**Maske (Walter 11.10.2026):** ov-Look — warme Karte, weisse Feld-Pillen, Abschnitts-Balken, 6er-Raster.
Kopfzeile = Mitarbeiter | Kinder | Meldungen (rot «Fehlt», gelb Hinweise, Beilagen aufklappbar).
Leere Pflichtangaben werden rot umrandet (`famzPflicht` in `js/famz-anmeldung.js`, bedingte Felder erst
wenn die Auslöser-Frage es verlangt, z.B. «Von wem» nur bei «Leistungen: Ja»; AHV/Ausweis des anderen
Elternteils nur bei Wohnsitz Schweiz = Kanton gesetzt). Beim anderen Elternteil und beim Partner sind
«verdient mehr», «Einkommen > 7'560», SE/NE und Leistungsbezug immer Pflicht, Pensum bei «angestellt». Ehepartner-Block ohne Eintrag zugeklappt
(«von Hand ausfüllen»). Der Zähler neben «PDF-Vorschau» sperrt nichts.

**In OneCrew speichern (Walter 11.10.2026):** Knopf im Fuss erscheint, sobald ein Feld, das beim Laden
leer war, ausgefüllt ist (`POST /api/famz-anmeldung/{empId}/in-onecrew-speichern`, Logik
`Services/FamzRueckschreiben.cs`). Nur leere OneCrew-Felder werden gefüllt, nie überschrieben; ein
ungültiger Wert (AHV-Prüfziffer, Datum, Pensum, Kanton) ⇒ gar nichts gespeichert. Speicherbar:
MA «Zivilstand seit» + AHV; Elternteil/Ehepartner Geschlecht, Geburtsdatum, AHV, Zivilstand, angestellt
(mit Erwerbs-Historie «Ersterfassung»), Arbeitsort Kanton, Pensum, verdient mehr, Einkommen > 7'560,
SE/NE, Ausgleichskasse, Leistungen; Kind Geschlecht, Geburtsdatum, Beziehung; «Elterliche Sorge» (füllt
jedes Kind mit leerer Sorge); «Kinder leben bei: beide Eltern» verknüpft Kinder im Haushalt OHNE anderen
Elternteil mit dem anderen Elternteil aus Abschnitt 4 — nur wenn dieser selbst im Haushalt lebt
(Mutter/Vater allein ergibt sich aus dem Haushalt, wird nicht gespeichert). Bewusst NICHT: Name,
Adresse, Nationalität, Bewilligung, Arbeitgeber-Zeilen (Historie/zusammengesetzt → Familie-Maske).
Tests `Tests/FamzRueckschreibenTests.cs`.

## Kind-Maske (vier Blöcke)

1. **Kind:** Name, Geburt, Geschlecht, Beziehung (leiblich/Adoptiv/Stief/Pflege/Geschwister/Enkel),
   anderer Elternteil, elterliche Sorge, erwerbsunfähig, Belege.
2. **Wohnen & Unterhalt:** Verlauf ab Datum (Haushalt, Unterhalt zur Hauptsache) und — wenn nicht im
   Haushalt — die Wohnadresse (Zusatzadresse des MA, mit Land). Neues Kind: Startwerte.
3. **Ausbildung:** Abschnitte (QST-Verlauf).
4. **Quellensteuer:** Ergebnis + QST-Entscheide.

Entfernt beim Kind: Nationalität, «In der Schweiz lebend», Häkchen «keine Unterhaltspflicht»,
Adress-Radios, «Erfahren am». Bestehende Werte bleiben in der Datenbank.

## Partner / anderer Elternteil

Neuer Familientyp **`AndererElternteil`** (Kindsmutter/-vater, nicht Partner/in). Bei Ehepartner,
Konkubinatspartner und anderem Elternteil zusätzlich: Zivilstand, Arbeitspensum, Telefon Arbeitgeber,
Einkommen über CHF 7'560/Jahr, als SE/NE angemeldet (+ Kasse), bezieht Zulagen/Leistungen (+ von wem).

**Erstanspruch:** «Ist das Einkommen … höher als das des MA?» erscheint bei allen drei Typen, sobald die
Person erwerbstätig ist. Verdient sie mehr, erhält in der Regel **sie** die Kinderzulage (FamZG Art. 7).
OneCrew zeigt das als Hinweis am Partner, am Kind und im Formular — gesperrt wird nichts, massgebend ist
die Verfügung der Familienausgleichskasse. Gespeichert bleibt `ma_hat_hoeheres_einkommen`
(MA-Sicht, Maske fragt aus Partner-Sicht und spiegelt).

**Gemeinsames Kind (QST):** ergibt sich aus dem anderen Elternteil — ist es der Konkubinatspartner,
gilt das Kind als gemeinsam (H1/A0-Regel), sonst nicht. «nicht erfasst / unbekannt» = nicht gemeinsam,
«offen» = ungeklärt.

## Datenbank (Schema-Stand 61)

`employee_family_member`: `beziehung_zum_ma`, `erwerbsunfaehig`, `elterliche_sorge`,
`anderer_elternteil_id` (FK, ON DELETE SET NULL), `arbeitspensum`, `arbeitgeber_telefon`,
`einkommen_ueber_minimum`, `se_ne_angemeldet`, `se_ne_ausgleichskasse`, `bezieht_leistung`,
`leistung_von`. Einmal-Migration: Häkchen «keine Unterhaltspflicht» → Verhältnis-Version
«Unterhalt = nein»; gemeinsame Kinder → anderer Elternteil = Konkubinatspartner.

Tests: `Tests/FamzAnmeldungTests.cs` (PDF gefüllt + flach, AHV-Kammfeld, Wohnt-bei, Sorge-Zusammenfassung,
Vertragskette), `Tests/KindQstVerlaufTests.cs`.

## GastroSocial-Portal per Lesezeichen (Walter 11.10.2026)

OneCrew darf nicht in fremde Seiten schreiben (Browser-Sicherheit). Deshalb `js/famz-gastrosocial.js`:
«Für GastroSocial kopieren» legt JSON (`onecrew: "famz-gs"`) in die Zwischenablage — inkl. der
Zuordnung Portal-Beschriftung → Wert pro Schritt (Antrag, Ehe-/Lebenspartner, anderer Elternteil, Kind n).
Das Lesezeichen «OneCrew einfüllen» (`famzGsEngine`, eingerichtet über «Lesezeichen einrichten»)
ist eine reine Füll-Maschine: findet Felder über die Beschriftung (darunter/rechts daneben), füllt nur
leere Felder, meldet Abweichungen (≠) und Nicht-Gefundenes (✗). Zuordnung anpassen = nur OneCrew deployen,
das Lesezeichen bleibt. Neues Lesezeichen nur nötig, wenn `famzGsEngine` selbst geändert wird.
«Felder melden» kopiert die Beschriftungen der Portal-Seite (ohne Werte) für die Zuordnung.
Schritt 1 (Antrag) nach Screenshot gebaut; Schritte 2–4 sind Annahmen bis zur ersten Feldmeldung.
Personensuche und «Meldeverfahren» wählt der Benutzer selbst.
