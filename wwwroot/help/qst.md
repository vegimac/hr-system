# Quellensteuer (QST)

Quellensteuer ist die Steuer, die du dem MA **direkt vom Lohn** abziehen und an den Kanton abliefern musst. Sie betrifft alle Mitarbeitenden ohne Schweizer Pass oder C-Bewilligung.

## Muss ich QST abziehen?

Das System prüft das **automatisch** für dich — in zwei Schritten.

**Schritt 1 — Wohnsitz:** Massgebend ist die Hauptwohnsitzadresse (aus
easy@work). Wohnt der MA im **Ausland**, ist auf den Schweizer Lohn
grundsätzlich IMMER QST geschuldet — auch ein Schweizer Pass oder
C-Ausweis befreit bei Auslandswohnsitz **nicht** automatisch (Grenzgänger
siehe unten).

**Schritt 2 — bei Wohnsitz Schweiz:** **Ein MA ist QST-pflichtig, AUSSER** eine dieser fünf Bedingungen ist erfüllt:

1. 🇨🇭 **Schweizer Bürger** (Nationalität CH)
2. **C-Ausweis** (Niederlassungsbewilligung) — gültig am Stichtag
3. **Behörden-Befreiung** mit Schreiben der Steuerbehörde
4. **Verheiratet mit einem Schweizer** (oder Schweizerin)
5. **Verheiratet mit einem C-Ausweis-Inhaber**

In allen anderen Fällen → **QST-pflichtig**.

⚠️ **Konkubinat befreit NIE:** Ein **Konkubinatspartner** mit CH-Pass oder
C-Ausweis befreit den MA **nicht** — die Befreiung über den Partner gilt nur
bei **Ehe oder eingetragener Partnerschaft**. Darum im Familie-Tab immer den
richtigen Typ wählen: «Ehepartner» vs. «Konkubinatspartner/in».

⚠️ **Der Ehepartner muss selbst in der Schweiz wohnen:** Wohnt er eindeutig
im Ausland, gilt die Befreiung 4/5 **nicht** — der MA bleibt pflichtig.
Ist die Wohnsituation des Partners unklar → mit der Steuerbehörde klären.

## Der empfohlene Ablauf (5 Schritte)

1. **Bewerbungsbogen** (McAdmin) vollständig ausfüllen lassen — erste Quelle für alle QST-Angaben.
2. **MA erfassen**: Nationalität, Bewilligung, Zivilstand + seit, Konfession, Wohnadresse (der Wohnkanton bestimmt, welcher Kanton die Steuer erhält).
3. **Familie-Tab komplett**: Ehepartner (erwerbstätig? + Arbeitgeber), Konkubinatspartner (erwerbstätig? + Einkommensfrage), Kinder (Haushalt, gemeinsames Kind, Erstausbildung ab 18).
4. **QST-Informationsformular** (McAdmin → «QST-Info Formular») drucken und ZUSAMMEN mit dem MA ausfüllen — deckt alle Fragen des kantonalen Anmeldeformulars ab. Antworten in OneCrew nachtragen.
5. **QST-Erfassung** anlegen (Vorschlag prüfen/übernehmen) + **QST-Anmeldung** (PDF) an die kantonale Steuerbehörde.

📄 **Kurz für Geschäftsführer (1 Seite A4, nur AG/SO/BE/LU):** `/qst-gf-kurz.html` (drucken) oder direkt `/QST-Kurz-GF.pdf`. Das ausführliche **QST-Handbuch** (inkl. Grenzgänger und Wohnsitz Ausland) liegt unter `docs/QST-Handbuch.pdf`.

## Wo erfasse ich das?

Sidebar **Mitarbeiter → MA wählen → Tab „Bewilligung QST Bank"**.

Oben im QST-Block siehst du einen **Banner mit dem Status**:

- 🟢 **Grün — „Nicht QST-pflichtig"** mit Begründung (z.B. „C-Ausweis seit 1.1.2020"). Du musst nichts tun.
- 🔵 **Blau — „QST-pflichtig, Erfassung vorhanden"** — alles in Ordnung, Lohnlauf läuft durch.
- 🔴 **Rot — „QST-Pflicht offen"** mit zwei Schnell-Buttons. Hier musst du was tun.

## Wenn rot: zwei Wege

### Weg 1: Schnell den höchsten Tarif setzen

Klick auf **🔴 „Höchsten Tarif erfassen"**. Das System legt sofort den maximalen Tarif an:

- **A0Y** (ledig, 0 Kinder, mit Kirchensteuer) — falls der MA nicht verheiratet ist.
- **C0Y** (verheiratet, Doppelverdiener) — falls er verheiratet ist.

💡 **Warum der höchste Tarif?** In der Schweiz gilt: lieber zu viel abziehen und am Jahresende zurückzahlen, als zu wenig abziehen. Zu wenig ist ein Verstoss gegen die Steuerpflicht. Du kannst den Tarif jederzeit korrigieren wenn du genaue Angaben hast.

### Weg 2: Behörden-Befreiung erfassen

Falls der MA ein **offizielles Schreiben der Steuerbehörde** hat („Ich bin von der QST befreit"):

1. Klick **📄 „Behörden-Befreiung erfassen"**.
2. Lade das Schreiben als PDF hoch (im Dokumenten-Tab) oder wähle ein bereits vorhandenes.
3. Setze **„Gültig ab"** und optional **„Gültig bis"** (leer = unbefristet).
4. Speichern. Status wechselt auf grün.

## Neue QST-Version erfassen — der Tarif ist das RESULTAT

Klick auf **«+ Neue QST-Version»**. Wichtig: **Den Tarif wählst du NICHT
selbst** — OneCrew leitet ihn aus den Stammdaten her und zeigt ihn gross
zuunterst als **Resultat** (z.B. `C2N`) mit Begründung. Die Maske ist von
oben nach unten aufgebaut:

1. **Gültig ab** — deine einzige Pflichteingabe (typisch Eintritt oder
   Änderungsdatum). Ein «Gültig bis» gibt es nicht — es wird beim nächsten
   Versionswechsel automatisch gesetzt.
2. **Zivilstand, seit, Konfession** — nur Anzeige; Pflege in easy@work
   bzw. der MA-Maske.
3. **Wohnsituation** — erscheint nur im Sonderfall (Grenzgänger /
   Wochenaufenthalter, beides automatisch aus den Adressen).
4. **Partner** — nur Anzeige aus dem Familie-Tab; einzig die
   Konkubinat-Frage erscheint bei Ledigen.
5. **Kinder** — Anzahl aus dem Familie-Tab + kinderbezogene Angaben.
6. **Behördenbewilligung & Sonderfälle** — nur mit Verfügung (siehe unten).
7. **Resultat** — der angewendete Tarif, gross und nicht editierbar.

Steuerkanton, Gemeinde und BFS-Nr. setzt der Server automatisch aus der
Wohnadresse — bei Auslandswohnsitz aus der Arbeits-Filiale.

## Die Tarif-Codes

| Code | Wer |
|---|---|
| **A** | Alleinstehend, ohne Kinderziffer (A1–A9 nur mit Behördenverfügung) |
| **B** | Verheiratet, Alleinverdiener |
| **C** | Verheiratet, Doppelverdiener |
| **H** | Alleinerziehend (Kind im gleichen Haushalt) |
| **L** | DE-Grenzgänger, Entsprechung von A |
| **M** | DE-Grenzgänger, Entsprechung von B |
| **N** | DE-Grenzgänger, Entsprechung von C (Doppelverdiener — NICHT «Nebenerwerb») |
| **P** | DE-Grenzgänger, Entsprechung von H (Alleinerziehend) |
| **Q** | DE-Grenzgänger, Entsprechung von G — kommt im normalen Lohnlauf nicht vor |

Hinweis: Die Tarife **D** (heute: z.B. rückvergütete AHV-Beiträge) und
**E** (vereinfachtes Abrechnungsverfahren) kommen im normalen
OneCrew-Lohnlauf nicht vor — E spielt nur indirekt eine Rolle: ist das
Partner-Einkommen nachweislich NUR über Tarif E abgerechnet, gilt B statt C.

Format: `<Tarif><Anzahl Kinder><Y/N>` — `Y` mit Kirchensteuer, `N` ohne.

Beispiele:
- `A0Y` — ledig, 0 Kinder, mit Kirchensteuer
- `C2N` — Doppelverdiener mit 2 Kindern, ohne Kirchensteuer
- `H1Y` — Alleinerziehend mit 1 Kind, mit Kirchensteuer

## Kirchensteuer (Y/N)

Y-fähige Konfessionen: **Röm.-katholisch, Christ-katholisch,
Evang.-reformiert, Israelitische Kultusgemeinschaft**. «Keine» und
«Andere» (muslimisch, orthodox, freikirchlich …) = immer N.
Zusätzlich zählt der **Kanton**: In Kantonen ohne Kirchensteuer in der QST
(z.B. GE, NE, VD, VS, TI) gibt es nie ein Y — massgebend ist immer die
offizielle kantonale Tariftabelle. Die Konfession pflegst du in der
MA-Maske; ändert sie sich, zieht das System das Y/N automatisch nach.

## Welche Kinder zählen?

**Minderjährige** — oder **Volljährige in Erstausbildung** (mit
Ausbildungsnachweis, z.B. laufende Ausbildungszulage), sofern der MA für
den Unterhalt **zur Hauptsache** aufkommt. Bei minderjährigen Kindern im
eigenen Haushalt fragt OneCrew nicht extra nach; bei Volljährigen und
Kindern ausser Haus schon. Unklare Unterhaltssituation bei einem Kind
ausser Haus → das Kind zählt NICHT in die Ziffer (es wird nicht geraten).

- **Tarif H** braucht BEIDES: Kind im gleichen Haushalt UND Unterhalt zur
  Hauptsache. Die H-Ziffer zählt die Haushaltskinder.
- **B/C** zählt dagegen alle berechtigten Kinder (auch ausser Haus).
- **Kind wird 18:** Die Ziffer bleibt für den Geburtstagsmonat; ab dem
  Folgemonat nur weiter mit Erstausbildung.
- **Alimente:** Erhaltene Alimente ändern den Code nicht. Zahlt der MA
  Alimente für Kinder beim Ex-Partner → A0; der Abzug läuft über die
  Steuerbehörde (Tarifkorrektur/NOV), nicht über den Tarif.

## Ehepartner = wichtiger Faktor

Bei verheirateten MA hat der **Ehepartner direkten Einfluss auf den Tarif**:

- **Alleinverdiener** (Partner ohne eigenes Einkommen) → **Tarif B**
- **Ersatztarif bei unklaren Verhältnissen (Art. 19 QSV — wichtig!):** Weist sich die Person über ihre persönlichen Verhältnisse **nicht zuverlässig aus** (Zivilstand unbestimmt, Konfession nicht belegt, Partnerdaten fehlen), gilt von Amtes wegen der Ersatztarif **A0** (Ledige/Unbestimmte) bzw. **C0** (Verheiratete) — **mit Kirchensteuer (Y), soweit der Kanton in der QST eine erhebt** (in Kantonen ohne Kirchensteuer, z.B. GE/NE/VD/VS/TI, also A0N/C0N). In OneCrew: roter Knopf «Ersatztarif erfassen» im QST-Tab; auch der Tarifvorschlag wendet bei fehlender Konfession das Prinzip automatisch an (mit Warnung). Sobald die Unterlagen da sind, den richtigen Tarif als neue Version erfassen — die Person kann bei der Steuerverwaltung bis 31. März des Folgejahres eine Neuberechnung verlangen.
- **Doppelverdiener** (beide arbeiten) → **Tarif C** — Achtung: auch Erwerbseinkommen des Partners im **Ausland** zählt!
- **Ersatzeinkünfte des Partners zählen wie Erwerbseinkommen** (bestätigt Steuerverwaltung, 08/2026): z.B. Taggelder, Teilinvaliditätsrenten, Arbeitslosenentschädigung, Erwerbsersatz/EO bei Schweizer Dienst. **Erwerbseinkommen und Ersatzeinkünfte zählen auch, wenn sie im Ausland erzielt bzw. bezogen werden.** Beispiel Status S: Ehemann leistet in der Ukraine Dienst mit Sold → Frage «Erwerbstätig/Ersatzeinkommen?» = **Ja** → **Tarif C**, nicht B.

Erfass den Ehepartner im **Familie-Tab** mit allen Angaben (Nationalität, Bewilligung, Erwerbstätig-Frage + Arbeitgeber). Der **Tarifvorschlag wertet die Erwerbstätig-Frage automatisch aus** (nicht erwerbstätig → B, erwerbstätig → C, offen → C als sicherer Default). Wenn der Partner Schweizer oder C-Ausweis-Inhaber ist, ist der MA übrigens **gar nicht QST-pflichtig** — siehe oben Bedingung 4 oder 5.

**Partner lebt im Ausland** (z.B. Flüchtlingsfamilien): keine Schweizer Bewilligung nötig — «In der Schweiz lebend» leer lassen und die Auslandsadresse als Zusatzadresse erfassen. Es kommt dann keine Bewilligungs-Warnung.

## Konkubinat (unverheiratetes Paar)

Das Konkubinat interessiert die QST nur über das **gemeinsame Kind**. Regel (AG/ESTV-Praxis): Der Elternteil mit dem **höheren Bruttoeinkommen** (= Hauptunterhalt) erhält **H1**, der andere **A0** — **nie beide H1**. So erfasst du es:

1. Partner im Familie-Tab als Typ **«Konkubinatspartner/in»** anlegen (💞-Badge).
2. Beim Partner: **Erwerbstätig?** beantworten — bei «Ja» erscheint die Frage «**Ist das Einkommen des Konkubinatspartners höher als das des MA?**». Bei «nicht erwerbstätig» gilt automatisch H1 für den MA.
3. Beim **Kind**: «**Gemeinsames Kind mit dem Konkubinatspartner? Ja/Nein**» (steht prominent vor der Adresse; erscheint nur, wenn ein K-Partner erfasst ist).

OneCrew leitet daraus den Tarif ab und **sperrt die Konkubinat-Häkchen im QST-Modal** (Werte kommen automatisch aus dem Familie-Tab). Ist der Tarif korrekt, zeigt die QST-Zeile eine **grüne Erklär-Zeile** («1 gemeinsames Kind · Konkubinat · Partner hat das höhere Einkommen → A0 korrekt»). Bei **gemischten Fällen** (gemeinsame UND nicht-gemeinsame Kinder) macht das System bewusst **keinen Vorschlag** — Meldung «Mit QST-Behörde abklären».

## Weitere Beschäftigungen des MA

Hat der MA **neben Schaub weitere Arbeitgeber**, in der QST-Erfassung «Weitere Beschäftigungen des MA» ankreuzen und die **volle Adresse** des anderen Arbeitgebers (Name, Strasse, PLZ/Ort/Kanton, Land) plus das **Pensum in %** erfassen — das Einkommen wird nicht mehr erfasst. Diese Angaben fliessen automatisch auf die kantonale **QST-Anmeldung**. Der **Tarifbuchstabe bleibt unverändert** — weitere Beschäftigungen wirken nur auf den **satzbestimmenden Lohn** (gemäss KS 45 über Gesamteinkommen, Gesamtbeschäftigungsgrad bzw. Hochrechnung; wenn nichts davon möglich ist, greift die Medianlohn-Regel der ESTV).

## Was wird auf dem Lohnzettel angezeigt?

- Eine eigene **Abzugs-Zeile** „Quellensteuer C2N AG" (Code + Kanton).
- Der Prozentsatz wird **automatisch** aus dem ESTV-Tarif gerechnet.
- Auch wenn das Ergebnis CHF 0.00 ist (kommt bei niedrigem Lohn vor), erscheint die Zeile — damit du sehen kannst, dass es geprüft wurde.

💡 **Mindestbetrag pro Kanton:** kommt aus der **ESTV-Tariftabelle** (nicht fest verdrahtet) — z.B. LU oft 13 CHF, AG CHF 2.00. Auch wenn der Tarif rechnerisch 0 ergibt, kann der Sockel greifen.

## Versionen — wenn sich was ändert

Wenn der MA z.B. heiratet oder ein Kind kriegt:

1. **Neue QST-Version** mit „Gültig ab" = Datum der Änderung (Wirkung ab
   **Folgemonat** — Verhältnisse am Monatsanfang zählen; einzige Ausnahme:
   nimmt der Ehepartner eine Erwerbstätigkeit AUF, gilt C bei ihm sofort,
   beim MA ab Folgemonat).
2. Der alte Eintrag wird **automatisch beendet** auf den Vortag.
3. Das System rechnet ab dem neuen Datum mit dem neuen Tarif.

So bleibt die Historie sauber — frühere Lohnzettel werden mit dem damals gültigen Tarif berechnet, nicht mit dem neuen.

## Verspätet gemeldete Änderungen (rückwirkende Korrektur)

Realität: Eine Heirat oder Scheidung wird oft erst Monate später gemeldet.
So läuft es in OneCrew:

1. Neue Version **rückwirkend** erfassen — bei bereits abgeschlossenen
   Monaten verlangt das System einen **Korrektur-Grund** (z.B. «Heirat
   verspätet gemeldet»).
2. Die **abgeschlossenen Löhne bleiben unverändert**. OneCrew rechnet die
   Differenz pro Monat aus und erzeugt Korrektur-Posten (im QST-Tab als
   ↳-Zeilen unter der Version sichtbar).
3. Die Posten werden **automatisch im nächsten Definitivlauf verrechnet**
   — als eigene Zeile «Quellensteuer-Korrektur (Nachbelastung/Erstattung
   …)» auf dem Lohnzettel.
4. Im Lohnlauf zeigt das ⋯-Menü (ab «provisorisch abgeschlossen») den
   Ausweis **«🔁 QST-Korrekturen»** mit den Summen pro Kanton für die
   kantonale Abrechnung.
5. Betrifft die Korrektur ein **Vorjahr**, läuft sie NICHT über den
   Lohnlauf, sondern direkt über die Steuerverwaltung (im Ausweis separat
   gelistet).

Eine in abgeschlossenen Löhnen verwendete QST-Version ist **eingefroren**
(🔒-Badge) — Änderungen laufen immer über eine neue Version. Und es kann
nur die **jüngste** abgeschlossene Periode wieder geöffnet werden.

## Anmeldung beim Kanton

**Sidebar → HR → QST-Anmeldung** generiert ein PDF, das du dem Kanton schickst, wenn ein MA eintritt oder austritt. Templates sind hinterlegt für SO, AG, ZH, BE. Andere Kantone können wir auf Anfrage hinzufügen. Mehr zum HR-Bereich: [HR-Hub](#hr-hub).

## Lohnlauf-Sperre & Editieren

⚠️ **Wenn QST-pflichtig aber keine Erfassung:**
- Dashboard zeigt rote Card mit MA-Name → Klick führt direkt in den QST-Tab.
- Im Lohnlauf erscheint die MA-Zeile mit ⚠ und Banner „QST-Pflicht offen — Bestätigen gesperrt".
- Bestätigen geht erst, wenn du einen Tarif erfasst oder die Befreiung dokumentiert hast.

**Editieren während dem Lohnlauf:** QST-Tarife bleiben änderbar bis der Definitivlauf **abgeschlossen** ist (weich — siehe [Edit-Sperre](#edit-sperre)). Nach dem Speichern lädt der offene Lohnzettel die Berechnung oft automatisch neu.

**Eintritt mitten im Monat:** Der gültige Tarif am Periodenbeginn / Eintritt wird trotzdem geladen — Kurzperiode und QST greifen zusammen.

## Häufige Fragen

**Was wenn der Wohnkanton wechselt?**
Neuer QST-Eintrag mit dem neuen Steuer-Kanton ab Umzugsdatum. Tarif bleibt eventuell gleich, aber der Prozentsatz ändert je nach Kanton.

**Der MA hat keine Adresse in der Schweiz — was tun?**
Nichts erfassen — die Auslandsadresse kommt inkl. Land aus **easy@work**.
OneCrew erkennt «Wohnsitz Ausland» automatisch, setzt den QST-Kanton auf
den Kanton der Arbeits-Filiale und füllt die Auslands-Felder der
QST-Erfassung vor.

**Was bedeutet „Grenzgänger"?**
Wohnort im Ausland, Arbeitsort in CH, tägliche/regelmässige Rückkehr.
OneCrew erkennt den Auslandswohnsitz **automatisch** aus der easy-Adresse
(kein manuelles Kreuz). Die Grenzgänger-**Sondertarife** gibt es aber NUR
mit Nachweis: DE = Gre-1-Bescheinigung (sonst normale Tarife, max. 60
Nichtrückkehrtage), FR = jährliche Bescheinigung in den 8
Vereinbarungs-Kantonen (sonst normale Tarife), IT = nur «neue» Grenzgänger
ab 17.07.2023 in TI/GR/VS, FL = keine CH-QST bei echtem Grenzgänger. Ein
MA, der unter der Woche in der CH wohnt, ist KEIN Grenzgänger → normale
Tarife.

**Wochenaufenthalter?**
Die Wochenaufenthaltsadresse wird beim MA als **Zusatzadresse (Typ
«Wochenaufenthalt»)** erfasst — das QST-Häkchen setzt sich dann von
selbst. Beim Schweizer Hauptwohnsitz ändert der Wochenaufenthalt am
QST-Kanton NICHTS (Sursee LU + Zimmer Zofingen AG → QST LU). Nur wer im
Ausland wohnt und eine CH-Wochenadresse hat, wird über deren Kanton
besteuert.

**Trennung vom befreienden Ehepartner (CH/C)?**
Schon die **tatsächliche Trennung** beendet die Befreiung — nicht erst die Scheidung! Im MA-Stamm «Getrennt seit» erfassen: ab dem **Folgemonat** wird der MA QST-pflichtig und der Tarifvorschlag wechselt auf A bzw. H (mit Kind im Haushalt). Beispiel: Trennung 15.08. → QST ab 01.09. Erhält der MA später selbst den C-Ausweis, endet die Pflicht wieder.

**Halbfamilie?**
Alleinerziehend mit Kind im gleichen Haushalt → Tarif H. Lebt der MA im Konkubinat, entscheidet das gemeinsame Kind + die Einkommensfrage (siehe Abschnitt «Konkubinat» oben).

## Was OneCrew bewusst NICHT selbst entscheidet

OneCrew automatisiert nur Tarife, die aus den Angaben eindeutig folgen.
Alles andere wird **rot** angezeigt — mit dem genauen Grund und der
Aufforderung, die Situation mit der **QST-Behörde** zu klären. Fehlen nur
Angaben, läuft der Lohn mit dem sicheren Übergangswert weiter (z.B.
Partner-Erwerb unbekannt → C; Gre-1 fehlt → normale Tarife statt
Grenzgängertarif; Zivilstand unbelegt → Ersatztarif). Fachlich komplexe
Fälle entscheidet die Behörde:

- **Alternierende Obhut** (Kind je hälftig bei getrennten Eltern) — nur
  EIN Elternteil bekommt H; übergangsweise A0.
- **Gemischtes Konkubinat** (gemeinsame und nicht-gemeinsame Kinder) —
  übergangsweise A0.
- **Mehrere parallele Arbeitskantone bei Auslandswohnsitz** — kein Kanton
  wird geraten, keine definitive QST-Abrechnung bis zur Klärung.
- **Kinderziffer auf Tarif A (A1–A9)** und **manueller Prozentsatz** —
  nur mit Verfügung der Steuerbehörde (Dokument Pflicht).

Und generell: OneCrew erfindet **nie eigene Steuersätze** — gerechnet
wird ausschliesslich mit den offiziellen ESTV-Tariftabellen.

## Häufige Stolpersteine

- **Tarif setzen vergessen** → Lohnlauf gesperrt. Quick-Fix: roten Banner anklicken, „Höchsten Tarif erfassen".
- **Bewilligung läuft ab und C-Ausweis kommt** → neuer QST-Eintrag mit „Gültig bis" auf dem Tag der C-Bewilligung. Ab dem nächsten Tag ist der MA nicht mehr QST-pflichtig.
- **Falscher Wohnkanton im QST** → kommt aus `employee.canton_code` (Hauptadresse). Wenn der MA umzieht, im Personalien-Tab die Adresse anpassen, dann neuen QST-Eintrag mit dem neuen Kanton.
- **„Mindestbetrag" auf dem Lohnzettel verwirrend** → der Kanton zieht einen Sockel-Betrag (z.B. 13 CHF LU), auch wenn der Tarif eigentlich 0 ergeben würde. Das ist gesetzlich.
