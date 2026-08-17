# Verträge

Hier definierst du, **wie viel** ein Mitarbeiter verdient und **wie** er angestellt ist. Pro MA gibt's eine ganze Verlaufs-Geschichte — jede Lohnerhöhung, jede Pensum-Änderung wird als neuer Vertrag erfasst, statt den alten zu überschreiben.

## Wo finde ich was?

Verträge erreichst du so:

- **Mitarbeiter** → MA wählen → Übersicht → Karte **Verträge** (oder Vertrags-Leiste)
- Über die globale Suche **⌘K** → MA → Verträge
- Manche Rollen haben zusätzlich eine eigene Verträge-Seite

Filiale immer über die Sidebar wählen — sonst siehst du die falsche Belegschaft.

## Die vier Vertragsmodelle

| Modell | Wer ist das | Lohn-Logik |
|---|---|---|
| **FLEX** *(früher „UTP")* | Aushilfen, flexible Einsätze | Stundenlohn — wer arbeitet, kriegt |
| **MTP** | Crew mit festem Stunden-Versprechen | Stundenlohn mit garantierten Wochenstunden |
| **FIX** | Crew mit festem Pensum | Monatslohn (z.B. 80 % von 4'500 = 3'600 CHF) |
| **FIX-M** | Restaurant-Manager, Shift-Manager | Festlohn ohne Stundenrechnung |

💡 **Tipp:** Beim easy@work-Import macht das System die Wahl meist automatisch richtig. Du siehst sie im Vertrags-Detail und kannst korrigieren wenn nötig.

## Wie lege ich einen neuen Vertrag an?

**Im MA-Detail oder in der Vertragsliste oben auf „+ Neuer Vertrag" klicken.** Du wählst:

1. **Vertragsbeginn** — Datum ab dem dieser Vertrag gilt.
2. **Vertragsmodell** — FLEX / MTP / FIX / FIX-M.
3. **Funktion** (z.B. Crew, Shift Manager) und **Ausbildung** (z.B. „Ia — ohne Gastronomische Berufslehre"). Daraus rechnet das System sofort den L-GAV-Mindestlohn aus.
4. **Lohn** — Stundenlohn oder Monatslohn, je nach Modell.
5. **Pensum %** (bei FIX und FIX-M) oder **garantierte Wochenstunden /Wo** (bei MTP). Bei FLEX optional der Schalter **«< 8 h»** (L-GAV-Sonderregel neben Max. h/Woche).
6. **Ferien %** (Standard 10.64 für 5 Wochen), **Feiertag %** (2.27 % bei Crew, 0 bei FIX-M), **13. ML %** (fix 8.33 — Pflicht).

Klick auf **Speichern** → das System beendet automatisch den Vorgänger-Vertrag auf den Vortag und legt den neuen ab Vertragsbeginn an. Keine Datums-Lücken.

## Wann lege ich einen neuen Vertrag an, statt den alten zu ändern?

**Faustregel:** Wenn sich etwas ändert, das für den Lohnzettel relevant ist → neuer Vertrag.

| Du willst … | Was tun |
|---|---|
| Lohn erhöhen | Neuen Vertrag ab dem neuen Datum |
| Pensum von 60 % auf 80 % | Neuen Vertrag |
| Vertragsmodell wechseln (z.B. FLEX → MTP) | Neuen Vertrag |
| Funktion ändern (Crew → Shift Manager) | Neuen Vertrag |
| Tippfehler in der Stellenbezeichnung | Bestehenden ändern |
| Probezeit erfasst aber falsch | Bestehenden ändern |

💡 **Warum so streng?** Wenn du den Lohnzettel eines früheren Monats nochmal generieren musst (z.B. nach Reklamation), greift das System auf den damals gültigen Vertrag zu. Hättest du den alten Lohn überschrieben, käme der falsche Wert raus.

## Mindestlohn-Check — live

Während du Lohn und Pensum eingibst, prüft das System **gleichzeitig** gegen den L-GAV-Mindestlohn. Du siehst direkt:

- ✅ **Grün** „Lohn ist in Ordnung" — du bist auf oder über dem Minimum.
- ❌ **Rot** „Mindestlohn unterschritten" — der gewählte Lohn ist zu tief. Du kannst zwar speichern, aber der Lohnlauf wird ihn **blockieren**.

💡 **Filial-Mindestlohn:** In Städten mit eigenem Mindestlohn (z.B. höher als L-GAV) gilt der höhere Wert. Das System nimmt automatisch das Maximum aus L-GAV und Filial-Floor.

## Import-Regeln (Strict-Modus) — easy@work muss sauber sein

Der easy@work-Import **rät nie**. Ist ein **aktiver oder zukünftiger** Vertrag in easy@work fehlerhaft erfasst, erscheint der MA in der Import-Vorschau als roter **CONFLICT** mit Klartext-Meldung — und es wird für ihn **kein Vertrag importiert**, bis easy@work korrigiert ist. Fehlerhafte **abgelaufene** Verträge werden dagegen **still weggelassen** (die Historie liegt im alten Lohnprogramm und in den MA-Dokumenten). Die harten Regeln:

1. **FLEX und MTP haben IMMER Stunden pro WOCHE.** «17 Stunden pro Monat» ist ein Erfassungsfehler → in easy@work die Vertragsart auf «Woche» stellen.
2. **Der Lohn ist Pflicht.** FLEX/MTP brauchen einen Stundenlohn-Tarif, FIX einen Monatslohn-Tarif — fehlt er (oder steht nur der Platzhalter CHF 1.00), wird der Vertrag nicht importiert. **Einzige Ausnahme: FIX-M** (siehe unten).
3. **Verträge dürfen sich nicht überschneiden** — auch nicht um einen Tag: endet ein Vertrag am 1.4., darf der nächste **nicht** am 1.4. beginnen (korrekt: Ende 31.3., Beginn 1.4.). Auch ein noch offener Alt-Vertrag mit Folgevertrag ist ein Fehler.

## Vertraulicher Lohn — nur bei FIX-M

Bei Kader/GF (Modell **FIX-M**) darf der Lohn aus Vertraulichkeit in easy@work fehlen («Pas de taux»). Nur dort gilt:

1. Der Sync legt den FIX-M-Vertrag **ohne Lohn** an und markiert ihn als «lokal gepflegt» (easy@work-Override).
2. **Lohn direkt im OneCrew-Vertrag erfassen** — MA-Detail → Vertrags-Leiste → «Bearbeiten», Mindestlohn-Prüfung greift wie überall.
3. Der Sync fasst Modell und erfassten Lohn danach **nie** mehr an. Bis der Lohn erfasst ist, sperrt die **«Lohnsumme fehlt»-Sperre** den Lohnlauf.

Liefert easy@work später wieder einen echten Lohn für den Vertrag, löst der Sync den Override automatisch und easy@work ist wieder führend.

## Arbeitsvertrag dem MA aufs Handy schicken

Im **Mitarbeiter-Detail** hat jeder Vertrag in der Vertrags-Leiste die Aktionen **Bearbeiten · Anschauen · SMS · Link ⊘** (Drucken und Herunterladen direkt im Vorschaufenster von „Anschauen“):

- **SMS** — erzeugt einen persönlichen Link (14 Tage gültig) und schickt ihn nach einer Rückfrage per SMS an die Mobilnummer des MA. Der MA sieht eine neutrale Seite mit Button „Arbeitsvertrag öffnen"; erst der Klick lädt das PDF. Beim erneuten Senden werden alte Links automatisch ungültig.
- **Link ⊘** — widerruft alle aktiven Links dieses Vertrags sofort (z.B. wenn eine SMS an die falsche Nummer ging).

Details, Sicherheit und Test-Umleitung: [SMS & Vertrags-Link](#sms).

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
Bei der Vertragserfassung das Feld **„Probezeit (Monate)"** ausfüllen, z.B. 3 — daraus berechnet das System **Probezeit bis**. Fehlt die Probezeit nach easy@work-Import: Admin → easy@work → **«Probezeiten nachführen»** ([easy@work](#easyatwork)). Das Gespräch führst du unter **MA Formulare → Probezeit** (Datum + Protokoll verknüpfen). Details: [Mitarbeiter](#mitarbeiter).

**Wann darf ich den Vertrag noch ändern?**
Solange der Definitivlauf der Periode **nicht abgeschlossen** ist (Akonto und «provisorisch» sperren Verträge nicht). Danach nur noch Folge-Vertrag ab neuem Datum — siehe [Edit-Sperre](#edit-sperre).

**Was ist das „garantierte Monat"-Feld bei MTP?**
Eine reine Anzeige zur Plausibilitäts-Prüfung: `garantierte Wochenstunden × Stundenlohn × 52 / 12`. So siehst du sofort, ob das ungefähr dem Mindesteinkommen entspricht das du dem MA versprochen hast.

## Häufige Stolpersteine

- **„Mindestlohn unterschritten"** beim Speichern → entweder Lohn anheben oder Ausbildungs-Level/Funktion prüfen (vielleicht ist „Mit Berufslehre" gemeint statt „Ohne"). Mehr: [Mindestlöhne](#mindestloehne).
- **Vertrag bleibt nicht gespeichert** → meist fehlt das Pflichtfeld „13. ML %". Das ist immer 8.33 — kannst du einfach eintippen.
- **Der MA hat zwei aktive Verträge gleichzeitig** → kommt vor wenn easy@work-Import nicht sauber lief. Den älteren beenden (Enddatum auf gestern), dann passt es wieder.
- **Vertrag lässt sich nicht ändern** → oft schon im Lohn verwendet oder [Edit-Sperre](#edit-sperre) — dann Folge-Vertrag ab neuem Datum.
