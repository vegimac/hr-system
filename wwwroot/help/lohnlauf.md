# Lohnlauf

Hier passiert das Wichtigste: aus Stempelzeiten, Verträgen und Absenzen wird der monatliche Lohn. Es gibt **zwei Läufe pro Periode** und beide brauchen die Zustimmung von Geschäftsführer *und* HR (4-Augen-Prinzip).

## Die zwei Läufe — wann was

| Lauf | Wann | Was passiert |
|---|---|---|
| **Akonto** | Mitte Monat (z.B. 20.) | Vorauszahlung — basierend auf bisher gestempelten Stunden bzw. % vom Festlohn |
| **Definitiv** | Ende Monat (z.B. 28.–31.) | Endgültiger Lohn — mit allen Korrekturen und Abrechnung der Akonto-Zahlung |

Du wechselst zwischen den beiden über die **Tab-Bar oben** im Lohn-Modul. Die Wahl wird gespeichert — du landest beim nächsten Öffnen wieder dort.

## Schritt-für-Schritt: Akonto-Lauf

**Du bist Geschäftsführer (Rolle „user"):**

1. **Sidebar → Lohn → Tab „Akonto-Lauf"**
2. Klick auf **„📅 Akonto vorbereiten"** — das System berechnet pro MA den Vorschuss-Betrag.
3. Schau jeden MA an. Wenn alles passt: **„✓ Freigeben"** pro Zeile. Korrektur nötig? → erst die Daten korrigieren (Stempel, Absenzen), dann „Neu berechnen", dann freigeben.
4. Wenn alle MA freigegeben sind: **„An HR senden"** ganz oben.

**Du bist HR (Rolle „superuser" oder „buchhaltung"):**

5. Du siehst alle MA als „GF freigegeben" (grünes Häkchen). Klick **„✓ HR-bestätigen"** pro MA. Wenn ein Betrag korrigiert werden muss: **„✎ ändern"**, neuen Betrag + Grund.
6. Wenn alle HR-bestätigt sind: **„💰 DTA auszahlen"** — zwei Dialogschritte:
   - Datum eingeben (Default: morgen).
   - „DTA an Bank gesendet?" mit JA bestätigen.
7. DTA-Datei wird heruntergeladen → an Bank schicken → Periode wechselt auf „Ausbezahlt".

💡 **Wichtig:** Die Bestätigung „DTA an Bank gesendet?" musst du erst klicken, *nachdem* du das File wirklich an die Bank geschickt hast. Sonst stimmt der Status nicht.

## Schritt-für-Schritt: Definitiv-Lauf

Funktioniert fast gleich wie Akonto, aber:

- **Akonto-Status muss passen:** Definitiv ist gesperrt, solange Akonto **läuft** (`IN_BEARBEITUNG_GF` / `BEI_HR` / `HR_FREIGEGEBEN`). Erlaubt ist:
  - Akonto **AUSBEZAHLT** (Normalfall — Vorauszahlung wird verrechnet), oder
  - Akonto **OFFEN** (Akonto bewusst übersprungen — z.B. kein Akonto-Termin / Legacy).
- Das System **startet Akonto nicht mehr automatisch**. Wenn du schon im Definitiv arbeitest, bleibst du dort — kein stiller Wechsel zurück zum Akonto-Tab.
- Der Definitivlauf **verrechnet eine ausbezahlte Akonto-Zahlung** automatisch — Zeile „Akonto-Vorauszahlung vom xx.xx.xxxx".
- Beim DTA-Versand landen **Lohnzettel + Stundenkontrolle (Monatsblatt)** im MA-Postfach; optional E-Mail-Hinweis.

## Was zeigt mir der Lohnzettel?

Auf der rechten Seite, wenn du einen MA wählst, siehst du den vollständigen Lohnzettel:

- **Lohn** — Festlohn, Stunden, Ferien-Auszahlung, 13. ML usw.
- **Abzüge** — AHV/IV/EO (5.3 %), ALV (1.1 %), NBU, KTG, BVG, Quellensteuer, LGAV.
- **Nettolohn** — Lohn minus Abzüge.
- **Weitere Zahlungen / Abzüge** — Familienzulagen, Lohnabtretungen (nur mit Beleg — [Lohnabtretungen](#lohnabtretungen)), Uniformen-Depot, Korrekturen.
- **Auszahlungsbetrag** — was geht effektiv an die Bank.
- **Stunden-Übersicht** (bei MTP/FIX/FIX-M): Soll · Ist · Differenz · Saldo.
- **Saldi** — Nacht-Saldo (**alle Modelle inkl. FLEX**), Ferien-Tage, Ferien-Geld, 13.-ML-Rückstellung.
- **Unbezahlter Urlaub** — Info-Zeile auf dem Zettel (FLEX/MTP); bei MTP ist der Festlohn bereits gekürzt.

## Was bedeuten die Sperren?

Manchmal lässt sich der Lohnlauf nicht abschliessen. Drei häufige Gründe:

🚫 **„Mindestlohn unterschritten"** — Ein MA verdient weniger als der L-GAV erlaubt. Geh in den Vertrag, hebe den Lohn auf den Mindestlohn an. Wenn der L-GAV gerade gestiegen ist, hilft der **„Verträge anpassen"**-Banner auf dem Dashboard.

🚫 **„QST-Pflicht offen"** — MA müsste Quellensteuer zahlen aber kein Tarif erfasst. Lösung: im MA-Detail → Tab **Bewilligung QST Bank** → **„🔴 Höchsten Tarif erfassen"** (dauert 3 Sekunden).

🚫 **„Lohnsumme fehlt"** — Vertrag hat keinen Lohn-Betrag (z.B. FLEX ohne Stundenlohn). Vertrag öffnen, Lohn nachtragen.

## Status kurz (Akonto)

| Periode | Was passiert |
|---|---|
| **OFFEN** | Noch nichts — „Akonto vorbereiten" |
| **IN_BEARBEITUNG_GF** | GF gibt pro MA frei |
| **BEI_HR** | HR bestätigt pro MA; GF gesperrt |
| **HR_FREIGEGEBEN** | Alle HR-bestätigt — DTA möglich |
| **AUSBEZAHLT** | Fertig (DTA bestätigt) |

## Status kurz (Definitiv)

| Periode | Was passiert |
|---|---|
| **offen** | GF bestätigt pro MA |
| **provisorisch_abgeschlossen** | Bei HR; Listen/Fibu möglich |
| **abgeschlossen** | DTA + Lohnzettel versendet |

Mehr zum Schutz der Daten: [Edit-Sperre](#edit-sperre). Akonto-% und Termine: [Filialen](#filialen).

## Geschäftsführer-Sicht vs. HR-Sicht

| Du bist GF (Filial-Leiter) | Du bist HR / Buchhaltung |
|---|---|
| Siehst nur deine Filiale | Siehst alle Filialen (Filter via Sidebar) |
| Bereitest vor, gibst frei, sendest an HR | Bestätigst, kannst Beträge korrigieren, schickst DTA |
| Hast keinen DTA-Knopf | Hast den 💰 DTA-Knopf |
| Bei Status „Bei HR" → alles gesperrt für dich | Du übernimmst die Kontrolle ab „Bei HR" |

Das System zeigt dir je nach Rolle nur die Buttons, die du auch benutzen darfst.

## Listen & Auswertungen zum Abschluss

Sobald der Definitivlauf mindestens **provisorisch abgeschlossen** ist, stehen in der Aktionszeile u.a.:

- **📊 Buchhaltung** / **📋 GF-Übersicht** — Saldo-Listen
- **Std.-Kontrolle** — Monatsblatt Stundenkontrolle (zur Unterschrift); beim Abschluss mit dem Lohnzettel ins [MA-Postfach](#postfach-ma)
- **Fibu-Journal** / Buchhaltungs-Saldo → eigener [Fibu-Bereich](#fibu)

💡 **DTA-Hinweis:** MA mit Auszahlungsbetrag 0.00 (z.B. FLEX ohne gestempelte Stunden) erscheinen bewusst **nicht** im DTA-File — Banken lehnen Aufträge mit Null-Zeilen ab. Lohnzettel und Abschluss sind davon nicht betroffen.

## Uniformen-Depot (CHF 50)

Beim **ersten Lohn** wird automatisch **CHF 50** als Uniformen-Depot einbehalten (Status «einbehalten»).

Bei **Austritt / letztem Lohn**:

- Uniform zurück → **Refund** auf dem (Korrektur-)Lohnzettel
- Nicht zurück → Depot **verfällt** (kein Refund)

Entscheidung: im Kündigungs-/Austritts-Flow **oder** nachträglich im MA-Tab **Zulagen → Uniformen-Depot** (Buttons nur bei Austrittsdatum / letztem Lohn). Details: [Kündigung & Austritt](#austritt).

## Korrekturlohn (ausgetretene MA)

Für Nachzahlungen / Korrekturen nach Austritt:

1. Im Lohn-Modul **«+ Korrektur»** → ausgetretenen MA wählen (aktive Filialwechsler sind ausgeschlossen)
2. Korrektur-Slip wie ein normaler Lohnzettel — inkl. spezieller Positionen:
   - **UVG/KTG-Korrektur** Codes `65.1` / `65.2` / `75.1` / `75.2`
   - **QST-Korrektur Vormonate** Lohnart **565**
   - ggf. Depot-Refund
3. Bestätigen auch in der HR-Phase möglich (wie Definitiv)

## Notfälle: Periode wieder öffnen

**Du bist Admin** und ein abgeschlossener Lauf hat einen Fehler:

- **Akonto zurücksetzen:** Sidebar → Lohnperioden → orangenen **„↺ Akonto zurücksetzen"**-Button. Funktioniert **nur bis zum Ausführungs-Datum** (also bevor die Bank die Zahlung verarbeitet hat).
- **Definitiv wieder öffnen:** Sidebar → Lohnperioden → „Wieder eröffnen". Ebenfalls nur bis zum Zahldatum.

⚠️ **Achtung:** Beim Reset musst du bestätigen, dass du die DTA-Datei bei der Bank gelöscht/storniert hast. Sonst zahlt die Bank zweimal! Das System fragt explizit nach.

## Stunden, Saldi, Rückstellungen — kurz erklärt

- **Nacht-Saldo** (Stunden) — für **alle Modelle inkl. FLEX**. Kein Geld, Kompensation mit Ruhetag. Soft-Warnung, wenn die Kompensation **> 9 h** wäre. Vortrag aus Mirus-Monatsblatt (Code 904) gilt auch für FLEX.
- **Ferien-Saldo (Tage)** — offene Ferientage (auch FLEX, inkl. Vormonat).
- **Ferien-Geld (CHF)** — FLEX/MTP: Saldo inkl. Vormonat. Bezug = Auszahlung aus dem **Pott** (Vormonat + aktueller Monat), anteilig Tage × Tagessatz, gedeckelt auf Pott-CHF.
- **13. ML / Probezeit:**
  - MTP/FIX/FIX-M: monatlich ansammeln, Auszahlung nach Vorgabe (meist Nov/Dez).
  - **FLEX während Probezeit:** der 13. wird als Saldo mitgeführt (Bezeichnung z.B. «Rückstellung Probezeit» / «Probe.Z. Rückstellung») — **nicht** ausbezahlt. Nach bestandener Probezeit (Stichtag = Periodenende) Nachzahlung + danach monatlich. **Verfall** nur, wenn der Austritt noch **während** der Probezeit liegt.

## Häufige Fragen

**Wer kriegt was und wann?**
- FLEX: Ferien-Tage **und** Ferien-Geld als Saldo (mit Vormonat); bei Bezug Auszahlung aus dem Pott. Feiertag monatlich. **13. ML:** während Probezeit nur Saldo; danach monatlich (siehe oben).
- MTP: Ferien-Tage + Ferien-Geld (Bezug aus dem Pott), Feiertag monatlich, 13. ML nach Vorgabe (meist November/Dezember).
- FIX/FIX-M: Ferien- und Feiertag-Tage akkumulieren (keine monatliche Auszahlung), 13. ML nach Vorgabe.

**Was ist der „Jahresausgleich" im Dezember-Lohnzettel?**
ALV und NBU sind nur bis CHF 148'200/Jahr beitragspflichtig. Damit das auch bei schwankenden Monatslöhnen passt, rechnet das System im Dezember die SV-Beiträge nochmal aufs ganze Jahr nach. Steht im Lohnzettel als „(Jahresausgleich)" hinter der Position.

**Warum sehe ich beim selben MA andere Werte als letzten Monat?**
Wahrscheinlich gab es Stempelkorrekturen, neue Absenzen oder eine Vertrags-Anpassung. Der **Aktivitäts-Log** (Admin-Bereich) zeigt dir genau, wer wann was geändert hat.

**Mein Lohnzettel ist zu breit und wird abgeschnitten.**
Sollte nicht mehr passieren — die Spalten sind jetzt so kompakt, dass alles in den Container passt. Falls doch: die rechten Spalten brechen um statt rauszulaufen. Sag uns Bescheid wenn's wieder vorkommt.

**Kann ich den Lohnzettel als PDF exportieren?**
Ja — im HR-Tab kannst du pro MA das **Lohnbeleg-PDF** generieren. Es geht automatisch beim Definitiv-Abschluss ins MA-Postfach.

## Lohnperioden (Admin)

Unter **System → Lohnperioden** (oder Perioden-Modul): Perioden anlegen, Bemerkung, Akonto zurücksetzen, Definitiv wieder öffnen, DTA erneut laden. Resets nur **bis zum Auszahlungsdatum** und nur mit Bestätigung „DTA bei der Bank storniert".

## Häufige Stolpersteine

- **Definitiv ist gesperrt** → Akonto noch in Bearbeitung / bei HR / HR-freigegeben? Dann warten oder Akonto abschliessen. Bei Akonto **OFFEN** oder **AUSBEZAHLT** darf Definitiv laufen. Der Akonto-Sperr-Banner erscheint nicht, wenn der Definitivlauf dieser Periode schon läuft.
- **„An HR senden" fehlt** → Sind alle MA freigegeben? Ein einziger nicht-bestätigter MA blockiert den Versand.
- **HR-Buttons fehlen für mich** → du bist als `user` eingeloggt (GF-Rolle). HR-Bestätigung und DTA macht jemand mit Rolle `superuser` oder `buchhaltung`.
- **Falscher Bank-Empfänger** → MA → Bewilligung QST Bank → Bank anpassen. Bei abweichendem Empfänger auch die Adresse vollständig ausfüllen.
- **Daten lassen sich nicht mehr ändern** → [Edit-Sperre](#edit-sperre) (hart vs. weich beachten).
- **Depot / Korrektur fehlt** → Uniformen-Depot im Zulagen-Tab oder Korrekturlohn «+ Korrektur»; aktive Filialwechsler nicht im Korrektur-Picker.
