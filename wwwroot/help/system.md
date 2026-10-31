# System (Admin)

Die Sidebar **System** ist die Schaltzentrale. Nur Rolle **admin**. Oben sechs Kategorien. Ein Klick zeigt die Karten; die Auswahl bleibt gespeichert. Auf jeder Unterseite führt **«← Zurück»** wieder hierher.

## Lohn-Stammdaten

| Karte | Zweck |
|---|---|
| **Lohnperioden** | Perioden anlegen, Bemerkung, wieder öffnen |
| **Swissdec Lohnpositionen** | Lohnarten mit SV-Häkchen. Die Häkchen steuern die Rechnung. |
| **Kontoplan (Fibu)** | Welche Lohnart auf welches Konto bucht |
| **ELM-Meldungen** | Monatsmeldung Quellensteuer und Statistik erzeugen und prüfen |
| **Lohnmeldungen (ELM)** | Meldungen senden und verfolgen, Zertifikat erneuern |
| **SV-Sätze** | AHV, ALV, NBU, KTG, BVG — versioniert, inkl. Höchstlohn |
| **Lohndaten-Empfänger** | Ausgleichskasse, FAK, Versicherer, Steuerverwaltungen. Die Filiale wählt den Empfänger und setzt ihre Nummern. |
| **Mindestlöhne** | L-GAV-Matrix — siehe [Mindestlöhne](#mindestloehne) |
| **QST-Tarife** | Kantonale Tarifdateien |
| **Familienzulagen-Tarife** | FAK-Sätze Kinder- und Ausbildungszulage |
| **Saldi-Vortrag** | Eröffnungssaldi aus dem alten System, einmalig |
| **Mirus Lohnraster für Migration** | Alte Mirus-Nummern den Lohnpositionen zuordnen |
| **Swissdec-Lohnarten** | Musterstamm 1000–6999, nur Ansicht |
| **Basen-Kontrolle** | SV-Basen aus den Häkchen nachrechnen und mit der Lohnrechnung vergleichen |
| **Absenz-Typen** | Krank, Unfall, Ferien, Mutterschaft — was lohnwirksam ist |
| **Feiertage** | National, pro Kanton oder Filiale. «Wie Sonntag» gilt für den Jugendschutz. «Vorschlag übernehmen» legt das Jahr nach der kantonalen Liste an. |

Den Alltag der Lohnmeldungen findest du auch unter [HR → ELM](#hr-hub).

## Filialen & Benutzer

| Karte | Zweck |
|---|---|
| **Hauptsitze** | Die GmbH: UID, Sitzadresse, welche Filialen dazu gehören. Die Arbeitszeit-Vorlage hängt am Hauptsitz. |
| **Filialen** | Restaurant, Einstellungen, Lohndaten-Empfänger der Filiale — siehe [Filialen](#filialen) |
| **Benutzer** | Zugang, Rolle, Filialen, Unterschrift, zweite Prüfung (Authenticator), Bereiche |

## Verzeichnisse & Vorgaben

| Karte | Zweck |
|---|---|
| **Ärzte** | Für Mutterschutz-Briefe |
| **Behörden** | Stamm für [Lohnabtretungen](#lohnabtretungen): Adresse, IBAN, Sachbearbeiter |
| **PLZ / Gemeinden** | Ortschaften und BFS-Gemeindenummer |
| **Grenzregion** | Ausland-PLZ der ESTV. Der Schweizer Import lässt diese Liste stehen. |
| **Banken** | Name, BIC, BC-Nummer. IBAN-Prüfung darüber. |
| **Nationen** | ISO-Code, Alternativ-Code (z.B. aus Mirus), aktiv ja/nein |
| **Mutterschafts-Regeln** | Gesetzliche Fristen |
| **Dokument-Struktur** | Kategorien und Typen der Personalakte. «Drucken» gibt die Übersicht. |
| **Schulungen & Ausbildungen** | Code, Auffrischung, Frist ab Eintritt, für wen, ob in FRED möglich, ab wann gewarnt wird. Die Gastro-Ausbildung ist keine Schulung — sie kommt als Einstufung aus easy@work. |

## Kommunikation

| Karte | Zweck |
|---|---|
| **E-Mail-Versand** | SMTP, Test-Mail, Rückläufer, Wiedervorlage |
| **SMS (eCall)** | Zugangsdaten und Test-SMS — siehe [SMS](#sms) |
| **Briefpost (WebStamp)** | Die Post frankiert, druckt und verschickt. Solange die Testphase läuft, geht eine Bestellung nur an die Testumgebung der Post. |
| **Moments-Texte** | Vorlagen für Moments, Vertrags-Link und Bewilligungs-SMS |
| **Postfach-Backfill** | Persönliches Postfach für alle aktiven MA anlegen |

## Kontrolle & Datenpflege

| Karte | Zweck |
|---|---|
| **Warnungen** | Welche Dashboard-To-dos an sind, Farbe, Vorlauf, Schwere |
| **Aktivitäts-Log** | Wer hat was geändert — [Aktivitäts-Log](#audit) |
| **Aktive Sitzungen** | Wer gerade angemeldet ist. Benutzer abmelden. |
| **Dokumenten-Audit** | Dateiname nennt eine andere Filiale als der gespeicherte Ort |
| **Vertragshistorie prüfen** | Überlappungen, Lücken, Abschnitte ohne Lohn. Korrigiert wird in easy@work. |
| **Funktionen prüfen** | Funktion abgelaufener Verträge aus dem Lohn zurückrechnen |
| **Datenaufbewahrung** | Stempelzeiten älter als X Jahre löschen (mindestens 5). Lohn und Personal bleiben. |
| **Virenscanner** | Jeder Upload wird geprüft. Status, Funde, bestehende Dokumente nachprüfen. |

## Briefverwaltung

**Austrittsbrief** — Text mit Platzhaltern, Unterzeichner aus dem HR-Team, PDF ansehen. Frankieren geht über die Briefpost und ist der Administration vorbehalten. Im Alltag erreichst du denselben Brief unter [HR → Offboarding](#austritt).

## Faustregeln

- **Versionierte Kataloge** (SV-Sätze, Mindestlöhne): eine vergangene Version bleibt. Die aktuelle änderst du nur, solange noch kein Lohnlauf sie benutzt. Eine neue Version beginnt frühestens morgen.
- Nach grossen Importen: Dashboard-To-dos und einen Test-Lohnzettel prüfen.
- Solange die **Test-Umleitung** bei E-Mail oder SMS aktiv ist, landet nichts beim echten Empfänger.
