# Edit-Sperre (Lohnlauf-Schutz)

Sobald ein Akonto- oder Definitivlauf einer Periode **bei HR** ist oder **abgeschlossen**, dürfen lohnrelevante Daten dieser Periode **von niemandem** mehr geändert werden — auch nicht von Admin. So bleibt der Lohnzettel konsistent.

## Was ist gesperrt?

Typischerweise (für Daten **in** der gesperrten Periode):

- Absenzen
- Einmalige Zulagen / Abzüge der Periode
- Versionierte Dinge mit „gültig ab" in der Vergangenheit (Verträge, Bank, QST, Bewilligung, wiederkehrende Zulagen …) — Änderung nur über **neue Version ab späterem Datum**

**Stempelzeiten** sind immer nur lesbar (Quelle = easy@work) — unabhängig von der Sperre.

## Was ist noch erlaubt?

- Periode noch **offen** bzw. Akonto noch **in Bearbeitung GF** → GF darf vorbereiten und korrigieren
- Neue Verträge / Bewilligungen **ab dem ersten erlaubten Tag** (meist 1. des Folgemonats)
- Reine Anzeige, Dokumente ablegen, Moments, Formulare …

Das System zeigt oft einen gelben Banner mit dem **ersten erlaubten Datum**.

## Wie hebe ich die Sperre auf?

Nur bewusst über Reset — mit Audit-Spur:

| Lauf | Wer | Wo | Grenze |
|---|---|---|---|
| **Akonto zurücksetzen** | Admin | Lohnperioden → ↺ Akonto zurücksetzen | nur bis Auszahlungsdatum |
| **Definitiv wieder öffnen** | Admin | Lohnperioden → Wieder eröffnen | nur bis Auszahlungsdatum |

Vorher musst du bestätigen, dass der **DTA bei der Bank storniert** ist — sonst droht Doppelzahlung.

## Fehlermeldung 409 LOHN_EDIT_LOCKED

„Speichern" schlägt fehl mit Hinweis auf die Sperre. Lösung: entweder warten bis nächste Periode, neue Version ab erlaubtem Datum — oder Admin setzt den Lauf zurück.
