using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Ermittelt den sinnvollsten Quellensteuer-Tarif aus MA-Stammdaten,
/// Kinder-Daten und den effektiv geladenen ESTV-Tarifkombinationen.
/// </summary>
public class QstTarifVorschlagService
{
    private readonly AppDbContext _db;
    private readonly QuellensteuerTarifService _tarife;

    public QstTarifVorschlagService(AppDbContext db, QuellensteuerTarifService tarife)
    {
        _db = db;
        _tarife = tarife;
    }

    public async Task<QstTarifVorschlagResult?> VorschlagenAsync(int employeeId, DateOnly? stichtag = null)
    {
        var refDate = stichtag ?? DateOnly.FromDateTime(DateTime.Today);
        var emp = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return null;

        var family = await _db.EmployeeFamilyMembers
            .AsNoTracking()
            .Where(f => f.EmployeeId == employeeId)
            .ToListAsync();

        var kanton = NormalizeCode(emp.CantonCode);
        var available = string.IsNullOrWhiteSpace(kanton)
            ? Array.Empty<QstTarifInfo>()
            : _tarife.GetTarifKombinationen(kanton, refDate.Year);

        return Build(emp, family, available, refDate);
    }

    public static QstTarifVorschlagResult Build(
        Employee employee,
        IEnumerable<EmployeeFamilyMember> family,
        IReadOnlyList<QstTarifInfo> availableTarife,
        DateOnly stichtag)
    {
        var warnings = new List<string>();
        var kanton = NormalizeCode(employee.CantonCode);
        if (string.IsNullOrWhiteSpace(kanton))
            warnings.Add("Kein Wohnkanton am Mitarbeiter hinterlegt.");

        var deductibleChildren = family
            .Where(f => IsKind(f) && IsQstDeductible(f, stichtag))
            .ToList();

        var kinderBerechnet = deductibleChildren.Count;
        var kinderImHaushalt = deductibleChildren.Count(IsSameHousehold);
        var kirchensteuer = HasKirchensteuer(employee.Religion);
        var preferredTarif = DeterminePreferredTarif(employee.MaritalStatus, kinderBerechnet, kinderImHaushalt, warnings);

        var match = ResolveAvailableTarif(availableTarife, preferredTarif, kinderBerechnet, kirchensteuer);
        warnings.AddRange(match.Warnings);

        var resultTarif = match.TarifCode ?? preferredTarif;
        var resultKinder = match.Kinder ?? kinderBerechnet;
        var resultKirche = match.Kirchensteuer ?? kirchensteuer;
        var qstCode = string.IsNullOrWhiteSpace(resultTarif)
            ? null
            : $"{resultTarif}{resultKinder}{(resultKirche ? "Y" : "N")}";

        return new QstTarifVorschlagResult(
            EmployeeId: employee.Id,
            Stichtag: stichtag,
            Steuerkanton: kanton,
            TarifCode: resultTarif,
            TarifBezeichnung: TarifBezeichnung(resultTarif),
            AnzahlKinder: resultKinder,
            BerechneteKinder: kinderBerechnet,
            KinderImSelbenHaushalt: kinderImHaushalt,
            Kirchensteuer: resultKirche,
            QstCode: qstCode,
            InTariftabelleGefunden: match.Found,
            Begruendung: BuildReason(employee.MaritalStatus, preferredTarif, kinderBerechnet, kinderImHaushalt, resultTarif),
            Warnings: warnings
        );
    }

    private static bool IsKind(EmployeeFamilyMember f)
        => string.Equals(f.MemberType, "Kind", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameHousehold(EmployeeFamilyMember f)
        => f.AlternativeAddressId == null;

    private static bool IsQstDeductible(EmployeeFamilyMember child, DateOnly stichtag)
    {
        var from = ToDateOnly(child.QstDeductibleFrom);
        var until = ToDateOnly(child.QstDeductibleUntil);
        if (from.HasValue || until.HasValue)
        {
            if (from.HasValue && from.Value > stichtag) return false;
            if (until.HasValue && until.Value < stichtag) return false;
            return true;
        }

        var dob = ToDateOnly(child.DateOfBirth);
        if (!dob.HasValue || dob.Value > stichtag) return false;
        return dob.Value.AddYears(18) >= stichtag;
    }

    private static DateOnly? ToDateOnly(DateTime? value)
        => value.HasValue ? DateOnly.FromDateTime(value.Value) : null;

    private static bool HasKirchensteuer(string? religion)
    {
        var r = NormalizeText(religion);
        return r is "evangelisch_reformiert" or "evangelisch-reformiert" or "evang"
            or "reformiert" or "evangelisch" or "roemisch_katholisch"
            or "römisch-katholisch" or "rk" or "roemisch" or "christ_katholisch"
            or "christkatholisch" or "christ-katholisch";
    }

    private static string DeterminePreferredTarif(
        string? maritalStatus,
        int deductibleChildren,
        int sameHouseholdChildren,
        List<string> warnings)
    {
        var z = NormalizeText(maritalStatus);
        var married = (z.Contains("verheiratet") || z.Contains("eingetragene_partnerschaft") || z.Contains("eingetragene partnerschaft"))
                   && !z.Contains("getrennt") && !z.Contains("aufgeloest") && !z.Contains("aufgelöst");
        if (married)
            return "C";

        var singleLike = string.IsNullOrWhiteSpace(z)
                      || z.Contains("ledig")
                      || z.Contains("geschieden")
                      || z.Contains("verwitwet")
                      || z.Contains("getrennt")
                      || z.Contains("aufgeloest")
                      || z.Contains("aufgelöst");

        if (deductibleChildren > 0 && sameHouseholdChildren > 0)
            return "H";

        if (!singleLike)
            warnings.Add($"Zivilstand '{maritalStatus}' nicht eindeutig erkannt; Vorschlag fällt auf Tarif A zurück.");

        return "A";
    }

    private static TarifMatch ResolveAvailableTarif(
        IReadOnlyList<QstTarifInfo> availableTarife,
        string preferredTarif,
        int requestedChildren,
        bool requestedKirche)
    {
        var warnings = new List<string>();
        if (availableTarife.Count == 0)
        {
            warnings.Add("Für diesen Kanton/Jahr sind keine Tarifkombinationen geladen; Vorschlag wurde nur aus Stammdaten abgeleitet.");
            return new TarifMatch(false, preferredTarif, requestedChildren, requestedKirche, warnings);
        }

        var tariffsToTry = preferredTarif == "A" ? new[] { "A" } : new[] { preferredTarif, "A" };
        var churchToTry = requestedKirche ? new[] { true, false } : new[] { false, true };

        foreach (var tarif in tariffsToTry)
        {
            foreach (var kirche in churchToTry)
            {
                var exact = availableTarife.FirstOrDefault(t =>
                    Same(t.Tarif, tarif) && t.Kinder == requestedChildren && t.Kirchensteuer == kirche);
                if (exact != null)
                {
                    AddFallbackWarnings(warnings, preferredTarif, requestedChildren, requestedKirche, exact);
                    return new TarifMatch(true, exact.Tarif, exact.Kinder, exact.Kirchensteuer, warnings);
                }
            }
        }

        foreach (var tarif in tariffsToTry)
        {
            foreach (var kirche in churchToTry)
            {
                var lower = availableTarife
                    .Where(t => Same(t.Tarif, tarif) && t.Kirchensteuer == kirche && t.Kinder <= requestedChildren)
                    .OrderByDescending(t => t.Kinder)
                    .FirstOrDefault();
                if (lower != null)
                {
                    AddFallbackWarnings(warnings, preferredTarif, requestedChildren, requestedKirche, lower);
                    return new TarifMatch(true, lower.Tarif, lower.Kinder, lower.Kirchensteuer, warnings);
                }
            }
        }

        warnings.Add($"Keine passende Tarifkombination fuer {preferredTarif}{requestedChildren}{(requestedKirche ? "Y" : "N")} gefunden.");
        return new TarifMatch(false, preferredTarif, requestedChildren, requestedKirche, warnings);
    }

    private static void AddFallbackWarnings(
        List<string> warnings,
        string preferredTarif,
        int requestedChildren,
        bool requestedKirche,
        QstTarifInfo actual)
    {
        if (!Same(actual.Tarif, preferredTarif))
            warnings.Add($"Tarif {preferredTarif} ist in der Tariftabelle nicht verfügbar; {actual.Tarif} wurde als Fallback verwendet.");
        if (actual.Kinder != requestedChildren)
            warnings.Add($"Tariftabelle enthält keine Kombination mit {requestedChildren} Kindern; {actual.Kinder} wurde verwendet.");
        if (actual.Kirchensteuer != requestedKirche)
            warnings.Add(actual.Kirchensteuer
                ? "Tariftabelle enthält nur die Variante mit Kirchensteuer."
                : "Tariftabelle enthält keine Kirchensteuer-Variante; Variante ohne Kirchensteuer wurde verwendet.");
    }

    private static string BuildReason(
        string? maritalStatus,
        string preferredTarif,
        int children,
        int sameHouseholdChildren,
        string? resultTarif)
    {
        var z = string.IsNullOrWhiteSpace(maritalStatus) ? "unbekannt" : maritalStatus;
        var basis = preferredTarif switch
        {
            "C" => $"Zivilstand '{z}' → verheiratet/eingetragene Partnerschaft; Standardvorschlag Doppelverdiener C.",
            "H" => $"{children} QST-abzugsberechtigte Kinder, davon {sameHouseholdChildren} im selben Haushalt → Tarif H.",
            _   => $"Zivilstand '{z}' und keine QST-abzugsberechtigten Kinder im selben Haushalt → Tarif A."
        };

        return resultTarif == preferredTarif
            ? basis
            : $"{basis} Tariftabelle-Fallback: {resultTarif}.";
    }

    private static string? TarifBezeichnung(string? tarif) => NormalizeCode(tarif) switch
    {
        "A" => "Tarif für alleinstehende Personen",
        "B" => "Verheiratet, Alleinverdiener",
        "C" => "Verheiratet, Doppelverdiener",
        "D" => "Nebenerwerb",
        "H" => "Alleinerziehend",
        "L" => "Grenzgänger alleinstehend",
        "M" => "Grenzgänger verheiratet",
        "N" => "Grenzgänger Nebenerwerb",
        "P" => "Pauschale",
        "Q" => "Grenzgänger alleinerziehend",
        _ => null
    };

    private static bool Same(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeText(string? value)
        => (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeCode(string? value)
        => (value ?? "").Trim().ToUpperInvariant();

    private record TarifMatch(
        bool Found,
        string? TarifCode,
        int? Kinder,
        bool? Kirchensteuer,
        List<string> Warnings);
}

public record QstTarifVorschlagResult(
    int EmployeeId,
    DateOnly Stichtag,
    string? Steuerkanton,
    string? TarifCode,
    string? TarifBezeichnung,
    int AnzahlKinder,
    int BerechneteKinder,
    int KinderImSelbenHaushalt,
    bool Kirchensteuer,
    string? QstCode,
    bool InTariftabelleGefunden,
    string Begruendung,
    IReadOnlyList<string> Warnings);
