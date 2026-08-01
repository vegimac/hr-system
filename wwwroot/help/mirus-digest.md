# Mirus-Änderungsmail (Sachbearbeiter)

Solange die Lohnverarbeitung noch in **Mirus** läuft, pflegt OneCrew die Stammdaten — Mirus braucht morgens eine Liste der lohnkritischen Änderungen. Dafür gibt es die **Mirus-Änderungsmail**.

## Wann geht die Mail raus?

- **Mo–Fr um 06:00** (Europe/Zurich)
- **Kein Versand am Wochenende**
- **Montag** deckt Freitag 06:00 bis Montag 06:00 ab (alles, was über das Wochenende passiert ist)
- Wenn es **keine** lohnkritischen Änderungen gibt → **keine Mail** (kein Spam)

## Wer bekommt sie?

Nur Benutzer mit dem Flag **«Mirus-Änderungsmail»** in der Benutzerverwaltung (aktive E-Mail, nicht die Rolle `employee`).

- **Admin / Superuser** → alle Filialen
- **Andere Rollen** → nur Filialen aus ihrer Filial-Zuordnung

## Was ist enthalten?

Alles, was für die Mirus-Lohnverarbeitung relevant ist — gruppiert nach Filiale und **Personalnummer** (ohne Vor-/Nachnamen in der Mail):

| Bereich | Beispiele |
|---|---|
| **Stammdaten / Adresse** | Strasse, PLZ, Ort, Wohnkanton, Zivilstand (+ seit), Ledigname, Nationalität, ZEMIS, Heimatort, Kündigung am/per, QST-Behörden-Befreiung, Pass/C-Ausweis-Dokument |
| **Weitere Adressen** | Korrespondenz, Sozialamt, Ferienwohnung … (neu / geändert / gelöscht) |
| **Vertrag** | Modell, Lohn, Pensum, Garantie-Stunden, Funktion, Vertragsbeginn/-ende, Filiale, Ausbildung |
| **Bank** | IBAN, Aufteilung, Gültigkeit |
| **Bewilligung** | Neue oder geänderte Aufenthaltsbewilligung + Dokument |
| **Quellensteuer** | Tarif, Kanton, Gültigkeit |
| **Familie** | Ehepartner/Kinder inkl. Bewilligung, Ausweis-Dokument, Nationalität, ZEMIS (QST-relevant) |
| **Familienzulagen** | Betrag / Zeitraum |
| **Zulagen / Abzüge / Abtretung / BVG-Zusatz** | wiederkehrend und periodenbezogen |

## Was ist bewusst NICHT enthalten?

- **Stempelzeiten** und **Absenzen** — die laufen schon automatisch nach Mirus
- Vor-/Nachname des MA in der Mail (nur Personalnummer)

## Vorschau / manuell auslösen

In der **Benutzerverwaltung** (Admin): Buttons zur **Vorschau** und zum Testversand an dich selbst. Filiale = Sidebar-Selektor links oben.

## Wenn etwas fehlt

1. Wurde die Änderung wirklich gespeichert? (Aktivitäts-Log prüfen)
2. Hat der Empfänger das Flag «Mirus-Änderungsmail»?
3. Liegt der MA in einer Filiale, die der Empfänger sehen darf?

Bei Unklarheiten: Admin → Aktivitäts-Log (Entität z.B. `Employment`, `EmployeeQuellensteuer`, `EmployeeAddress`).
