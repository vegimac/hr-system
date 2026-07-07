namespace HrSystem.Models;

/// <summary>
/// Globale Konfiguration der Dashboard-/ToDo-Warnungen (Walter-Vorgabe 06.07.2026).
/// Pro Warn-Kategorie (= der Category-String aus DashboardService) lässt sich
/// einstellen: an/aus, Vorlauf-Fenster in Tagen, Eskalations-Schwelle in Tagen
/// und der Schweregrad (Basis + eskaliert). GLOBAL, nicht pro Filiale.
///
/// Die Seed-Werte in Program.cs / der Migration bilden das heutige Verhalten
/// 1:1 ab — fehlt eine Zeile, defaultet der DashboardService auf enabled +
/// den im Code hinterlegten Fallback.
/// </summary>
public class DashboardWarningConfig
{
    public int Id { get; set; }

    /// <summary>Exakter Category-String aus DashboardService, z.B. «permit_expiring». UNIQUE.</summary>
    public string Category { get; set; } = "";

    /// <summary>Deutscher Anzeigename für die Verwaltungs-UI.</summary>
    public string Label { get; set; } = "";

    /// <summary>Warnung aktiv? false = der Block wird im DashboardService übersprungen.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Vorlauf-Fenster in Tagen (z.B. 60 = warnen ab 60 Tagen vor Ablauf).
    /// NULL = zustandsbasierte Warnung ohne Zeitfenster.</summary>
    public int? WarnDays { get; set; }

    /// <summary>Ab diesem Rest-Tageswert (≤) wird der eskalierte Schweregrad verwendet.
    /// NULL = keine Eskalation.</summary>
    public int? EscalateDays { get; set; }

    /// <summary>Basis-Schweregrad: critical / warning / info.</summary>
    public string SeverityBase { get; set; } = "warning";

    /// <summary>Eskalierter Schweregrad. NULL = keine Eskalation (dann gilt immer Basis).</summary>
    public string? SeverityEscalated { get; set; }

    /// <summary>true = Datums-/Vorlauf-basiert (UI zeigt das Vorlauf-Feld);
    /// false = reine Zustands-Warnung ohne Vorlauf.</summary>
    public bool IsDateBased { get; set; }

    /// <summary>Sortier-Reihenfolge in der Verwaltungs-Tabelle.</summary>
    public int SortOrder { get; set; }
}
