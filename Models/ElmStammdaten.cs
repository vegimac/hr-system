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
    public string? UvgKundenNummer { get; set; }
    public string? UvgVertragsNummer { get; set; }

    // ── UVGZ ──────────────────────────────────────────────────────────
    public string? UvgzVersicherer { get; set; }
    public string? UvgzKundenNummer { get; set; }
    public string? UvgzVertragsNummer { get; set; }

    // ── KTG ───────────────────────────────────────────────────────────
    public string? KtgVersicherer { get; set; }
    public string? KtgKundenNummer { get; set; }
    public string? KtgVertragsNummer { get; set; }

    // ── BVG ───────────────────────────────────────────────────────────
    public string? BvgVersicherer { get; set; }
    public string? BvgKundenNummer { get; set; }
    public string? BvgVertragsNummer { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; }
}
