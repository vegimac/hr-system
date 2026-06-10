# Verträge

Hier definierst du, **wie viel** ein Mitarbeiter verdient und **wie** er angestellt ist. Pro MA gibt's eine ganze Verlaufs-Geschichte — jede Lohnerhöhung, jede Pensum-Änderung wird als neuer Vertrag erfasst, statt den alten zu überschreiben.

## Wo finde ich was?

Sidebar **Verträge** → links die MA-Liste, rechts der gewählte Vertrag mit allen Details.

Wenn du oben in der Sidebar eine Filiale gewählt hast, siehst du nur MA dieser Filiale. „Alle Filialen" → ganze Belegschaft.

## Die vier Vertragsmodelle

| Modell | Wer ist das | Lohn-Logik |
|---|---|---|
| **UTP** | Aushilfen, flexible Einsätze | Stundenlohn — wer arbeitet, kriegt |
| **MTP** | Crew mit festem Stunden-Versprechen | Stundenlohn mit garantierten Wochenstunden |
| **FIX** | Crew mit festem Pensum | Monatslohn (z.B. 80 % von 4'500 = 3'600 CHF) |
| **FIX-M** | Restaurant-Manager, Shift-Manager | Festlohn ohne Stundenrechnung |

💡 **Tipp:** Beim easy@work-Import macht das System die Wahl meist automatisch richtig. Du siehst sie im Vertrags-Detail und kannst korrigieren wenn nötig.

## Wie lege ich einen neuen Vertrag an?

**Im MA-Detail oder in der Vertragsliste oben auf „+ Neuer Vertrag" klicken.** Du wählst:

1. **Vertragsbeginn** — Datum ab dem dieser Vertrag gilt.
2. **Vertragsmodell** — UTP / MTP / FIX / FIX-M.
3. **Funktion** (z.B. Crew, Shift Manager) und **Ausbildung** (z.B. „Ia — ohne Gastronomische Berufslehre"). Daraus rechnet das System sofort den L-GAV-Mindestlohn aus.
4. **Lohn** — Stundenlohn oder Monatslohn, je nach Modell.
5. **Pensum** (bei FIX und FIX-M) oder **garantierte Wochenstunden** (bei MTP).
6. **Ferien %** (Standard 10.64 für 5 Wochen), **Feiertag %** (2.27 % bei Crew, 0 bei FIX-M), **13. ML %** (fix 8.33 — Pflicht).

Klick auf **Speichern** → das System beendet automatisch den Vorgänger-Vertrag auf den Vortag und legt den neuen ab Vertragsbeginn an. Keine Datums-Lücken.

## Wann lege ich einen neuen Vertrag an, statt den alten zu ändern?

**Faustregel:** Wenn sich etwas ändert, das für den Lohnzettel relevant ist → neuer Vertrag.

| Du willst … | Was tun |
|---|---|
| Lohn erhöhen | Neuen Vertrag ab dem neuen Datum |
| Pensum von 60 % auf 80 % | Neuen Vertrag |
| Vertragsmodell wechseln (z.B. UTP → MTP) | Neuen Vertrag |
| Funktion ändern (Crew → Shift Manager) | Neuen Vertrag |
| Tippfehler in der Stellenbezeichnung | Bestehenden ändern |
| Probezeit erfasst aber falsch | Bestehenden ändern |

💡 **Warum so streng?** Wenn du den Lohnzettel eines früheren Monats nochmal generieren musst (z.B. nach Reklamation), greift das System auf den damals gültigen Vertrag zu. Hättest du den alten Lohn überschrieben, käme der falsche Wert raus.

## Mindestlohn-Check — live

Während du Lohn und Pensum eingibst, prüft das System **gleichzeitig** gegen den L-GAV-Mindestlohn. Du siehst direkt:

- ✅ **Grün** „Lohn ist in Ordnung" — du bist auf oder über dem Minimum.
- ❌ **Rot** „Mindestlohn unterschritten" — der gewählte Lohn ist zu tief. Du kannst zwar speichern, aber der Lohnlauf wird ihn **blockieren**.

💡 **Filial-Mindestlohn:** In Städten mit eigenem Mindestlohn (z.B. höher als L-GAV) gilt der höhere Wert. Das System nimmt automatisch das Maximum aus L-GAV und Filial-Floor.

## Wie verlängere ich einen befristeten Vertrag?

Im Vertrag rechts das **Enddatum** entfernen oder anpassen. Wenn der Vertrag dadurch unbefristet wird, wandelt das System den **Vertragstyp** automatisch auf „unbefristet" um.

## Wie löse ich einen Mitarbeiter ab?

Zwei Wege, je nach Situation:

**Reguläres Vertragsende:** Vertrag öffnen, Enddatum setzen, speichern. Lohn läuft bis zum Enddatum.

**Austritt aus dem Unternehmen:** Im MA-Detail oben den **🛑-Button**. Das ist der richtige Weg — das System rechnet automatisch Kurzperiode + Ferien-Restanspruch und ordnet das Enddatum allen aktiven Verträgen zu.

## Mindestlohn-Anpassung — wenn der L-GAV steigt

Wenn der L-GAV per Stichtag (z.B. 1.1.2027) steigt und einige deiner Verträge dann unter dem neuen Minimum liegen, zeigt das Dashboard einen **Warn-Banner**. Klick darauf → du siehst alle betroffenen MA mit dem nötigen neuen Lohn und kannst **„Alle automatisch anpassen"** klicken.

Optional kann das System dem MA eine **Postfach-Mitteilung** schicken: „Dein Stundenlohn steigt per 1.1.2027 auf CHF 22.50."

## Was passiert mit alten Verträgen?

Sie bleiben **forever** in der Historie. Das System rechnet auch fünf Jahre später einen Lohnzettel mit dem damals gültigen Vertrag korrekt nach.

In der Vertragsliste siehst du:

- 🟢 **Aktiv** — heutiges Datum liegt zwischen Beginn und Ende.
- ⚪ **Vergangenheit** — Enddatum bereits erreicht.
- ⏳ **Zukunft** — Vertragsbeginn liegt in der Zukunft (z.B. Lohnerhöhung ab 1.1.).

## Häufige Fragen

**Kann ich einen Vertrag löschen?**
Nur einen, der noch nie in einem Lohnlauf verwendet wurde. Sobald ein Vertrag mindestens einmal in einer Periode gelaufen ist, ist er **gesperrt** — du kannst nur noch einen neuen Vertrag drüber legen.

**Was bedeutet die rote Pille „🔒 In Lohn verwendet"?**
Der Vertrag wurde in mindestens einer abgeschlossenen oder laufenden Periode benutzt. Direktes Editieren ist gesperrt. Lösung: einen Folge-Vertrag mit den gewünschten Werten anlegen.

**Wo erfasse ich den Stellen-Wechsel auf Shift Manager?**
Neuer Vertrag mit Funktion „Shift Leader 1–6 Mt." (oder „Shift Leader 7+ Mt." je nach Erfahrung). Vertragsmodell wird automatisch auf FIX-M gesetzt — Shift Manager sind immer Management.

**Mein MA macht eine Probezeit. Wie?**
Bei der Vertragserfassung das Feld **„Probezeit (Monate)"** ausfüllen, z.B. 3. Das ist informativ — die Probezeit hat keine direkte Wirkung auf den Lohn.

**Was ist das „garantierte Monat"-Feld bei MTP?**
Eine reine Anzeige zur Plausibilitäts-Prüfung: `garantierte Wochenstunden × Stundenlohn × 52 / 12`. So siehst du sofort, ob das ungefähr dem Mindesteinkommen entspricht das du dem MA versprochen hast.

## Häufige Stolpersteine

- **„Mindestlohn unterschritten"** beim Speichern → entweder Lohn anheben oder Ausbildungs-Level/Funktion prüfen (vielleicht ist „Mit Berufslehre" gemeint statt „Ohne").
- **Vertrag bleibt nicht gespeichert** → meist fehlt das Pflichtfeld „13. ML %". Das ist immer 8.33 — kannst du einfach eintippen.
- **Der MA hat zwei aktive Verträge gleichzeitig** → kommt vor wenn easy@work-Import nicht sauber lief. Den älteren beenden (Enddatum auf gestern), dann passt es wieder.
