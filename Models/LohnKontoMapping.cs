namespace HrSystem.Models;

/// <summary>
/// Kontoplan / Lohnart→Konten-Mapping (Walter-Vorgabe 22.05.2026).
/// Übernommen 1:1 aus dem Mirus/McDonald's-Schweiz-Buchungsschema (export.xls).
///
/// Schlüssel: Position (Mirus-Lohnart) × SubPosition × Kostenstelle (= unser
/// Vertragsmodell: 100 Crew Fix/MTP, 200 Crew Flex/UTP, 300 Management, 400 Gerant).
/// Liefert pro Lohnart das Soll-/Gegenkonto + Buchungstext. Treibt den
/// Fibu-Journal-Generator (Etappe 2) und später den Abacus-Export.
///
/// `IsVormonat` = die Rückstellungs-Auflösungszeile (Vormonat) — bei Ferien/
/// Feiertagen wird der Vormonat aufgelöst und der aktuelle Monat neu gebildet.
/// </summary>
public class LohnKontoMapping
{
    public int Id { get; set; }

    /// <summary>Mirus-Lohnart-Code (z.B. 10 Bruttolohn, 195 Ferien/Feiertag, 510 ALV, 2010 RST 13.).</summary>
    public int Position { get; set; }

    /// <summary>SubPosition (NULL = keine).</summary>
    public int? SubPosition { get; set; }

    /// <summary>Soll-Konto.</summary>
    public string Fibukonto { get; set; } = "";

    /// <summary>Haben-/Gegenkonto.</summary>
    public string Gegenkonto { get; set; } = "";

    /// <summary>Kostenstellen-Nr (100/200/300/400) — NULL = gruppenunabhängig (z.B. SV, QST, Nettolohn).</summary>
    public string? KostenstelleNr { get; set; }

    public string? KostenstelleName { get; set; }

    public string Bezeichnung { get; set; } = "";

    /// <summary>true = Rückstellungs-Auflösung des Vormonats (RST → Aufwand).</summary>
    public bool IsVormonat { get; set; }

    public bool SollBuchung { get; set; } = true;

    public int SortOrder { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}
