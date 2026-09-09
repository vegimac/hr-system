namespace HrSystem.Models;

/// <summary>
/// Versicherungs-Code eines Mitarbeiters pro Versicherungsart (Walter 07.09.2026,
/// Swissdec-Lösungscodes): UVG «A/B/P…», UVGZ und KTG «10/11/12», BVG «11/21/22/K2010».
/// Versioniert (ab/bis) wie die BVG-Zusatz-Mitgliedschaft. KEIN Eintrag = der MA
/// bekommt automatisch die Standard-Lösung der SV-Sätze (Zeile ohne Code bzw.
/// Zeile mit «Standard»-Häkchen) — erfasst wird nur, wer abweicht.
/// Beim BVG kann statt Prozent ein fester Monatsbeitrag AN/AG hinterlegt werden
/// (Vorgabe der Pensionskasse); dann ersetzt er die Prozentrechnung.
/// </summary>
public class EmployeeVersicherungCode
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    /// <summary>UVG | UVGZ | KTG | BVG | AHV (nur Code SONDERFALL = AHV/ALV-Sonderfall, nicht beitragspflichtig)</summary>
    public string Art { get; set; } = "UVG";

    /// <summary>
    /// AHV/ALV-Sonderfall (Swissdec «AHV-ALV-Sonderfall», Walter 09.09.2026): Person ist
    /// nicht AHV/IV/EO- und ALV-beitragspflichtig (z.B. Versicherung im Ausland mit
    /// A1-Bescheinigung, Entsandte). UVG/UVGZ/KTG/BVG/QST laufen normal; der Lohn wird
    /// in der AHV-Meldung als «AHV-Open/ALV-Open» ausgewiesen, keine FAK-Meldung.
    /// </summary>
    public const string ArtAhv = "AHV";
    public const string CodeSonderfall = "SONDERFALL";
    public bool IstAhvSonderfall => Art == ArtAhv && string.Equals(Code, CodeSonderfall, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lösungscode (SocialInsuranceRate.LoesungsCode). Leer erlaubt, wenn nur ein Fixbetrag (BVG) erfasst wird.</summary>
    public string? Code { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    /// <summary>BVG: fester Beitrag AN pro Monat (CHF) — ersetzt die Prozentrechnung.</summary>
    public decimal? BeitragFixAn { get; set; }
    /// <summary>BVG: fester Beitrag AG pro Monat (CHF).</summary>
    public decimal? BeitragFixAg { get; set; }

    /// <summary>BVG-Eintrittsgrund (Swissdec EntryReason): entryCompany | interruptionOfEmployment | others …</summary>
    public string? BvgEintrittsgrund { get; set; }
    /// <summary>BVG: bei Eintritt voll arbeitsfähig? (Swissdec FullyFitForWork / NotFullyFitForWork)</summary>
    public bool? BvgVollArbeitsfaehig { get; set; }
    /// <summary>BVG: manuell an die PK gemeldete Jahresbasis (CHF) — ersetzt die berechnete Basis in der Meldung.</summary>
    public decimal? BvgBasisManuell { get; set; }

    public string? Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }

    public Employee? Employee { get; set; }

    public bool GiltAm(DateOnly d) => ValidFrom <= d && (ValidTo == null || ValidTo >= d);

    /// <summary>Versicherungsart → SV-Satz-Code(s) in social_insurance_rate.</summary>
    public static string[] SvCodesFuer(string art) => art switch
    {
        "UVG"  => new[] { "NBUV", "BUV" },
        "UVGZ" => new[] { "UVGZ" },
        "KTG"  => new[] { "KTG" },
        "BVG"  => new[] { "BVG" },
        "AHV"  => new[] { "AHV", "ALV", "ALVZ" },
        _      => Array.Empty<string>(),
    };

    /// <summary>SV-Satz-Code → Versicherungsart (NULL = keine Lösungslogik, z.B. AHV/ALV/ALVZ/QST).</summary>
    public static string? ArtFuerSvCode(string? svCode) => svCode?.ToUpperInvariant() switch
    {
        "NBUV" or "BUV" => "UVG",
        "UVGZ"          => "UVGZ",
        "KTG"           => "KTG",
        "BVG"           => "BVG",
        _               => null,
    };
}
