# Absenzen, Kalender & Mirus-Import

Absenzen (Krankheit, Unfall, Ferien, Militär …) beeinflussen den Lohn. Stempelzeiten kommen aus easy@work — Absenzen erfassst du in OneCrew (oder importierst sie aus dem Mirus-Dienstplan).

## Absenzen beim Mitarbeiter

**Mitarbeiter → Tab „Absenzen / KTG/UVG"**

1. Neue Absenz: Typ wählen, Von/Bis, ggf. Ausfall-%
2. Speichern — Stunden werden berechnet
3. Bei Krankheit/Unfall siehst du **Karenz** und den **KTG/UVG-Tagessatz** (Regeln A/B)

💡 Solange der Lohnlauf der Periode gesperrt ist, kannst du Absenzen dieser Periode nicht mehr ändern — siehe [Edit-Sperre](#edit-sperre).

## Absenzkalender

**Mitarbeiter → Restaurant Admin → Absenzkalender** (oder direkt die Seite)

Monatsübersicht der Filiale: wer ist wann abwesend, wo knirscht es personell. Praktisch für die Planung im Restaurant.

## Mirus Absenz-Import (Dienstplan)

Wenn Absenzen noch im **Mirus-Dienstplan** (XLS) stehen:

1. Sidebar → **Mirus Absenz Import** (je nach Rolle)
2. Datei wählen, Filiale prüfen
3. Vorschau: welche Zeilen werden zu Krankheit / Unfall / Ferien …
4. Commit nur wenn die Zuordnung stimmt

Danach erscheinen die Einträge im Absenzen-Tab des MA und im Lohnlauf.

## Was gehört wohin?

| Thema | Ort |
|---|---|
| Krankheit, Unfall, Ferien, Militär … | Absenzen-Tab |
| Gestempelte Arbeitszeit | Stempelzeiten-Tab (nur lesen → easy@work) |
| Wann jemand grundsätzlich einsetzbar ist | Verfügbarkeit-Tab (aus easy@work) |
| Auswertung über viele MA | HR → Absenz-Auswertung |

## Häufige Fragen

**Darf ich Stempelzeiten korrigieren?**
Nein. Nur in **easy@work**, dann Sync abwarten oder MA synchronisieren.

**Ferien bei MTP/FIX — was passiert im Lohn?**
Sollstunden / Festlohn werden gekürzt; Auszahlung je nach Modell aus dem Ferien-Pott bzw. als Tage-Saldo. Details im [Lohnlauf](#lohnlauf).
