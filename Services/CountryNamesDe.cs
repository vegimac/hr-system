// Walter-Vorgabe 13.06.2026: ENTFERNT — Quelle der Nationalitäten-Namen ist
// jetzt ausschließlich die DB-Tabelle `nationality.name_de`. Fehlt eine
// Nation, wird sie in den Systemeinstellungen ergänzt.
//
// Diese Datei kann gelöscht werden (`rm Services/CountryNamesDe.cs`) —
// sie steht hier nur, damit das C#-Projekt während der Übergangsphase
// keinen Compile-Fehler wirft, falls noch `using HrSystem.Services` mit
// einer alten Referenz auf `CountryNamesDe.Resolve` existieren würde.
//
// Es gibt aktuell KEINEN Aufruf mehr auf diese Klasse im gesamten
// Projekt — `grep -r "CountryNamesDe" .` bestätigt das.

namespace HrSystem.Services;

// Bewusst leer.
