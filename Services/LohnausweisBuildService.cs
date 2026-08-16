using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Baut LohnausweisData + PDF aus Snapshots/Stammdaten (Walter 30.07.2026).
/// Ausgelagert aus <c>LohnausweisController</c>, damit der authentifizierte
/// Download und der öffentliche Behörden-Link EXAKT dasselbe Formular erzeugen.
/// </summary>
public static class LohnausweisBuildService
{
    /// <summary>
    /// Baut das LohnausweisData-DTO. Gibt (null, 0, null) wenn keine Snapshots.
    /// Letzter Tuple-Eintrag = UID der Filiale (Barcode).
    /// </summary>
    public static async Task<(LohnausweisData? Data, int Months, string? Uid, AppUser? HrUser)>
        BuildDataAsync(AppDbContext db, Employee emp, int year)
    {
        var aggregated = await AggregateAsync(db, emp.Id, year);
        if (aggregated.AnzahlSnapshots == 0) return (null, 0, null, null);

        var company = await db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == aggregated.CompanyProfileId);

        AppUser? hrUser = null;
        if (company != null)
        {
            var branchUsers = await db.UserBranchAccesses
                .Include(uba => uba.User)
                .Where(uba => uba.CompanyProfileId == company.Id && uba.User.IsActive)
                .ToListAsync();
            var signatoryUba = branchUsers.FirstOrDefault(uba => uba.Role == "HR_VERANTWORTLICH")
                            ?? branchUsers.FirstOrDefault(uba => uba.User.IsHrTeam)
                            ?? branchUsers.FirstOrDefault(uba => uba.IsDefault);
            hrUser = signatoryUba?.User;
        }

        var (periodeVon, periodeBis, istGanzesJahr) = ResolveAnstellungsperiode(emp, year);

        decimal? z21 = null;
        if (company?.LohnausweisPos21VerpflegungMonat is decimal monatsBetrag && monatsBetrag > 0)
            z21 = Math.Round(monatsBetrag * aggregated.AnzahlSnapshots, 2);

        var empfaengerAdresse = BuildEmployeeAddress(emp);
        var bestaetigung      = BuildBestaetigungsBlock(company, hrUser);

        var swissNum = new System.Globalization.NumberFormatInfo
        {
            NumberDecimalSeparator = ".",
            NumberGroupSeparator   = "'",
            NumberGroupSizes       = new[] { 3 }
        };
        // Bemerkungen (Walter-Vorgabe 16.08.2026): NUR ganze Franken — wie
        // auf dem restlichen Lohnausweis (kaufmaennisch gerundet, ohne .00).
        string? ktgBemerkung = aggregated.KtgTotal > 0
            ? $"Krankengeldversicherung CHF {Math.Round(aggregated.KtgTotal, 0, MidpointRounding.AwayFromZero).ToString("N0", swissNum)}"
            : null;
        string? lgavBemerkung = aggregated.LgavTotal > 0
            ? $"L-GAV-Vollzugsbeitrag: CHF {Math.Round(aggregated.LgavTotal, 0, MidpointRounding.AwayFromZero).ToString("N0", swissNum)}"
            : null;

        // Teilzeit-Hinweis (Wegleitung Ziffer 15, Walter-Vorgabe 16.08.2026):
        // bei Beschaeftigungsgrad < 100 % gehoert «X%-Stelle.» in die
        // Bemerkungen — Pensum aus dem im Jahr zuletzt gueltigen Vertrag.
        string? pensumBemerkung = null;
        var jahresEnde = new DateTime(year, 12, 31);
        var vertragImJahr = emp.Employments?
            .Where(e2 => e2.ContractStartDate <= jahresEnde
                      && (e2.ContractEndDate == null || e2.ContractEndDate >= new DateTime(year, 1, 1)))
            .OrderByDescending(e2 => e2.ContractStartDate)
            .FirstOrDefault();
        if (vertragImJahr?.EmploymentPercentage is decimal pensum && pensum > 0 && pensum < 100)
            pensumBemerkung = pensum.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%-Stelle.";

        string? hrFullName = null;
        if (hrUser != null)
        {
            var combined = $"{hrUser.LastName} {hrUser.FirstName}".Trim();
            if (!string.IsNullOrWhiteSpace(combined)) hrFullName = combined;
        }

        string? firmenname = company?.CompanyName;
        string? branchCl = null;

        var data = new LohnausweisData
        {
            EmpfaengerAdresse        = empfaengerAdresse,
            IstGanzesJahr            = istGanzesJahr,
            IstLohnausweis           = true,
            AhvNummer                = emp.SocialSecurityNumber,
            Geburtsdatum             = emp.DateOfBirth?.ToString("dd.MM.yyyy"),
            MitarbeiterNameAdresse   = empfaengerAdresse,
            Jahr                     = year.ToString(),
            PeriodeVon               = periodeVon,
            PeriodeBis               = periodeBis,
            BoxFFreierTransport      = company?.LohnausweisBoxFFreierTransport ?? false,
            BoxGKantineGratis        = company?.LohnausweisBoxGKantineGratis   ?? false,
            Heimatort                = null,
            Ziffer1Lohn              = aggregated.Brutto,
            Ziffer21VerpflegungUnterkunft = z21,
            Ziffer8BruttoTotal       = aggregated.Brutto + (z21 ?? 0m),
            Ziffer9AhvIvEoAlvNbu     = aggregated.SvAbzuegeTotal,
            Ziffer101BvgOrdentlich   = aggregated.BvgAbzuege,
            Ziffer11Nettolohn        = aggregated.Netto,
            Ziffer12Quellensteuer    = aggregated.QstBetrag,
            Ziffer141Bemerkungen     = null,
            Ziffer142Bemerkungen     = null,
            Ziffer151Bemerkungen     = string.Join(" ", new[] { pensumBemerkung, ktgBemerkung }
                                           .Where(s => !string.IsNullOrWhiteSpace(s))) is string z15 && z15.Length > 0
                                           ? z15 : null,
            Ziffer152Bemerkungen     = lgavBemerkung,
            Ziffer151Ort             = company?.City ?? "Meggen",
            Ziffer152Datum           = DateTime.Today.ToString("dd.MM.yyyy"),
            BestaetigungAgBlock      = bestaetigung,

            CompanyUidFormatted      = company?.UidNummer,
            CompanyName              = firmenname,
            BranchName               = branchCl,
            CompanyStreet            = $"{company?.Street ?? ""} {company?.HouseNumber ?? ""}".Trim(),
            CompanyZip               = company?.ZipCode,
            CompanyCity              = company?.City,
            CompanyCountry           = ToSwissdecCountry(company?.Country),
            CompanyPhone             = company?.Phone,
            HrVerantwortlicherName   = hrFullName,

            MaLastname               = emp.LastName,
            MaFirstname              = emp.FirstName,
            MaStreet                 = emp.Street ?? "",
            MaZip                    = emp.ZipCode,
            MaCity                   = emp.City,
            MaCountry                = ToSwissdecCountry(emp.Country),
        };

        return (data, aggregated.AnzahlSnapshots, company?.UidNummer, hrUser);
    }

    /// <summary>
    /// Generiert das PDF für einen MA/Jahr. Signature optional (Login-User oder HR).
    /// </summary>
    public static async Task<(byte[]? Pdf, string? Filename, string? Error)> GeneratePdfAsync(
        AppDbContext db, LohnausweisPdfService pdfSvc, int employeeId, int year,
        byte[]? signaturePng = null, string? signerName = null)
    {
        var emp = await db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return (null, null, "Mitarbeiter nicht gefunden.");

        var (data, _, companyUid, hrUser) = await BuildDataAsync(db, emp, year);
        if (data == null)
            return (null, null, $"Keine Lohnabrechnungen für {emp.FirstName} {emp.LastName} im Jahr {year} gefunden.");

        if (signaturePng == null && hrUser != null)
        {
            signaturePng = hrUser.SignaturePng;
            var fullName = $"{hrUser.FirstName} {hrUser.LastName}".Trim();
            signerName = string.IsNullOrWhiteSpace(fullName) ? hrUser.Username : fullName;
        }

        try
        {
            var bytes = pdfSvc.Generate(data, signaturePng, signerName, companyUid);
            var filename = $"Lohnausweis_{year}_{emp.LastName}_{emp.FirstName}.pdf";
            return (bytes, filename, null);
        }
        catch (Exception ex)
        {
            return (null, null, "PDF konnte nicht erstellt werden: " + ex.Message);
        }
    }

    private static async Task<AggregatedYear> AggregateAsync(AppDbContext db, int employeeId, int year)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd   = new DateOnly(year, 12, 31);

        var snapshots = await db.PayrollSnapshots
            .Include(s => s.Periode)
            .Where(s => s.EmployeeId == employeeId
                     && s.Periode != null
                     && s.Periode.PeriodFrom >= yearStart
                     && s.Periode.PeriodFrom <= yearEnd)
            .OrderBy(s => s.Periode!.PeriodFrom)
            .ToListAsync();

        var agg = new AggregatedYear { AnzahlSnapshots = snapshots.Count };
        if (snapshots.Count == 0) return agg;

        agg.CompanyProfileId = snapshots[0].CompanyProfileId;

        decimal sv   = 0m;
        decimal ktg  = 0m;
        decimal bvg  = 0m;
        decimal lgav = 0m;

        foreach (var s in snapshots)
        {
            agg.Brutto    += s.Brutto;
            agg.Netto     += s.Netto;
            agg.QstBetrag += s.QstBetrag;
            ExtractAbzuege(s.SlipJson, ref sv, ref ktg, ref bvg, ref lgav);
        }

        agg.SvAbzuegeTotal = Math.Round(Math.Abs(sv), 2);
        agg.KtgTotal       = Math.Round(Math.Abs(ktg), 2);
        agg.BvgAbzuege     = Math.Round(Math.Abs(bvg), 2);
        agg.LgavTotal      = Math.Round(Math.Abs(lgav), 2);
        agg.QstBetrag      = Math.Round(Math.Abs(agg.QstBetrag), 2);
        agg.Brutto         = Math.Round(agg.Brutto, 2);
        agg.Netto          = Math.Round(agg.Netto, 2);

        return agg;
    }

    private static void ExtractAbzuege(
        string slipJson,
        ref decimal svAbzuegeTotal,
        ref decimal ktgBetrag,
        ref decimal bvgAbzuege,
        ref decimal lgavBetrag)
    {
        if (string.IsNullOrWhiteSpace(slipJson) || slipJson == "{}") return;

        try
        {
            using var doc = JsonDocument.Parse(slipJson);
            var root = doc.RootElement;

            JsonElement lines = default;
            bool found = false;
            foreach (var k in new[] { "abzugLines", "abzuege", "lines", "positions", "lohnpositionen", "items" })
            {
                if (root.TryGetProperty(k, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    lines = arr;
                    found = true;
                    break;
                }
            }
            if (!found) return;

            foreach (var line in lines.EnumerateArray())
            {
                var label  = GetStringProp(line, "bezeichnung", "label", "name");
                var amount = GetDecimalProp(line, "betrag", "amount", "totalAmount");
                if (amount == null || string.IsNullOrWhiteSpace(label)) continue;

                var key = label.ToLowerInvariant();

                if (key.Contains("bvg")
                    || key.Contains("berufliche vorsorge")
                    || key.Contains("pensionskasse")
                    || key.Contains("gastrosocial")
                    || key.Contains("uno basis")
                    || key.Contains("uno int")
                    || key.Contains("2. säule")
                    || key.Contains("2. saule"))
                {
                    bvgAbzuege += amount.Value;
                    continue;
                }

                if (key.Contains("lgav") || key.Contains("l-gav") || key.Contains("gav-beitrag")
                    || key.Contains("vollzugsbeitrag"))
                {
                    lgavBetrag += amount.Value;
                    continue;
                }

                if (key.Contains("krankentaggeld") || key.StartsWith("ktg")
                    || key.Contains("krankengeld"))
                {
                    ktgBetrag += amount.Value;
                    continue;
                }

                if (key.Contains("ahv")
                    || key.Contains("iv/eo") || key.Contains("/eo")
                    || key.Contains("alv")
                    || key.Contains("arbeitslosen")
                    || key.Contains("nbu")
                    || key.Contains("nichtberufs")
                    || key.StartsWith("uv "))
                {
                    svAbzuegeTotal += amount.Value;
                    continue;
                }
            }
        }
        catch
        {
            // Snapshot-JSON nicht parsbar → Abzüge bleiben 0.
        }
    }

    private static string? GetStringProp(JsonElement el, params string[] candidates)
    {
        foreach (var c in candidates)
            if (el.TryGetProperty(c, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        return null;
    }

    private static decimal? GetDecimalProp(JsonElement el, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!el.TryGetProperty(c, out var p)) continue;
            if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
            if (p.ValueKind == JsonValueKind.String
                && decimal.TryParse(p.GetString(),
                       System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out var d))
                return d;
        }
        return null;
    }

    private static (string von, string bis, bool ganzesJahr) ResolveAnstellungsperiode(Employee emp, int year)
    {
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd   = new DateTime(year, 12, 31);

        DateTime? entry = emp.EntryDate;
        if (entry == null && emp.Employments != null && emp.Employments.Count > 0)
            entry = emp.Employments.Min(e => e.ContractStartDate);

        DateTime? exit = emp.ExitDate;
        if (exit == null && emp.Employments != null && emp.Employments.Count > 0)
        {
            // Walter-Bug 16.08.2026: hat der MA einen OFFENEN Vertrag (ohne
            // Enddatum), ist er NICHT ausgetreten — das Enddatum eines alten
            // Vorvertrags (z.B. 31.12.2024) darf die Periode nicht kappen.
            var hatOffenenVertrag = emp.Employments.Any(e => e.ContractEndDate == null);
            exit = hatOffenenVertrag
                ? null
                : emp.Employments
                    .Where(e => e.ContractEndDate.HasValue)
                    .OrderByDescending(e => e.ContractEndDate)
                    .Select(e => e.ContractEndDate)
                    .FirstOrDefault();
        }

        var effFrom = entry.HasValue && entry.Value > yearStart ? entry.Value : yearStart;
        var effTo   = exit.HasValue  && exit.Value  < yearEnd   ? exit.Value  : yearEnd;
        if (effTo < effFrom) effTo = yearEnd; // Daten-Guard: nie verdrehte Periode ausgeben
        var ganzesJahr = effFrom <= yearStart && effTo >= yearEnd;

        return (
            effFrom.ToString("dd.MM.yyyy"),
            effTo.ToString("dd.MM.yyyy"),
            ganzesJahr
        );
    }

    private static string ToSwissdecCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return "SWITZERLAND";
        var c = country.Trim().ToUpperInvariant();
        return c is "CH" or "SCHWEIZ" or "SWITZERLAND" or "SUISSE" or "SVIZZERA"
            ? "SWITZERLAND"
            : c;
    }

    private static string BuildEmployeeAddress(Employee emp)
    {
        var name   = $"{emp.FirstName} {emp.LastName}".Trim();
        var street = emp.Street ?? "";
        var place  = $"{emp.ZipCode ?? ""} {emp.City ?? ""}".Trim();
        var parts  = new[] { name, street, place }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join("\n", parts);
    }

    private static string BuildBestaetigungsBlock(CompanyProfile? company, AppUser? hrUser)
    {
        if (company == null) return "";
        var firma  = company.CompanyName;
        var street = $"{company.Street ?? ""} {company.HouseNumber ?? ""}".Trim();
        var place  = $"{company.ZipCode ?? ""} {company.City ?? ""}".Trim();
        var uid    = company.UidNummer;

        string? hrName = null;
        if (hrUser != null)
        {
            var fullName = $"{hrUser.FirstName} {hrUser.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(fullName)) hrName = fullName;
        }
        string? hrTel = !string.IsNullOrWhiteSpace(company.Phone)
            ? $"Tel: {company.Phone}"
            : null;

        var parts = new[] { uid, firma, hrName, street, place, hrTel }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join("\n", parts);
    }

    private class AggregatedYear
    {
        public int     AnzahlSnapshots  { get; set; }
        public int     CompanyProfileId { get; set; }
        public decimal Brutto           { get; set; }
        public decimal Netto            { get; set; }
        public decimal SvAbzuegeTotal   { get; set; }
        public decimal KtgTotal         { get; set; }
        public decimal BvgAbzuege       { get; set; }
        public decimal LgavTotal        { get; set; }
        public decimal QstBetrag        { get; set; }
    }
}
