# Lohnabtretungen & Behörden

Wenn ein Teil des Lohns an eine Behörde geht (Pfändung, Sozialhilfe, ORS …), erfasst du das als **Lohnabtretung** beim Mitarbeiter. Die Behörde und ihre Sachbearbeiter pflegst du einmal zentral — danach wählst du sie nur noch aus.

## Wo arbeite ich?

| Ort | Was |
|---|---|
| **MA → Tab «Zulagen Abzüge Abtretung BVG»** | Abtretung pro Mitarbeiter erfassen / bearbeiten |
| **HR → Lohnabtretungen** | Übersicht aller Abtretungen (Filiale = Sidebar, inkl. «Alle Filialen») |
| **System → Behörden** | Behörden-Stamm: Adresse, IBAN, Sachbearbeiter, Kontoinhaber für DTA |

💡 In der HR-Liste: **Klick auf die Zeile** (oder die Behörden-Spalte / →) springt direkt zum MA, öffnet den Zulagen-Tab und das Bearbeiten-Fenster der gewählten Abtretung. **👁 Doku** öffnet nur die Vorschau.

## Behörde anlegen (einmal)

**System → Behörden → Neue Behörde** (nur Admin):

1. Name, Typ, Adresse, PLZ, Ort
2. **Bankverbindung** (IBAN) — wird für den Behörden-DTA gebraucht
3. Optional: **Kontoinhaber = andere Behörde** — wenn auf dem QR-Einzahlungsschein ein anderer Name steht als die fallführende Stelle  
   Beispiel: Fall bei **ORS SERVICE AG Burgdorf**, Zahlung aber an **ORS Service AG Zürich** → bei Burgdorf unter Bankverbindung die Behörde Zürich als Kontoinhaber wählen. Im DTA steht dann Name + Adresse von Zürich, die Abtretung bleibt bei Burgdorf.
4. **Sachbearbeiter** im Behörden-Fenster pflegen (Name, Telefon, E-Mail) — pro Behörde mehrere möglich

Der frühere «zentrale Behörden-Kontakt» entfällt: Korrespondenz und Lohnausweis-Mail laufen über den **gewählten Sachbearbeiter**.

## Lohnabtretung erfassen (pro MA)

1. Mitarbeiter öffnen → Tab **Zulagen Abzüge Abtretung BVG**
2. **«Lohnabtretung erfassen»**
3. Ausfüllen:

| Feld | Hinweis |
|---|---|
| **Behörde** | aus dem Stamm |
| **Sachbearbeiter** | aus dem Stamm dieser Behörde (für Mail / Kontakt in der Liste) |
| **Bezeichnung** | z.B. Lohnpfändung |
| **Freibetrag** | was dem MA mindestens bleibt |
| **Zielbetrag** | optional — 0 = unbegrenzt bis Widerruf |
| **Gültig ab / bis** | Versionierung; bei Edit-Sperre gilt das übliche «ab Datum» |
| **Referenz/Aktenzeichen** | für Korrespondenz (oft AHV oder Aktenzeichen des Amts) |
| **Zahlungsreferenz** | QR 27-stellig oder SCOR/RF — landet im DTA |
| **Bemerkung** | beim **Neu-Erfassen** automatisch vorausgefüllt: **Name, Vorname, AHV** — kannst du anpassen |
| **Lohnausweis an Behörde** | optional: beim Definitiv-Abschluss geht ein Download-Link per Mail an den **Sachbearbeiter** (mit E-Mail). Der Lohnzettel an den MA bleibt unverändert. |

4. Speichern → danach erscheint der Dialog **Dokument verknüpfen** (wie bei Bewilligungen).

## Dokument-Pflicht (wichtig!)

Ohne verknüpften Beleg (Pfändungsurkunde, Verfügung …) ist die Abtretung **im Lohnlauf unwirksam** — kein Abzug, keine Behörden-Zahlung.

| Anzeige | Bedeutung |
|---|---|
| 🔴 **Dokument-Pflicht** / **🔗 fehlt** | kein Beleg → Abtretung greift nicht |
| 🟢 **Doku ✓** / **👁 Doku** | Beleg verknüpft → wirksam (sofern Gültigkeit und Zielbetrag ok) |

Verknüpfen: Button **🔗 Doku verknüpfen** → bestehendes MA-Dokument wählen oder neu hochladen.  
Aufheben: über das ⋮-Menü der Abtretung (dann wieder unwirksam).

## Was passiert im Lohnlauf?

- Abzug und Behörden-Anteil erscheinen auf dem Lohnzettel nur, wenn **Dokument verknüpft** und die Abtretung gültig ist.
- Der **Behörden-DTA** (pain.001) zahlt an die Behörde — Empfängername kommt aus dem **Kontoinhaber** (eigene Behörde oder verknüpfte andere Behörde), Betrag und Zahlungsreferenz aus der Abtretung.
- Beträge ≤ 0 erscheinen nicht im DTA (wie bei MA-Auszahlungen).

Mehr zum Ablauf Akonto/Definitiv: [Lohnlauf](#lohnlauf).

## Übersicht in HR

**HR → Lohnabtretungen**:

- Filter über den **globalen Filial-Selektor** (auch «Alle Filialen»)
- Spalten: MA, Behörde, Sachbearbeiter, Telefon, E-Mail, Freibetrag, Doku-Status
- Banner zeigt, wie viele Einträge noch **ohne Dokument** sind
- Sortierung nach Vorname (wie überall)

## Checkliste für einen neuen Fall

1. ☐ Behörde im Stamm vorhanden (IBAN, ggf. Kontoinhaber-Behörde)?
2. ☐ Sachbearbeiter mit Telefon/E-Mail erfasst?
3. ☐ Abtretung beim MA angelegt (Referenz + Zahlungsreferenz)?
4. ☐ **Beleg-Dokument verknüpft**?
5. ☐ Optional: «Lohnausweis an Behörde» nur mit SB inkl. E-Mail?
6. ☐ Im nächsten Lohnlauf auf dem Zettel und im Behörden-DTA prüfen

## Häufige Stolpersteine

- **Abtretung erscheint nicht auf dem Lohnzettel** → fehlt das Dokument? Oder Gültig-ab noch in der Zukunft / Zielbetrag schon erreicht?
- **Falscher Name auf dem DTA** → bei der Behörde den **Kontoinhaber** (andere Behörde) prüfen, nicht den Fall-Namen ändern.
- **Lohnausweis-Mail kommt nicht an** → Sachbearbeiter ohne E-Mail oder Häkchen nicht gesetzt.
- **Kann nicht speichern** → [Edit-Sperre](#edit-sperre): Gültig-ab muss nach der letzten eingefrorenen Periode liegen.
- **Dokument-Vorschau 404** → im Programm über **👁 Doku** öffnen (interne Preview-Route), nicht über eine alte URL.

## Verwandte Themen

- Dokumente ablegen: [Dokumente & Posteingang](#dokumente)
- Behörden-Stamm: [System (Admin)](#system)
- HR-Einstieg: [HR-Bereich](#hr-hub)
- Lohnlauf / DTA: [Lohnlauf](#lohnlauf)
