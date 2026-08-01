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

- **Akonto muss vorher abgeschlossen sein** (Status „Ausbezahlt"). Sonst ist der Definitivlauf gesperrt.
- Der Definitivlauf **verrechnet die Akonto-Zahlung** automatisch — du siehst beim MA eine zusätzliche Zeile „Akonto-Vorauszahlung vom xx.xx.xxxx".
- Beim DTA-Versand werden zusätzlich **Lohnzettel-PDFs ins MA-Postfach** gelegt und (optional) per E-Mail benachrichtigt.

## Was zeigt mir der Lohnzettel?

Auf der rechten Seite, wenn du einen MA wählst, siehst du den vollständigen Lohnzettel:

- **Lohn** — Festlohn, Stunden, Ferien-Auszahlung, 13. ML usw.
- **Abzüge** — AHV/IV/EO (5.3 %), ALV (1.1 %), NBU, KTG, BVG, Quellensteuer, LGAV.
- **Nettolohn** — Lohn minus Abzüge.
- **Weitere Zahlungen / Abzüge** — Familienzulagen (Kinderzulage etc.), Lohnabtretungen.
- **Auszahlungsbetrag** — was geht effektiv an die Bank.
- **Stunden-Übersicht** (bei MTP/FIX/FIX-M): Soll · Ist · Differenz · Saldo.
- **Saldi** (Nacht-Saldo, Ferien-Tage, Ferien-Geld, 13.-ML-Rückstellung).

## Was bedeuten die Sperren?

Manchmal lässt sich der Lohnlauf nicht abschliessen. Drei häufige Gründe:

🚫 **„Mindestlohn unterschritten"** — Ein MA verdient weniger als der L-GAV erlaubt. Geh in den Vertrag, hebe den Lohn auf den Mindestlohn an. Wenn der L-GAV gerade gestiegen ist, hilft der **„Verträge anpassen"**-Banner auf dem Dashboard.

🚫 **„QST-Pflicht offen"** — MA müsste Quellensteuer zahlen aber kein Tarif erfasst. Lösung: im MA-Detail → QST-Tab → **„🔴 Höchsten Tarif erfassen"** klicken (dauert 3 Sekunden).

🚫 **„Lohnsumme fehlt"** — Vertrag hat keinen Lohn-Betrag (z.B. FLEX ohne Stundenlohn). Vertrag öffnen, Lohn nachtragen.

## Geschäftsführer-Sicht vs. HR-Sicht

| Du bist GF (Filial-Leiter) | Du bist HR / Buchhaltung |
|---|---|
| Siehst nur deine Filiale | Siehst alle Filialen (Filter via Sidebar) |
| Bereitest vor, gibst frei, sendest an HR | Bestätigst, kannst Beträge korrigieren, schickst DTA |
| Hast keinen DTA-Knopf | Hast den 💰 DTA-Knopf |
| Bei Status „Bei HR" → alles gesperrt für dich | Du übernimmst die Kontrolle ab „Bei HR" |

Das System zeigt dir je nach Rolle nur die Buttons, die du auch benutzen darfst.

## Listen & Auswertungen zum Abschluss

Sobald der Definitivlauf mindestens **provisorisch abgeschlossen** ist, stehen in der Aktionszeile zwei Saldo-Listen bereit: **„📊 Buchhaltung"** (alle Saldi, Brutto/Netto, IBAN, Summenzeile) und **„📋 GF-Übersicht"** (kompakt). Das **Fibu-Journal** und die Buchhaltungs-Saldo-Liste für die Finanzbuchhaltung liegen im eigenen [Fibu-Bereich](#fibu) (Rollen buchhaltung + admin).

💡 **DTA-Hinweis:** MA mit Auszahlungsbetrag 0.00 (z.B. FLEX ohne gestempelte Stunden) erscheinen bewusst **nicht** im DTA-File — Banken lehnen Aufträge mit Null-Zeilen ab. Lohnzettel und Abschluss sind davon nicht betroffen.

## Notfälle: Periode wieder öffnen

**Du bist Admin** und ein abgeschlossener Lauf hat einen Fehler:

- **Akonto zurücksetzen:** Sidebar → Lohnperioden → orangenen **„↺ Akonto zurücksetzen"**-Button. Funktioniert **nur bis zum Ausführungs-Datum** (also bevor die Bank die Zahlung verarbeitet hat).
- **Definitiv wieder öffnen:** Sidebar → Lohnperioden → „Wieder eröffnen". Ebenfalls nur bis zum Zahldatum.

⚠️ **Achtung:** Beim Reset musst du bestätigen, dass du die DTA-Datei bei der Bank gelöscht/storniert hast. Sonst zahlt die Bank zweimal! Das System fragt explizit nach.

## Stunden, Saldi, Rückstellungen — kurz erklärt

- **Nacht-Saldo** (in Stunden) — Nachtstunden werden gesammelt, kein direkter Geld-Wert. Der MA kompensiert sie irgendwann mit Ruhetag.
- **Ferien-Saldo (Tage)** — wie viele Ferientage hat der MA noch offen (auch bei FLEX, inkl. Vormonats-Saldo).
- **Ferien-Geld (CHF)** — bei FLEX/MTP: Ferienanspruch in CHF (Saldo, inkl. Vormonat). Beim Bezug Auszahlung anteilig aus dem Pott (Pott CHF / Pott Tage × bezogene Tage) — nicht monatlich.
- **Rückstellung 13. ML** — bei MTP/FIX/FIX-M wird der 13. monatlich angesammelt und am Auszahlungsmonat (meist November oder Dezember) komplett ausbezahlt. FLEX-MA kriegen den 13. monatlich anteilig.

## Häufige Fragen

**Wer kriegt was und wann?**
- FLEX: Ferien-Tage **und** Ferien-Geld als Saldo (mit Vormonat); bei Bezug Auszahlung aus dem Pott. Feiertag und 13. ML **monatlich**.
- MTP: Ferien-Tage + Ferien-Geld (Auszahlung bei Bezug aus dem Pott), Feiertag monatlich, 13. ML nach Vorgabe (meist November/Dezember).
- FIX/FIX-M: Ferien- und Feiertag-Tage akkumulieren (keine monatliche Auszahlung), 13. ML nach Vorgabe.

**Was ist der „Jahresausgleich" im Dezember-Lohnzettel?**
ALV und NBU sind nur bis CHF 148'200/Jahr beitragspflichtig. Damit das auch bei schwankenden Monatslöhnen passt, rechnet das System im Dezember die SV-Beiträge nochmal aufs ganze Jahr nach. Steht im Lohnzettel als „(Jahresausgleich)" hinter der Position.

**Warum sehe ich beim selben MA andere Werte als letzten Monat?**
Wahrscheinlich gab es Stempelkorrekturen, neue Absenzen oder eine Vertrags-Anpassung. Der **Aktivitäts-Log** (Admin-Bereich) zeigt dir genau, wer wann was geändert hat.

**Mein Lohnzettel ist zu breit und wird abgeschnitten.**
Sollte nicht mehr passieren — die Spalten sind jetzt so kompakt, dass alles in den Container passt. Falls doch: die rechten Spalten brechen um statt rauszulaufen. Sag uns Bescheid wenn's wieder vorkommt.

**Kann ich den Lohnzettel als PDF exportieren?**
Ja — im HR-Tab kannst du pro MA das **Lohnbeleg-PDF** generieren. Es geht automatisch beim Definitiv-Abschluss ins MA-Postfach.

## Häufige Stolpersteine

- **Definitiv ist gesperrt obwohl Akonto durch ist** → Status-Sicht: ist Akonto wirklich „Ausbezahlt" oder noch „HR-freigegeben"? Erst nach DTA-Klick wechselt der Status final.
- **„An HR senden" fehlt** → Sind alle MA freigegeben? Ein einziger nicht-bestätigter MA blockiert den Versand.
- **HR-Buttons fehlen für mich** → du bist als `user` eingeloggt (GF-Rolle). HR-Bestätigung und DTA macht jemand mit Rolle `superuser` oder `buchhaltung`.
- **Falscher Bank-Empfänger** → MA-Bank-Tab → Bankverbindung anpassen. Bei abweichendem Empfänger (z.B. Revolut) auch die Adresse vollständig ausfüllen, sonst lehnt die Bank die SEPA-Zahlung ab.
