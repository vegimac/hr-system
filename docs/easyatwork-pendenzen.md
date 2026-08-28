# easy@work — Pendenzenliste fürs Treffen (Herbst 2026)

Gesammelte Themen aus dem laufenden Betrieb OneCrew ↔ easy@work.
Neue Punkte immer hier ergänzen (Datum + Kurzbegründung), damit fürs
Treffen mit easy@work alles an einem Ort liegt.

Stand: 28.08.2026 · Ansprechpartner OneCrew: Walter Schaub

---

## 1 · Adressen: zweite Adresse «Wochenaufenthalt» (28.08.2026)

**Ist:** easy@work führt pro MA genau EINE Adresse. Unsere Konvention
(fix seit 28.08.2026): **die easy-Adresse ist IMMER der Hauptwohnsitz**
— daran hängt bei uns der QST-Kanton. Die Wochenaufenthaltsadresse
(Studenten-Filialen Zürich/Bern!) pflegen wir vorerst nur in OneCrew
als Zusatzadresse.

**Wunsch an easy@work:** zweites, klar gekennzeichnetes Adressfeld
«Wochenaufenthaltsadresse» (oder Adress-Typisierung), damit die
Trennung Hauptwohnsitz/Aufenthaltsort auch in easy sauber ist und
niemand versehentlich das Wochenzimmer als Hauptadresse einträgt.

## 2 · Custom-Field-Intervalle: «to» inkonsistent (bekannt seit 26.07.2026)

Das Enddatum von Custom-Field-Intervallen (z.B. Nachtarbeit-Arztzeugnis)
speichert easy inkonsistent — mal inklusives Bis (Zürich-00:00 des
Tages), mal exklusives Mitternacht (= nächster Tag 00:00). Wir rechnen
das Ende deshalb selbst und prüfen easy nur noch dagegen
(`EawDateUtil.IntervalEndMatchesSoll`).

**Wunsch:** verbindliche, dokumentierte Semantik (inklusiv ODER
exklusiv, aber einheitlich).

## 3 · Absenzen: `_business_dates[]`-Semantik verbindlich klären (14.08.2026)

Support-Auskunft: `_business_dates[]` = lokales Datum mit 00:00:00.
Vor dem Bau des Absenz-Syncs wollen wir das an echten Beispielen
gegenprüfen — am Treffen bestätigen lassen (inkl. Verhalten bei
mehrtägigen Absenzen über Monatswechsel und Zeitzonen-Rändern).

## 4 · Verfügbarkeiten: Bulk-Endpunkt fehlt (09.07.2026)

`availabilities` + `…/days` gibt es nur pro MA (1+N Calls). Für den
Massen-Sync über alle Filialen bräuchten wir einen Bulk-Endpunkt
(alle Verfügbarkeiten eines Customers in einem Call) oder zumindest
ein `updated_since`-Filter.

## 5 · Notfallkontakte: Cross-Customer-Problematik (26.08.2026)

`emergency_contacts` liefert nur die Kontakte des jeweiligen Customers.
MA, die in mehreren Filialen (= Customers) arbeiten, haben ihren
Kontakt oft nur in EINEM Customer — Löschungen dürfen wir deshalb
nicht nachziehen. **Wunsch:** Kontakte auf MA-Ebene statt pro Customer,
oder ein Merge-sicherer Abgleich.

## 6 · UTC-Timestamps ohne Zeitzonen-Kennzeichnung (Dauerthema)

Alle `from`/`to`-Strings kommen als «yyyy-MM-dd HH:mm:ss» OHNE
Zeitzonen-Angabe, sind aber UTC. Mitternacht Zürich = 22:00/23:00 UTC
des Vortags — häufige Fehlerquelle. **Wunsch:** ISO-8601 mit Offset
(«2026-08-28T00:00:00+02:00») oder zumindest explizite Doku pro Feld.

---

*Erledigte Punkte nicht löschen, sondern durchstreichen + Datum der
Erledigung notieren — die Historie ist fürs nächste Treffen Gold wert.*
