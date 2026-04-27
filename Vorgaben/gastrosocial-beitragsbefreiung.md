# GastroSocial BVG – Beitragsbefreiung bei Arbeitsunfähigkeit

Zusammenfassung des GastroSocial-Merkblatts **1003-dt** (Stand 11/2025). Dient als
Referenz für die spätere Implementierung der BVG-Beitragsbefreiungs-Logik im HR-System.

Quelle: [`1003-dt_Arbeitsunfaehigkeit.pdf`](./1003-dt_Arbeitsunfaehigkeit.pdf) (im selben Ordner).

> ⚠️ **Abgrenzung:** Dieses Dokument betrifft die BVG-Pensionskasse (GastroSocial)
> und ist **nicht** identisch mit dem KTG/UVG-Taggeldlohn. Wir haben dafür zwei
> getrennte Durchschnittsbasen:
>
> | Zweck | Basis |
> |---|---|
> | **KTG/UVG-Taggeld** (Tab „KTG/UVG" beim Mitarbeiter, aktuell implementiert) | 6 Monate Bruttolohn |
> | **BVG-Beitragsbefreiung** (dieses Merkblatt, noch nicht implementiert) | 12 Monate bei variablem Lohn |

---

## Wartefrist – 3 Monate

Während der dreimonatigen Wartefrist zahlen **Arbeitgeber und Mitarbeiter die
BVG-Beiträge im bisherigen Umfang weiter**. Erst danach tritt die Beitragsbefreiung
(50 % oder 100 %) in Kraft. Der Beginn der Wartefrist richtet sich nach dem Tag,
an dem die Arbeitsunfähigkeit eintritt.

### Wann beginnt die Wartefrist?

- **AU-Beginn 1.–15. des Monats** → der ganze Monat zählt als arbeitsunfähig.
  Wartefrist startet **am 1. dieses Monats**.
- **AU-Beginn ab dem 16. des Monats** → der Monat zählt noch als gesund.
  Wartefrist startet **am 1. des Folgemonats**.

### Beispiele aus dem Merkblatt

| Fall | AU-Beginn | Wartefrist-Zeitraum | Beitragsbefreiung ab |
|---|---|---|---|
| 1 | 10. Januar | 1. Januar – 31. März | 1. April |
| 2 | 18. Januar | 1. Februar – 30. April | 1. Mai |

---

## Ende der Beitragsbefreiung

Spiegelbildliche Regel zum AU-Ende:

- **AU-Ende 1.–15. des Monats** → der ganze Monat zählt als gesund.
  Beiträge sind **ab dem 1. dieses Monats** wieder geschuldet.
- **AU-Ende ab dem 16. des Monats** → der Monat zählt noch als arbeitsunfähig.
  Beiträge erst **ab dem 1. des Folgemonats**.

---

## Rahmenbedingungen

- **Maximale Dauer** der Beitragsbefreiung: **720 Tage** (dreimonatige Wartefrist
  eingeschlossen).
- Bei **jeder neuen AU** startet die Wartefrist neu.
- Die Beitragsbefreiung endet spätestens mit:
  - Wiedereintritt der Arbeitsfähigkeit
  - Ende des Arbeitsverhältnisses
  - Erreichen des ordentlichen Rücktrittsalters (Referenzalter)

---

## Meldepflichtiger Lohn während der Wartefrist

### Fixer Monatslohn
Während der ersten 3 Monate bleibt **derselbe Lohn wie vor AU-Beginn**
beitragspflichtig.

### Variabler Monatslohn
Während der ersten 3 Monate gilt der **durchschnittliche Lohn der letzten
12 Monate mit normaler Beschäftigung**.

**Minimum-Anhebung:** Monate unter **CHF 2'520.–** werden auf CHF 2'520.– angehoben
(= CHF 2'205.– Koordinationsabzug + CHF 315.– minimal versicherter Lohn).

### Rechenbeispiel aus dem Merkblatt

Unfall am 11. Juli 2026, 100 % AU bis 8. Dezember 2026.

Letzte 12 Monate (Juli 2025 – Juni 2026):

| Monat | Lohn |
|---|---|
| Jul 25 | 2'879.– |
| Aug 25 | 2'665.– |
| Sep 25 | 3'177.– |
| Okt 25 | 3'065.– |
| Nov 25 | 3'040.– |
| Dez 25 | 3'220.– |
| Jan 26 | 4'005.– |
| Feb 26 | 3'090.– |
| Mär 26 | 2'787.– |
| Apr 26 | 2'815.– |
| Mai 26 | 2'730.– |
| Jun 26 | 2'623.– |
| **Ø**  | **CHF 3'008.–** |

Anwendung auf die Beitragsperiode:

| Monat 2026 | Status | Geltender Lohn |
|---|---|---|
| Jul (1. Monat Wartefrist) | beitragspflichtig | 3'008.– |
| Aug (2. Monat Wartefrist) | beitragspflichtig | 3'008.– |
| Sep (3. Monat Wartefrist) | beitragspflichtig | 3'008.– |
| Okt | beitragsfrei | — |
| Nov | beitragsfrei | — |
| Dez (AU endet 8. Dez → Monat als gesund) | beitragspflichtig | 3'008.– |

---

## Teilweise Arbeitsunfähigkeit

Abgestufte Beitragsbefreiung nach GastroSocial-Tabelle (nicht nach effektivem Grad):

| Grad der Arbeitsunfähigkeit | Beitragsbefreiter Lohnanteil |
|---|---|
| 0 – 49 %   | 0 %   |
| 50 – 69 %  | 50 %  |
| 70 – 100 % | 100 % |

### Beispiel

- Lohn während Wartefrist: CHF 3'140.–
- Effektive AU: 60 %
- GastroSocial-geltende AU: **50 %**
- Meldepflichtiger Lohn nach Wartefrist: **50 % von 3'140.– = CHF 1'570.–**

### Lohnabzug mit prozentualem Koordinationsabzug

Bei Teil-AU ist der **Koordinationsabzug prozentual** anzuwenden – die reguläre
Lohnabzugstabelle oder der Lohnabzugsrechner greift hier **nicht**.

| Posten | Betrag |
|---|---|
| Meldepflichtiger Lohn während Wartefrist | CHF 3'140.– |
| Meldepflichtiger Lohn nach Wartefrist (50 %) | CHF 1'570.– |
| ./. Koordinationsabzug (50 % von 2'205.–) | CHF 1'102.50 |
| **Beitragspflichtiger Lohn** | **CHF 467.50** |
| Lohnabzug ab 25 J. (Vorsorgeplan Uno Basis, 7 %) | CHF 32.75 |

### Umsatzlöhne
Bei Umsatzlöhnen ist der **effektiv erzielte Lohn für 50 % Tätigkeit** zu
deklarieren (nicht der hochgerechnete 100 %-Lohn mal 0.5).

---

## Invalidität

Ab Beginn der Invalidenrente wird der **Koordinationsabzug an den
Invaliditätsgrad angepasst**.

---

## Lohnprogramme

Rechnet das Lohnprogramm die Lohnarten für Krankheit/Unfall auf 100 % hoch
(z. B. Krankentaggeld wird als voller Lohn ausgewiesen), dann werden während
der ersten 3 Monate die **hochgerechneten Löhne** für die BVG-Meldung
verwendet.

---

## Meldung und Belege

- **GastroSocial-Lohnliste:** Abschnitt B ausfüllen.
- **Andere Lohnmeldungen:** Hinweis erfassen, z. B. „seit 23. September 2026
  50 % arbeitsunfähig".
- **Arztzeugnisse / Taggeldabrechnungen:** für Personen, die länger als 3 Monate
  AU sind, werden Arztzeugnisse bzw. Taggeldabrechnungen über die gesamte
  Zeitperiode benötigt.

---

## Implementierungs-Skizze für später

Wenn wir das im HR-System abbilden wollen, brauchen wir grob folgende Bausteine:

### Datenmodell

```csharp
public class ArbeitsunfaehigkeitsFall {
    int      Id;
    int      EmployeeId;
    int      CompanyProfileId;
    DateOnly AuVon;           // Datum AU-Beginn
    DateOnly? AuBis;          // Datum AU-Ende (null = laufend)
    int      GradProzent;     // 0–100
    string   Grund;           // "Krankheit" | "Unfall"
    // berechnete Felder
    DateOnly WartefristVon;   // 1. des Monats je nach 1.–15. vs. ab 16.
    DateOnly WartefristBis;   // + 3 Monate
    DateOnly BefreiungVon;    // = WartefristBis + 1 Tag, Monatsanfang
    DateOnly? BefreiungBis;   // je nach AU-Ende-Stichtag
    int      GeltenderGradStufe; // 0 | 50 | 100 %
}

public class Lohn12MonatsDurchschnitt {
    int     EmployeeId;
    int     CompanyProfileId;
    int     BerechnetPerYear;
    int     BerechnetPerMonth;
    decimal DurchschnittBrutto;  // mit Minimum-Anhebung auf 2'520
    string  DetailJson;          // [{jahr, monat, brutto_original, brutto_angehoben}]
}
```

### Regeln als Pseudo-Code

```csharp
DateOnly WartefristStart(DateOnly auVon) =>
    auVon.Day <= 15
        ? new DateOnly(auVon.Year, auVon.Month, 1)
        : new DateOnly(auVon.Year, auVon.Month, 1).AddMonths(1);

DateOnly BeitragsbefreiungAb(DateOnly wartefristStart) =>
    wartefristStart.AddMonths(3);

int GeltenderGrad(int effektiverGrad) => effektiverGrad switch {
    < 50  => 0,
    < 70  => 50,
    _     => 100
};

decimal MindestbetragAngehoben(decimal lohn) => Math.Max(lohn, 2520m);
```

### BVG-Abzug im Calculate-Endpoint

Pro Periode prüfen, ob der Mitarbeiter in einer Beitragsbefreiungs-Phase ist, und
den BVG-Abzug entsprechend auf 0, 50 % oder 100 % des normalen Abzugs skalieren
(inkl. Koordinationsabzug prozentual).

### 720-Tage-Deckel

Pro AU-Fall `TageBefreiungGenutzt` mitzählen; bei Überschreitung 720 Tage endet
die Befreiung automatisch.
