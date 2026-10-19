namespace HrSystem.Models;

/// <summary>
/// Kontoplan / Lohnart→Konten-Mapping.
/// Seit Walter 19.10.2026: Schlüssel = Swissdec-Lohnart (<see cref="LohnartCode"/>)
/// × Vertrags-Gruppe (<see cref="Modell"/> CREW/FLEX/MGMT) → Soll/Gegen/MWST.
/// Kostenstelle ist Legacy (nur Fallback); Abacus bekommt nur die Konten.
/// Position/SubPosition bleiben für Alt-Journal/SV-Brücke (Mirus-Nummern).
/// </summary>
public class LohnKontoMapping
{
    public int Id { get; set; }

    /// <summary>Mirus-Lohnart-Code (Legacy-Brücke, z.B. 10 Bruttolohn, 510 ALV).</summary>
    public int Position { get; set; }

    public int? SubPosition { get; set; }

    /// <summary>Swissdec-Lohnart (z.B. 1005, 1160, 5010) — primärer UI-Schlüssel.</summary>
    public string? LohnartCode { get; set; }

    /// <summary>CREW (MTP+FIX) / FLEX / MGMT (FIX-M) / null = modellunabhängig.</summary>
    public string? Modell { get; set; }

    public string Fibukonto { get; set; } = "";
    public string Gegenkonto { get; set; } = "";

    /// <summary>Legacy — nicht mehr pflegen; Journal nutzt <see cref="Modell"/>.</summary>
    public string? KostenstelleNr { get; set; }
    public string? KostenstelleName { get; set; }

    public string Bezeichnung { get; set; } = "";
    public bool IsVormonat { get; set; }

    public string? MwstKonto { get; set; }
    public string? MwstCode { get; set; }
    public decimal? MwstProzent { get; set; }

    public bool SollBuchung { get; set; } = true;
    public int SortOrder { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}
