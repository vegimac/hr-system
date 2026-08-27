namespace HrSystem.Models;

/// <summary>
/// Swissdec-E3 (Walter 28.08.2026): Stammdaten der MELDEEINHEIT
/// (= Rechtseinheit Schaub Restaurants GmbH) für die elektronische
/// Lohnmeldung — EINE Zeile, nicht pro Filiale. Quelle für UID,
/// Ausgleichskassen-Nummern und Versicherer-/Vertragsnummern im
/// DeclareAnnualSalary-XML (ersetzt die E2-Platzhalter).
/// </summary>
public class ElmStammdaten
{
    public int Id { get; set; }

    /// <summary>UID im Format CHE-XXX.XXX.XXX.</summary>
    public string? Uid { get; set; }

    // ── AHV/ALV — Ausgleichskasse (GastroSocial) ──────────────────────
    public string? AkName { get; set; }
    /// <summary>Kassen-Nummer (Addressee, z.B. «002.000»).</summary>
    public string? AkKassenNummer { get; set; }
    /// <summary>Abrechnungs-/Mitglieder-Nummer bei der Kasse.</summary>
    public string? AkAbrechnungsNummer { get; set; }

    // ── FAK ───────────────────────────────────────────────────────────
    public string? FakKassenNummer { get; set; }
    public string? FakAbrechnungsNummer { get; set; }

    // ── UVG ───────────────────────────────────────────────────────────
    public string? UvgVersicherer { get; set; }
    /// <summary>Versicherer-Nummer (Swissdec-Adressierung, z.B. Swica «S122»).</summary>
    public string? UvgVersichererNummer { get; set; }
    public string? UvgKundenNummer { get; set; }
    public string? UvgVertragsNummer { get; set; }
    /// <summary>UID des UVG-Versicherers (fürs XML: UVG-LAA-Insurance).</summary>
    public string? UvgUid { get; set; }
    public DateOnly? UvgVersichertSeit { get; set; }

    // ── UVGZ ──────────────────────────────────────────────────────────
    public string? UvgzVersicherer { get; set; }
    /// <summary>Versicherer-Nummer (Swissdec-Adressierung, z.B. Swica «S122»).</summary>
    public string? UvgzVersichererNummer { get; set; }
    public string? UvgzKundenNummer { get; set; }
    public string? UvgzVertragsNummer { get; set; }

    // ── KTG ───────────────────────────────────────────────────────────
    public string? KtgVersicherer { get; set; }
    /// <summary>Versicherer-Nummer (Swissdec-Adressierung, z.B. Swica «S122»).</summary>
    public string? KtgVersichererNummer { get; set; }
    public string? KtgKundenNummer { get; set; }
    public string? KtgVertragsNummer { get; set; }

    // ── BVG ───────────────────────────────────────────────────────────
    public string? BvgVersicherer { get; set; }
    /// <summary>Versicherer-Nummer (Swissdec-Adressierung, z.B. Swica «S122»).</summary>
    public string? BvgVersichererNummer { get; set; }
    public string? BvgKundenNummer { get; set; }
    public string? BvgVertragsNummer { get; set; }
    /// <summary>UID der BVG-Vorsorgeeinrichtung (fürs XML: BVG-LPP-Insurance).</summary>
    public string? BvgUid { get; set; }
    public DateOnly? BvgVersichertSeit { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; }
}
