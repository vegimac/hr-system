# Edit-Sperre (Lohnlauf-Schutz)

Sobald ein Lohnlauf einer Periode «eingefroren» ist, dürfen bestimmte Daten **von niemandem** mehr geändert werden — auch nicht von Admin. So bleibt der Lohnzettel konsistent. Es gibt **zwei Stärken**, je nach Datentyp.

## Zwei Sperr-Stufen

| Stufe | Wann gesperrt? | Betrifft u.a. |
|---|---|---|
| **Hart** | Akonto bei HR / HR-freigegeben / ausbezahlt **oder** Definitiv provisorisch / abgeschlossen | Wiederkehrende Zulagen/Abzüge, Bankkonten (gültige Versionen), Lohnabtretungen, viele periodenbezogene Zulagen |
| **Weich** | Erst wenn Definitiv wirklich **abgeschlossen** (DTA / final) | **Absenzen**, **Verträge**, **Quellensteuer**, **Kinderzulagen / Familienzulagen** |

Während der HR-Kontrolle (`provisorisch_abgeschlossen`) und im gesamten Akonto-Strang bleiben Absenzen, Verträge, QST und Kinderzulagen also noch korrigierbar — genau dafür ist die Kontrolle da.

Akonto **IN_BEARBEITUNG_GF** (GF bereitet vor) sperrt **nicht** — Stempel-/Absenz-Korrekturen sind noch möglich.

## Was ist immer nur lesen?

**Stempelzeiten** — Quelle = easy@work, unabhängig von der Sperre. Korrigieren nur in easy@work.

## Was ist noch erlaubt?

- Periode noch offen bzw. Akonto noch bei GF → vorbereiten und korrigieren
- Bei **weicher** Sperre: solange Definitiv nicht final abgeschlossen ist → Absenzen/QST/Verträge/Kinderzulagen der Periode noch änderbar
- Neue Versionen **ab dem ersten erlaubten Tag** (gelber Banner zeigt oft dieses Datum)
- Reine Anzeige, Dokumente ablegen, Moments, Formulare …

## Wie hebe ich die harte Sperre auf?

Nur bewusst über Reset — mit Audit-Spur:

| Lauf | Wer | Wo | Grenze |
|---|---|---|---|
| **Akonto zurücksetzen** | Admin | Lohnperioden → ↺ Akonto zurücksetzen | nur bis Auszahlungsdatum |
| **Definitiv wieder öffnen** | Admin | Lohnperioden → Wieder eröffnen | nur bis Auszahlungsdatum |

Vorher musst du bestätigen, dass der **DTA bei der Bank storniert** ist — sonst droht Doppelzahlung.

## Fehlermeldung 409 LOHN_EDIT_LOCKED

„Speichern" schlägt fehl mit Hinweis auf die Sperre. Lösung: entweder warten / neue Version ab erlaubtem Datum — oder Admin setzt den Lauf zurück (bei harter Sperre).
