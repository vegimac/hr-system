# Filialen

Jede Filiale hat Stammdaten und Einstellungen, die den Lohnlauf und Formulare steuern.

**Wo:** System → **Filialen** → Filiale wählen (Admin). Viele Werte siehst du auch im Alltag indirekt (Akonto-%, Max-Stunden …).

## Tabs

### Stammdaten
Adresse, AHV/BVG-Nummern, Telefon (wichtig für Formulare), Lohnausweis-Optionen (Verpflegung, Transport …), Standard-Probezeit in Monaten, Firmen-Bankverbindung für DTA, SSL-Nummern pro QST-Kanton.

### Unterzeichner
Namen für Formulare (AG-Vertreter etc.). Die **Unterschrift** auf dem PDF kommt trotzdem vom **eingeloggten User**.

### Einstellungen (die wichtigsten)

| Einstellung | Wirkung |
|---|---|
| **Normale / max. Wochenstunden** | Planung + Warnung im Stempelzeiten-Tab |
| **Nachtstunden-Fenster** | Wann zählt eine Stunde als Nacht |
| **Karenz** | Krankheitstage vor Taggeld |
| **L-GAV-Beitrag** | Automatischer Beitrag im Lohn |
| **13. ML Auszahlungsmonate** | Wann der 13. bei MTP/FIX ausgezahlt wird |
| **Akonto % FIX / Stunden** | Wie viel % Vorschuss (FIX vs. FLEX/MTP) |
| **Akonto-Termine** | Kalender für die Akonto-Läufe |
| **Ferien % / Feiertag %** | Defaults (Anzeige / Vertrag) |
| **Kommunaler Mindestlohn** | Jahreslohn der Stadt → System rechnet Monat/Std.; gilt als Floor neben L-GAV |

💡 **Kommunaler Mindestlohn:** Du erfasst den **Jahreslohn**. Monat = Jahr/13, Stundenlohn = Jahr / 52 / Wochenstunden der Filiale. Jugendliche nur wenn „gilt für Jugend" aktiv.

## Arbeitszeit

Eigener Tab im Filial-Detail.

- **Feiertage** sind nicht mehr hier: sie gelten für alle Filialen gemeinsam und werden in Systemeinstellungen → Lohn-Stammdaten → **Feiertage** gepflegt (nur Admin).
- **Regeln für Stempel-Verstösse:** Die Vorlage kommt vom Hauptsitz (⋮ beim Hauptsitz → «Arbeitszeit-Regeln (Vorlage)», nur Admin). Hier setzt du nur, was in dieser Filiale anders ist: Regel ein/aus, Grenzwerte, eigene Erklärung. Leer = wie Vorlage (grau angezeigt).

## Tipps

- Änderungen an Akonto-% wirken auf den **nächsten** Akonto-Lauf.
- „Auf alle Filialen kopieren" (wo vorhanden) mit Vorsicht — prüfe pro Filiale nach.
- Filial-Telefon und Bank müssen stimmen, bevor du Behördenformulare oder DTA machst.
