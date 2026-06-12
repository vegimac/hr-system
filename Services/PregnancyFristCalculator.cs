using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Walter-Vorgabe 13.06.2026: zentrale Fristen-Berechnung für das
/// Mutterschafts-Modul. Vorher dupliziert in PregnancyController,
/// PregnancyPdfService und DashboardService — jede Änderung musste
/// an drei Stellen nachgezogen werden.
///
/// Statisch + seiteneffekt-frei (alle Daten als Parameter), damit
/// die Berechnung als Bibliothek aus Controllern UND Services genutzt
/// werden kann ohne DI/Lifetime-Themen.
/// </summary>
public static class PregnancyFristCalculator
{
    /// <summary>
    /// Berechnete Frist + Status für eine Regel-Anwendung auf eine
    /// konkrete Schwangerschaft. `DatumEnde` ist nullable — nur gesetzt
    /// wenn die Regel ein Phasen-Ende definiert (Variante B).
    /// </summary>
    public record Frist(
        DateOnly  Datum,
        DateOnly? DatumEnde,
        string    Status   // "bevorstehend" | "aktiv" | "abgeschlossen"
    );

    /// <summary>
    /// Wählt das Basis-Datum gemäss `code`:
    ///   MELDUNG → Meldedatum
    ///   GEBURT  → Geburtsdatum, Fallback ET
    ///   sonst   → Errechneter Termin (Default)
    /// </summary>
    public static DateOnly ResolveBasis(string? code, EmployeePregnancy p) => code switch
    {
        "MELDUNG" => p.Meldedatum,
        "GEBURT"  => p.Geburtsdatum ?? p.ErrechneterTermin,
        _         => p.ErrechneterTermin
    };

    /// <summary>
    /// Verschiebt das Basis-Datum um |offsetMonate| Monate + |offsetWochen|
    /// Wochen. Richtung "NACHHER" addiert, sonst (VORHER / null) subtrahiert.
    /// Vorzeichen am Offset wird ignoriert (Walter-Konvention: positiver Wert
    /// in der Regel, Richtung steuert das Vorzeichen).
    /// </summary>
    public static DateOnly ApplyOffset(DateOnly basis, string? richtung, int offsetMonate, int offsetWochen)
    {
        int sign   = richtung == "NACHHER" ? 1 : -1;
        int monate = System.Math.Abs(offsetMonate);
        int wochen = System.Math.Abs(offsetWochen);
        return basis.AddMonths(sign * monate).AddDays(sign * wochen * 7);
    }

    /// <summary>
    /// Wendet eine PregnancyRule auf eine Schwangerschaft an. Liefert
    /// Start-Datum + optionales Phasen-Ende + Status.
    ///
    /// Status-Bestimmung:
    ///   • Start &gt; today                              → "bevorstehend"
    ///   • Ende vorhanden UND today &gt; Ende             → "abgeschlossen"
    ///   • Ende vorhanden UND today &lt;= Ende            → "aktiv"
    ///   • Kein Ende, Richtung NACHHER                  → "abgeschlossen"
    ///   • Kein Ende, Richtung VORHER:
    ///       – Geburt vorhanden und 1 Monat danach vorbei → "abgeschlossen"
    ///       – sonst                                       → "aktiv"
    /// </summary>
    public static Frist Calculate(PregnancyRule r, EmployeePregnancy p, DateOnly today)
    {
        // Start
        var basisStart = ResolveBasis(r.BerechnungBasis, p);
        var datum      = ApplyOffset(basisStart, r.Richtung, r.OffsetMonate, r.OffsetWochen);

        // Phasen-Ende (Variante B)
        DateOnly? datumEnde = null;
        bool hasEnde = r.BasisEnde != null
                    || r.OffsetEndeMonate.HasValue
                    || r.OffsetEndeWochen.HasValue;
        if (hasEnde)
        {
            var basisEnde = ResolveBasis(r.BasisEnde ?? r.BerechnungBasis, p);
            var richt     = r.RichtungEnde ?? r.Richtung;
            datumEnde     = ApplyOffset(basisEnde, richt,
                                         r.OffsetEndeMonate ?? 0,
                                         r.OffsetEndeWochen ?? 0);
        }

        // Status
        string status;
        if (datum > today)
        {
            status = "bevorstehend";
        }
        else if (datumEnde.HasValue && today > datumEnde.Value)
        {
            status = "abgeschlossen";
        }
        else if (datumEnde.HasValue)
        {
            status = "aktiv";
        }
        else
        {
            if (r.Richtung == "NACHHER")
            {
                status = "abgeschlossen";
            }
            else
            {
                var geburt = p.Geburtsdatum;
                status = (geburt.HasValue && today > geburt.Value.AddMonths(1))
                    ? "abgeschlossen" : "aktiv";
            }
        }

        return new Frist(datum, datumEnde, status);
    }
}
