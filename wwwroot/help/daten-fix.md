# Daten-Fix (Admin)

Unter **System → Daten-Fix** korrigierst du bewusst Stammdaten-Fehler. Nur Rolle **admin** — kein Alltagswerkzeug für GF/HR.

## Personalnummer ändern

Typischer Fall: In easy@work wurde die Nummer korrigiert, der Einzel-Sync am MA findet die Person nicht mehr, weil OneCrew noch die alte Nummer hat.

**Ablauf**

1. Aktuelle Nummer in OneCrew eingeben → **Prüfen**
2. Neue Nummer (wie in easy@work) eintragen → erneut **Prüfen**
3. Checks lesen (frei? Filial-Präfix?) → **Nummer umsetzen**

**Was geändert wird**

- `employee.employee_number`
- Postfach-Username (`app_user`), falls vorhanden

**Was bewusst nicht geändert wird**

- easy@work-ID (bleibt der Anker für Stempelzeiten)
- kein Nummern-Alias
- keine Lohn-/Absenz-/Dokument-Zeilen (die hängen an der MA-ID)

**Prüfungen**

| Check | Wirkung |
|---|---|
| Nummer frei | blockiert, wenn ein anderer MA sie schon hat |
| Filial-Präfix | Warnung; Admin kann bewusst überschreiben |
| Alias bei anderem MA | nur Hinweis |

Nach dem Fix: am MA «easy@work synchronisieren» — sollte wieder greifen.
