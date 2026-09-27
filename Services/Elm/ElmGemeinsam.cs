using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.Elm;

/// <summary>
/// Gemeinsame Bausteine aller ELM-Meldungen (Walter 27.09.2026): Namensräume,
/// Kopf (RequestContext), Firmenbeschreibung mit Workplaces, Personalien.
/// Jahres- und Monatsmeldung teilen diese Teile — sie dürfen NICHT kopiert
/// werden, sonst laufen sie auseinander und wir melden zwei verschiedene Firmen.
/// </summary>
public static class ElmGemeinsam
{
    public static readonly XNamespace Sdst  = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types";
    public static readonly XNamespace Sdcst = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types";
    public static readonly XNamespace Sdc   = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:container";
    public static readonly XNamespace Sd    = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration";
    public static readonly XNamespace Ep    = "urn:ch:swissdec:basis:v1:20260306:components";
    public static readonly XNamespace C     = "urn:ch:swissdec:common:v3:20260306";

    /// <summary>Betrag mit zwei Stellen, invariant («8000.00»).</summary>
    public static string Amt(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Alle CHF-Beträge im ELM-XML auf 5 Rappen (Walter-Vorgabe 21.09.2026, ABSOLUT).
    /// Ausnahmen: BVG-Fixbeträge der Kasse und Prozentfelder — die gehen unverändert
    /// durch und benutzen <see cref="Amt"/>.
    /// </summary>
    public static string Betrag05(decimal v) => Amt(PayrollCalculations.Round05(v));

    /// <summary>«Bahnhofstrasse» + «1» → «Bahnhofstrasse 1» (Swissdec führt beides in EINEM Feld).</summary>
    public static string StrasseMitNr(string? strasse, string? nr)
    {
        var st = (strasse ?? "").Trim(); var n = (nr ?? "").Trim();
        if (st.Length == 0) return n;
        return n.Length == 0 || st.EndsWith(" " + n) ? st : $"{st} {n}";
    }

    public static string MapCivilStatus(string? ms)
    {
        var s = (ms ?? "").ToLowerInvariant();
        if (s.Contains("verheiratet")) return "married";
        if (s.Contains("getrennt")) return "separated";
        if (s.Contains("geschieden")) return "divorced";
        if (s.Contains("verwitwet")) return "widowed";
        if (s.Contains("aufgel")) return "partnershipDissolvedByLaw";
        if (s.Contains("partnerschaft")) return "registeredPartnership";
        if (s.Contains("ledig") || s.Contains("konkubinat")) return "single";   // Konkubinat ist kein Zivilstand
        return "unknown";
    }

    /// <summary>Workplace-Kennung: Filialcode («#LU», «#058»), sonst DB-Id.</summary>
    public static string WpId(CompanyProfile b)
        => "#" + (string.IsNullOrWhiteSpace(b.RestaurantCode) ? $"wp{b.Id}" : b.RestaurantCode!.Trim());

    /// <summary>Stabile Work-Kennung einer Person.</summary>
    public static string WorkId(Employee e) => $"#w{e.Id}";

    /// <summary>
    /// Stammdaten der Rechtseinheit für den Meldungskopf: Hauptsitz (Name, UID,
    /// Sitzadresse) und die Filialen, die als Workplaces gemeldet werden.
    /// </summary>
    public record RechtseinheitStamm(
        Hauptsitz? Hauptsitz,
        List<CompanyProfile> Filialen,
        CompanyProfile Haupt,
        string Uid,
        string Firmenname,
        string SitzStrasse,
        string SitzPlz,
        string SitzOrt,
        Dictionary<int, int> GemeindeNr);

    /// <summary>
    /// Lädt Hauptsitz + Filialen und leitet UID, Firmenname, Sitzadresse und die
    /// BFS-Gemeindenummern je Filiale ab. Fehlendes wird in <paramref name="warn"/>
    /// gemeldet und mit einem klar erkennbaren Platzhalter gefüllt — nie still.
    /// </summary>
    public static async Task<RechtseinheitStamm?> LadeRechtseinheitAsync(
        AppDbContext db, List<string> warn, CancellationToken ct = default)
    {
        var filialen = await db.CompanyProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        if (filialen.Count == 0)
        {
            warn.Add("Keine Filialen (CompanyProfiles) vorhanden.");
            return null;
        }
        var haupt = filialen[0];

        var hsIds = filialen.Where(b => b.HauptsitzId != null).Select(b => b.HauptsitzId!.Value).Distinct().ToList();
        Hauptsitz? hs = null;
        if (hsIds.Count > 0)
        {
            var hsList = await db.Hauptsitze.AsNoTracking().Where(h => hsIds.Contains(h.Id)).OrderBy(h => h.Id).ToListAsync(ct);
            hs = hsList.FirstOrDefault();
            if (hsIds.Count > 1)
                warn.Add($"Filialen sind {hsIds.Count} verschiedenen Hauptsitzen zugeordnet — eine Meldung je Rechtseinheit kommt später; dieses XML läuft komplett unter «{hs?.Name}».");
        }
        else
        {
            hs = await db.Hauptsitze.AsNoTracking().Where(h => h.IsActive).OrderBy(h => h.Id).FirstOrDefaultAsync(ct);
            if (hs != null)
                warn.Add($"Keine Filiale ist einem Hauptsitz zugeordnet — es wird «{hs.Name}» verwendet (Filiale → Stammdaten bearbeiten).");
        }

        var st = await db.ElmStammdaten.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var uid = (hs?.Uid ?? st?.Uid ?? haupt.UidBfs ?? haupt.UidNummer ?? "").Trim();
        if (!Regex.IsMatch(uid, @"^CHE-\d{3}\.\d{3}\.\d{3}$"))
        {
            warn.Add($"UID fehlt/ungültig («{uid}») — Platzhalter CHE-123.123.123 eingesetzt (Hauptsitz-Verwaltung: UID erfassen).");
            uid = "CHE-123.123.123";
        }

        var gemeindeNr = new Dictionary<int, int>();
        foreach (var b in filialen)
        {
            if (b.BfsGemeindeNr is > 0) { gemeindeNr[b.Id] = b.BfsGemeindeNr.Value; continue; }
            var plz = (b.ZipCode ?? "").Trim(); var ort = (b.City ?? "").Trim().ToLowerInvariant();
            if (plz.Length != 4)
            {
                warn.Add($"Filiale «{b.FullDisplayName}»: keine BFS-Gemeindenummer (PLZ fehlt) — Swissdec verlangt MunicipalityID pro Workplace.");
                continue;
            }
            var treffer = await db.SwissLocations.AsNoTracking()
                .Where(l => l.Plz4 == plz).Select(l => new { l.BfsNr, l.Ortschaftsname, l.Gemeindename }).ToListAsync(ct);
            var nrs = treffer.Select(t => t.BfsNr).Distinct().ToList();
            var best = treffer.FirstOrDefault(t => (t.Ortschaftsname ?? "").ToLowerInvariant().StartsWith(ort)
                                                || (t.Gemeindename ?? "").ToLowerInvariant() == ort)?.BfsNr
                       ?? (nrs.Count == 1 ? nrs[0] : (int?)null);
            if (best is > 0) gemeindeNr[b.Id] = best.Value;
            else warn.Add($"Filiale «{b.FullDisplayName}»: BFS-Gemeindenummer nicht eindeutig ableitbar (PLZ {plz}, {nrs.Count} Gemeinden) — bitte in den Stammdaten eintragen.");
        }

        return new RechtseinheitStamm(
            hs, filialen, haupt, uid,
            (hs?.Name ?? haupt.CompanyName ?? "").Trim(),
            !string.IsNullOrWhiteSpace(hs?.Strasse) ? hs!.Strasse!.Trim() : StrasseMitNr(haupt.Street, haupt.HouseNumber),
            !string.IsNullOrWhiteSpace(hs?.Plz)     ? hs!.Plz!.Trim()     : (haupt.ZipCode ?? "").Trim(),
            !string.IsNullOrWhiteSpace(hs?.Ort)     ? hs!.Ort!.Trim()     : (haupt.City ?? "").Trim(),
            gemeindeNr);
    }

    /// <summary>Meldungskopf (RequestContext) — identisch für Jahres- und Monatsmeldung.</summary>
    public static XElement RequestContext(string firmenname, DateTime jetzt)
        => new(Ep + "RequestContext",
            new XElement(Ep + "UserAgent",
                new XElement(Ep + "Producer", "Schaub Restaurants GmbH"),
                new XElement(Ep + "Name", "OneCrew"),
                new XElement(Ep + "Version", "2026.09"),
                new XElement(Ep + "StandardVersion", "6.0"),
                new XElement(Ep + "Certificate", "n/a")),
            new XElement(Ep + "CompanyName", firmenname),
            new XElement(Ep + "TransmissionDate", jetzt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
            new XElement(Ep + "RequestID", Guid.NewGuid().ToString("N")),
            new XElement(Ep + "LanguageCode", "de"));

    /// <summary>
    /// Firmenbeschreibung: Rechtseinheit + alle Filialen als Workplaces.
    /// <paramref name="arbeitszeitmodelle"/> sind die CompanyWorkingTime-Einträge;
    /// solange es die Modell-Verwaltung nicht gibt, ist es genau eines aus den
    /// Wochenstunden der Hauptfiliale.
    /// </summary>
    public static XElement CompanyDescription(RechtseinheitStamm s, IEnumerable<XElement> arbeitszeitmodelle)
        => new(Sd + "CompanyDescription",
            new XElement(C + "Name", new XElement(C + "HR-RC-Name", s.Firmenname)),
            new XElement(C + "Address",
                string.IsNullOrWhiteSpace(s.SitzStrasse) ? null : new XElement(C + "Street", s.SitzStrasse),
                new XElement(C + "ZIP-Code", string.IsNullOrWhiteSpace(s.SitzPlz) ? "0000" : s.SitzPlz),
                new XElement(C + "City", string.IsNullOrWhiteSpace(s.SitzOrt) ? "Unbekannt" : s.SitzOrt),
                new XElement(C + "Country", "SWITZERLAND")),
            new XElement(C + "UID-BFS", new XElement(Ep + "UID", s.Uid)),
            s.Filialen.Select(b => new XElement(C + "Workplace",
                new XAttribute("workplaceID", WpId(b)),
                Regex.IsMatch((b.BurNummer ?? "").Trim(), "^[A-Z][0-9]{8}$")
                    ? new XElement(C + "BUR-REE-Number", b.BurNummer!.Trim())
                    : null,
                // Reihenfolge laut XSD: ComplementaryLine, Street, ZIP, City, Country, Canton, MunicipalityID
                new XElement(C + "AddressExtended",
                    string.IsNullOrWhiteSpace(b.BranchName) ? null : new XElement(C + "ComplementaryLine", b.BranchName!.Trim()),
                    string.IsNullOrWhiteSpace(StrasseMitNr(b.Street, b.HouseNumber)) ? null : new XElement(C + "Street", StrasseMitNr(b.Street, b.HouseNumber)),
                    new XElement(C + "ZIP-Code", string.IsNullOrWhiteSpace(b.ZipCode) ? "0000" : b.ZipCode!.Trim()),
                    new XElement(C + "City", string.IsNullOrWhiteSpace(b.City) ? "Unbekannt" : b.City!.Trim()),
                    new XElement(C + "Country", "SWITZERLAND"),
                    string.IsNullOrWhiteSpace(b.KantonCode) ? null : new XElement(C + "Canton", b.KantonCode!.Trim().ToUpperInvariant()),
                    s.GemeindeNr.TryGetValue(b.Id, out var g) ? new XElement(C + "MunicipalityID", g) : null))),
            arbeitszeitmodelle);

    /// <summary>
    /// Bewilligungsart → Swissdec <c>ResidenceCategory</c>. Nur für Ausländer;
    /// Schweizer haben keine (Walter 27.09.2026, Beleg TF14 annual-B, TF37 shortTerm-L).
    /// Ein unbekannter Buchstabe gibt null — lieber keine Angabe als eine falsche.
    /// </summary>
    public static string? Bewilligung(string? code) => (code ?? "").Trim().ToUpperInvariant() switch
    {
        "L" => "shortTerm-L",
        "B" => "annual-B",
        "C" => "settled-C",
        "G" => "crossBorder-G",
        "N" => "asylumSeeker-N",
        "S" => "needForProtection-S",
        "F" => "ProvisionallyAdmittedForeigners-F",
        "CI" => "ResidentForeignNationalWithGainfulEmployment-Ci",
        _ => null,
    };

    /// <summary>
    /// Personalien. <paramref name="wohnGemeindeNr"/> setzt die BFS-Gemeindenummer der
    /// Wohnadresse — die Monatsmeldung (Statistik) braucht sie, die Jahresmeldung nicht.
    /// </summary>
    public static XElement Particulars(Employee e, List<string> warn, int? wohnGemeindeNr = null)
    {
        var svDigits = Regex.Replace(e.SocialSecurityNumber ?? "", @"\D", "");
        XElement svEl;
        if (svDigits.Length == 13)
            svEl = new XElement(C + "SV-AS-Number",
                $"{svDigits[..3]}.{svDigits.Substring(3, 4)}.{svDigits.Substring(7, 4)}.{svDigits.Substring(11, 2)}");
        else
        {
            svEl = new XElement(C + "unknown");
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): AHV-Nummer fehlt/ungültig — als «unknown» gemeldet.");
        }

        var sex = (e.Gender ?? "").ToLowerInvariant();
        var sexCode = sex.StartsWith("f") || sex.StartsWith("w") ? "F" : "M";
        if (string.IsNullOrEmpty(sex))
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Geschlecht fehlt — als M gemeldet.");

        var natCode = (e.NationalityRef?.Code ?? "").ToUpperInvariant();
        if (!Regex.IsMatch(natCode, "^[A-Z]{2}$"))
        {
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Nationalität fehlt — als CH gemeldet.");
            natCode = "CH";
        }

        var zip = string.IsNullOrWhiteSpace(e.ZipCode) ? "0000" : e.ZipCode!.Trim();
        var city = string.IsNullOrWhiteSpace(e.City) ? "Unbekannt" : e.City!.Trim();
        if (zip == "0000" || city == "Unbekannt")
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Adresse unvollständig — Platzhalter eingesetzt.");

        var landCh = string.IsNullOrWhiteSpace(e.Country) || e.Country!.Trim().ToUpperInvariant() == "CH";
        var canton = (e.CantonCode ?? "").ToUpperInvariant();
        if (!Regex.IsMatch(canton, "^[A-Z]{2}$"))
        {
            canton = landCh ? "LU" : "EX";
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Wohnkanton fehlt — als {canton} gemeldet.");
        }

        var civilEl = new XElement(C + "CivilStatus", new XElement(C + "Status", MapCivilStatus(e.MaritalStatus)));
        if (e.MaritalStatusSince != null)
            civilEl.Add(new XElement(C + "ValidAsOf", e.MaritalStatusSince.Value.ToString("yyyy-MM-dd")));

        var adresse = new XElement(C + "Address",
            string.IsNullOrWhiteSpace(e.Street) ? null : new XElement(C + "Street", e.Street.Trim()),
            new XElement(C + "ZIP-Code", zip),
            new XElement(C + "City", city),
            new XElement(C + "Country", "SWITZERLAND"),
            new XElement(C + "ResidenceCanton", canton),
            wohnGemeindeNr is > 0 ? new XElement(C + "MunicipalityID", wohnGemeindeNr!.Value) : null);

        // Bewilligungsart nur bei Ausländern (Reihenfolge laut XSD nach Nationality).
        var bewilligung = natCode == "CH" ? null : Bewilligung(e.PermitType?.Code);
        if (natCode != "CH" && bewilligung == null && e.PermitTypeId != null)
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Bewilligungsart «{e.PermitType?.Code}» "
                   + "ist Swissdec nicht bekannt — ohne Angabe gemeldet.");

        return new XElement(C + "Particulars",
            new XElement(C + "Social-InsuranceIdentification", svEl),
            new XElement(C + "EmployeeNumber", e.EmployeeNumber),
            new XElement(C + "Lastname", e.LastName),
            new XElement(C + "Firstname", e.FirstName),
            new XElement(C + "Sex", sexCode),
            new XElement(C + "DateOfBirth", e.DateOfBirth!.Value.ToString("yyyy-MM-dd")),
            new XElement(C + "Nationality", natCode),
            bewilligung == null ? null : new XElement(C + "ResidenceCategory", bewilligung),
            civilEl,
            new XElement(C + "Addresses", adresse),
            new XElement(C + "LanguageCode",
                new[] { "de", "fr", "it", "en" }.Contains((e.LanguageCode ?? "de").ToLowerInvariant())
                    ? (e.LanguageCode ?? "de").ToLowerInvariant() : "de"));
    }

    /// <summary>
    /// Arbeitszeit-Block einer Person: FIX/FIX-M und MTP = Steady (Wochenstunden +
    /// Beschäftigungsgrad), FLEX = Unsteady.
    /// </summary>
    public static XElement WorkingTime(Employment? em, decimal betriebsWochenstunden)
    {
        // Honorar ohne Zeitbindung hat keine feste Arbeitszeit (Walter 27.09.2026).
        var art = (em?.SwissdecVertragsart ?? "").Trim();
        if (art.Contains("NoTimeConstraint", StringComparison.OrdinalIgnoreCase))
            return new XElement(C + "Unsteady");
        var model = em?.EmploymentModel?.ToUpperInvariant() ?? "";
        if ((model == "FIX" || model == "FIX-M") && em != null)
        {
            var pct = em.EmploymentPercentage ?? 100m;
            return new XElement(C + "Steady",
                new XElement(C + "WeeklyHours", Amt(PayrollCalculations.Rappen(betriebsWochenstunden * pct / 100m))),
                new XElement(C + "ActivityRate", Amt(pct)));
        }
        if (model == "MTP" && em?.GuaranteedHoursPerWeek is decimal gh && gh > 0)
            return new XElement(C + "Steady",
                new XElement(C + "WeeklyHours", Amt(gh)),
                new XElement(C + "ActivityRate", Amt(PayrollCalculations.Rappen(gh / betriebsWochenstunden * 100m))));
        return new XElement(C + "Unsteady");
    }
}
