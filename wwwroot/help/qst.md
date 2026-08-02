# Quellensteuer (QST)

Quellensteuer ist die Steuer, die du dem MA **direkt vom Lohn** abziehen und an den Kanton abliefern musst. Sie betrifft alle Mitarbeitenden ohne Schweizer Pass oder C-Bewilligung.

## Muss ich QST abziehen?

Das System prüft das **automatisch** für dich. Konkret: **Ein MA ist QST-pflichtig, AUSSER** eine dieser fünf Bedingungen ist erfüllt:

1. 🇨🇭 **Schweizer Bürger** (Nationalität CH)
2. **C-Ausweis** (Niederlassungsbewilligung) — gültig am Stichtag
3. **Behörden-Befreiung** mit Schreiben der Steuerbehörde
4. **Verheiratet mit einem Schweizer** (oder Schweizerin)
5. **Verheiratet mit einem C-Ausweis-Inhaber**

In allen anderen Fällen → **QST-pflichtig**.

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

## Normaler Tarif erfassen (nicht-Schnell)

Klick auf **„+ Neue QST-Erfassung"** ODER bearbeite den vorgeschlagenen Tarif. Du füllst aus:

- **Gültig ab** — typischerweise der Eintritts-Tag oder der Tag der Bewilligungs-Änderung.
- **Steuerkanton** — wo der MA wohnt (Wohnsitz-Kanton, nicht Arbeits-Kanton).
- **QST-Gemeinde + BFS-Nr.** — kommt aus der Hauptadresse.
- **Tarif-Code** — siehe unten.
- **Anzahl Kinder** — abzugsberechtigte Kinder.
- **Kirchensteuer Ja/Nein** — hängt oft an der **Konfession** des MA: ändert sich die Konfession, passt das System das Kirchensteuer-Flag (Y/N) mit an.

Das System bildet daraus automatisch den **QST-Code**, z.B. `B1N` = Verheiratet, 1 Kind, ohne Kirchensteuer.

## Die Tarif-Codes

| Code | Wer |
|---|---|
| **A** | Alleinstehend, ohne Kinder |
| **B** | Verheiratet, Alleinverdiener |
| **C** | Verheiratet, Doppelverdiener |
| **D** | Nebenerwerb |
| **H** | Alleinerziehend |
| **L** | Grenzgänger (alleinstehend) |
| **M** | Grenzgänger verheiratet |
| **N** | Grenzgänger Nebenerwerb |
| **P** | Pauschale |
| **Q** | Grenzgänger alleinerziehend |

Format: `<Tarif><Anzahl Kinder><Y/N>` — `Y` mit Kirchensteuer, `N` ohne.

Beispiele:
- `A0Y` — ledig, 0 Kinder, mit Kirchensteuer
- `C2N` — Doppelverdiener mit 2 Kindern, ohne Kirchensteuer
- `H1Y` — Alleinerziehend mit 1 Kind, mit Kirchensteuer

## Ehepartner = wichtiger Faktor

Bei verheirateten MA hat der **Ehepartner direkten Einfluss auf den Tarif**:

- **Alleinverdiener** (Partner ohne eigenes Einkommen) → **Tarif B**
- **Doppelverdiener** (beide arbeiten) → **Tarif C**

Erfass den Ehepartner im **Familie-Tab** mit allen Angaben (Nationalität, Bewilligung, Einkommens-Zeitraum). Wenn der Partner Schweizer oder C-Ausweis-Inhaber ist, ist der MA übrigens **gar nicht QST-pflichtig** — siehe oben Bedingung 4 oder 5.

## Was wird auf dem Lohnzettel angezeigt?

- Eine eigene **Abzugs-Zeile** „Quellensteuer C2N AG" (Code + Kanton).
- Der Prozentsatz wird **automatisch** aus dem ESTV-Tarif gerechnet.
- Auch wenn das Ergebnis CHF 0.00 ist (kommt bei niedrigem Lohn vor), erscheint die Zeile — damit du sehen kannst, dass es geprüft wurde.

💡 **Mindestbetrag pro Kanton:** kommt aus der **ESTV-Tariftabelle** (nicht fest verdrahtet) — z.B. LU oft 13 CHF, AG CHF 2.00. Auch wenn der Tarif rechnerisch 0 ergibt, kann der Sockel greifen.

## Versionen — wenn sich was ändert

Wenn der MA z.B. heiratet oder ein Kind kriegt:

1. **Neuer QST-Eintrag** mit „Gültig ab" = Datum der Änderung.
2. Der alte Eintrag wird **automatisch beendet** auf den Vortag.
3. Das System rechnet ab dem neuen Datum mit dem neuen Tarif.

So bleibt die Historie sauber — frühere Lohnzettel werden mit dem damals gültigen Tarif berechnet, nicht mit dem neuen.

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
Im QST-Tab unter „Zusätzliche Angaben" → **„Wohnsitz Ausland"** ausfüllen + ISO-Land-Code. Das ist relevant für die Anmeldung beim Kanton.

**Was bedeutet „Grenzgänger"?**
Wohnort im Ausland, Arbeitsort in CH, **tägliche Rückkehr** zum Wohnort. Das System hat eine eigene Checkbox dafür unter „Tarif-relevante Stammdaten".

**Halbfamilie?**
Wenn der MA mit eigenen Kindern alleinerziehend ist aber im Konkubinat lebt (also nicht verheiratet). Beeinflusst den Tarif (H vs. A).

## Häufige Stolpersteine

- **Tarif setzen vergessen** → Lohnlauf gesperrt. Quick-Fix: roten Banner anklicken, „Höchsten Tarif erfassen".
- **Bewilligung läuft ab und C-Ausweis kommt** → neuer QST-Eintrag mit „Gültig bis" auf dem Tag der C-Bewilligung. Ab dem nächsten Tag ist der MA nicht mehr QST-pflichtig.
- **Falscher Wohnkanton im QST** → kommt aus `employee.canton_code` (Hauptadresse). Wenn der MA umzieht, im Personalien-Tab die Adresse anpassen, dann neuen QST-Eintrag mit dem neuen Kanton.
- **„Mindestbetrag" auf dem Lohnzettel verwirrend** → der Kanton zieht einen Sockel-Betrag (z.B. 13 CHF LU), auch wenn der Tarif eigentlich 0 ergeben würde. Das ist gesetzlich.
