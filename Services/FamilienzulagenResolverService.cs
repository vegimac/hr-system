using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Walter-Vorgabe 28.05.2026: zentrale Auflösung der Familienzulagen-Beträge
/// pro Periode. Nutzer wählt nur noch die Kategorie (KZ/AZ/GZ/AdoptZ) — die
/// Höhe (Satz 1 vs. Satz 2 nach Alter, jeweils aktuell gültiger Tarif für die
/// Filiale) wird zur Laufzeit berechnet.
///
/// Wird vom PayrollCalculationEngine (Lohnlauf, pro Periode) UND vom
/// FamilyMemberAllowancesController (Frontend-Live-Vorschau im Erfassungs-
/// modal) genutzt — eine Quelle, kein Auseinanderlaufen.
/// </summary>
public class FamilienzulagenResolverService
{
    private readonly AppDbContext _db;
    public FamilienzulagenResolverService(AppDbContext db) { _db = db; }

    /// <summary>
    /// Ergebnis der Tarif-Auflösung: Betrag + Beschreibung welcher Satz greift.
    /// Wenn KEIN Tarif gefunden oder Kategorie nicht im Tarif hinterlegt:
    /// Amount = null, Description erklärt den Grund.
    /// </summary>
    public record ResolveResult(
        decimal? Amount,
        string   AllowanceType,    // "KZ" | "AZ" | "GZ" | "AdoptZ"
        string   SatzLabel,        // "Satz 1" | "Satz 2 ab 12 J." | "Pauschal"
        string   Description,      // Klartext für UI ("215.00 CHF/Mt, Satz 1, Tarif AG ab 01.01.2026")
        int?     TarifId,
        DateOnly? TarifValidFrom
    );

    /// <summary>
    /// Tarif zur Filiale + Stichtag laden.
    /// </summary>
    public async Task<FamilienzulagenTarif?> GetTarifAsync(string? kantonCode, DateOnly stichtag)
    {
        if (string.IsNullOrWhiteSpace(kantonCode)) return null;
        var code = kantonCode.Trim().ToUpper();
        return await _db.FamilienzulagenTarife
            .Where(t => t.IsActive
                     && t.KantonCode == code
                     && t.ValidFrom <= stichtag
                     && (t.ValidTo == null || t.ValidTo >= stichtag))
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Reine Rechnung — kein DB-Zugriff. Liefert den am Stichtag gültigen
    /// Tarif-Betrag für die Kategorie unter Berücksichtigung des Kindesalters.
    /// </summary>
    public static ResolveResult Resolve(FamilienzulagenTarif? tarif, string? allowanceType, int? childAge)
    {
        var type = (allowanceType ?? "KZ").Trim().ToUpper();
        if (type == "ADOPTZ") type = "AdoptZ"; // Kanon-Schreibweise

        if (tarif == null)
        {
            return new ResolveResult(null, type, "", "Kein FAK-Tarif für die Filiale am Stichtag hinterlegt.", null, null);
        }

        // Tarif-Gültigkeit als Klartext (für UI-Beschreibung)
        string ValidPart()
        {
            var ab = $"ab {tarif.ValidFrom:dd.MM.yyyy}";
            return tarif.ValidTo.HasValue ? $"{ab} – {tarif.ValidTo:dd.MM.yyyy}" : ab;
        }

        switch (type)
        {
            case "KZ":
                return ResolveKzAz(
                    satz1: tarif.KinderzulageSatz1,
                    satz2: tarif.KinderzulageSatz2,
                    satz2AbAlter: tarif.KinderzulageSatz2AbAlter,
                    childAge: childAge,
                    type: "KZ",
                    label: "Kinderzulage",
                    tarif: tarif,
                    validPart: ValidPart()
                );
            case "AZ":
                return ResolveKzAz(
                    satz1: tarif.AusbildungszulageSatz1,
                    satz2: tarif.AusbildungszulageSatz2,
                    satz2AbAlter: tarif.AusbildungszulageSatz2AbAlter,
                    childAge: childAge,
                    type: "AZ",
                    label: "Ausbildungszulage",
                    tarif: tarif,
                    validPart: ValidPart()
                );
            case "GZ":
                if (tarif.GeburtszulageBetrag is decimal gz && gz > 0)
                    return new ResolveResult(gz, "GZ", "Pauschal",
                        $"{gz:0.00} CHF (einmalig), Tarif {tarif.KantonCode} {ValidPart()}",
                        tarif.Id, tarif.ValidFrom);
                return new ResolveResult(null, "GZ", "", $"Tarif {tarif.KantonCode} hat keinen Geburtszulage-Betrag hinterlegt.", tarif.Id, tarif.ValidFrom);
            case "AdoptZ":
                if (tarif.AdoptionszulageBetrag is decimal az && az > 0)
                    return new ResolveResult(az, "AdoptZ", "Pauschal",
                        $"{az:0.00} CHF (einmalig), Tarif {tarif.KantonCode} {ValidPart()}",
                        tarif.Id, tarif.ValidFrom);
                return new ResolveResult(null, "AdoptZ", "", $"Tarif {tarif.KantonCode} hat keinen Adoptionszulage-Betrag hinterlegt.", tarif.Id, tarif.ValidFrom);
            default:
                return new ResolveResult(null, type, "", $"Unbekannte Zulagenart '{allowanceType}'.", tarif.Id, tarif.ValidFrom);
        }
    }

    private static ResolveResult ResolveKzAz(
        decimal? satz1, decimal? satz2, int? satz2AbAlter,
        int? childAge, string type, string label,
        FamilienzulagenTarif tarif, string validPart)
    {
        bool useSatz2 = satz2.HasValue
                     && satz2AbAlter.HasValue
                     && childAge.HasValue
                     && childAge.Value >= satz2AbAlter.Value;

        decimal? amount = useSatz2 ? satz2 : satz1;
        string satzLabel = useSatz2 ? $"Satz 2 ab {satz2AbAlter} J." : "Satz 1";

        if (amount == null)
        {
            return new ResolveResult(null, type, satzLabel,
                $"Tarif {tarif.KantonCode} hat keinen {label}-{satzLabel} hinterlegt.",
                tarif.Id, tarif.ValidFrom);
        }

        var amt = amount.Value;
        var ageNote = childAge.HasValue ? $", Kind {childAge.Value} J." : "";
        return new ResolveResult(amt, type, satzLabel,
            $"{amt:0.00} CHF/Mt, {satzLabel}{ageNote}, Tarif {tarif.KantonCode} {validPart}",
            tarif.Id, tarif.ValidFrom);
    }

    /// <summary>
    /// Wie alt ist das Kind am Stichtag? Liefert null wenn DateOfBirth fehlt.
    /// </summary>
    public static int? CalcAge(DateTime? dob, DateOnly stichtag)
    {
        if (!dob.HasValue) return null;
        var b = DateOnly.FromDateTime(dob.Value);
        var age = stichtag.Year - b.Year;
        if (stichtag.Month < b.Month || (stichtag.Month == b.Month && stichtag.Day < b.Day)) age--;
        return age;
    }

    /// <summary>
    /// Walter-Vorgabe 28.05.2026 (v3): User-getriebene Auflösung — der gewählte
    /// Tarif-Satz aus der FamilyMemberAllowance (1 oder 2 für KZ/AZ, null für
    /// Pauschal-Typen GZ/AdoptZ) bestimmt direkt, welcher Wert aus dem Tarif
    /// zur Anwendung kommt. KEIN Alter-Auto-Switching mehr.
    ///
    /// Bei satzNr == null UND type=KZ/AZ → Fallback auf Satz 1 (Backward-Compat
    /// für Alt-Daten ohne tarif_satz_nr).
    /// </summary>
    public static ResolveResult ResolveBySatz(FamilienzulagenTarif? tarif, string? allowanceType, int? satzNr)
    {
        var type = (allowanceType ?? "KZ").Trim();
        // Kanon-Schreibweise (case-insensitiv, AdoptZ Camelcase)
        if (string.Equals(type, "AdoptZ", StringComparison.OrdinalIgnoreCase)) type = "AdoptZ";
        else type = type.ToUpperInvariant();

        if (tarif == null)
        {
            return new ResolveResult(null, type, "", "Kein FAK-Tarif für die Filiale am Stichtag hinterlegt.", null, null);
        }

        string ValidPart()
        {
            var ab = $"ab {tarif.ValidFrom:dd.MM.yyyy}";
            return tarif.ValidTo.HasValue ? $"{ab} – {tarif.ValidTo:dd.MM.yyyy}" : ab;
        }

        decimal? amount = null;
        string satzLabel = "";

        switch (type)
        {
            case "KZ":
                if (satzNr == 2)
                {
                    amount = tarif.KinderzulageSatz2;
                    satzLabel = tarif.KinderzulageSatz2AbAlter.HasValue
                        ? $"Satz 2 ab {tarif.KinderzulageSatz2AbAlter} J."
                        : "Satz 2";
                }
                else
                {
                    amount = tarif.KinderzulageSatz1;
                    satzLabel = "Satz 1";
                }
                break;
            case "AZ":
                if (satzNr == 2)
                {
                    amount = tarif.AusbildungszulageSatz2;
                    satzLabel = tarif.AusbildungszulageSatz2AbAlter.HasValue
                        ? $"Satz 2 ab {tarif.AusbildungszulageSatz2AbAlter} J."
                        : "Satz 2";
                }
                else
                {
                    amount = tarif.AusbildungszulageSatz1;
                    satzLabel = "Satz 1";
                }
                break;
            case "GZ":
                amount = tarif.GeburtszulageBetrag;
                satzLabel = "Pauschal";
                break;
            case "AdoptZ":
                amount = tarif.AdoptionszulageBetrag;
                satzLabel = "Pauschal";
                break;
            default:
                return new ResolveResult(null, type, "", $"Unbekannte Zulagenart '{allowanceType}'.", tarif.Id, tarif.ValidFrom);
        }

        if (!amount.HasValue || amount.Value <= 0m)
        {
            return new ResolveResult(null, type, satzLabel,
                $"Tarif {tarif.KantonCode} {ValidPart()} hat keinen {type} {satzLabel} hinterlegt.",
                tarif.Id, tarif.ValidFrom);
        }

        var amt = amount.Value;
        var einheit = (type == "GZ" || type == "AdoptZ") ? "CHF (einmalig)" : "CHF/Mt";
        return new ResolveResult(amt, type, satzLabel,
            $"{amt:0.00} {einheit}, {satzLabel}, Tarif {tarif.KantonCode} {ValidPart()}",
            tarif.Id, tarif.ValidFrom);
    }
}
