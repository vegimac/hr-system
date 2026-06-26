using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace HrSystem.Controllers;

/// <summary>
/// Jahres-Lohnausweis (ESTV Form 11 dfe).
///
/// Phase 1: aggregiert die PayrollSnapshots eines Mitarbeiters über ein
/// Kalenderjahr und füllt das amtliche AcroForm-Template.
///
/// Walter (12.05.2026):
///   • Ziffer 1 = Bruttolohn (alles Lohnpflichtige inkl. Zulagen, 13. ML, Ferien-Geld-Auszahlung)
///   • Ziffer 2.1 Verpflegung/Unterkunft = pro Filiale konfigurierbar (LohnausweisPos21VerpflegungMonat × Monate).
///     Bei McDonald's Schaub: 0 — Crew zahlt 50%-Anteil, keine unentgeltliche Verpflegung.
///   • Ziffer 8 = Bruttoeinkommen Total
///   • Ziffer 9 = AHV/IV/EO/ALV/NBU-Beiträge AN-Anteil aus den Slip-Lohnpositionen
///   • Ziffer 10.1 = BVG-Beiträge ordentlich
///   • Ziffer 11 = Nettolohn (Brutto − Abzüge)
///   • Ziffer 12 = Quellensteuer-Abzug
///   • Box F = LohnausweisBoxFFreierTransport (Filial-Default, McD = false)
///   • Box G = LohnausweisBoxGKantineGratis  (Filial-Default, McD = false)
/// </summary>
[ApiController]
[Route("api/lohnausweis")]
[Authorize]
public class LohnausweisController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnausweisPdfService _pdf;

    public LohnausweisController(AppDbContext db, LohnausweisPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET /api/lohnausweis/{empId}/{year}/preview
    // Aggregierte Werte für das Vorschau-Modal (Walter darf editieren bevor PDF).
    // ════════════════════════════════════════════════════════════════════════
    [HttpGet("{employeeId}/{year}/preview")]
    public async Task<IActionResult> Preview(int employeeId, int year)
    {
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        var (data, anzahlMonate, _) = await BuildDataAsync(emp, year);
        if (data == null)
            return BadRequest(new {
                error = $"Keine Lohnabrechnungen für {emp.FirstName} {emp.LastName} im Jahr {year} gefunden."
            });

        return Ok(new { anzahlMonate, data });
    }

    // ════════════════════════════════════════════════════════════════════════
    // POST /api/lohnausweis/{empId}/{year}/pdf
    // Frontend schickt das (eventuell editierte) Preview-DTO als Body und
    // bekommt das PDF zurück.
    // ════════════════════════════════════════════════════════════════════════
    [HttpPost("{employeeId}/{year}/pdf")]
    public async Task<IActionResult> PdfFromPreview(int employeeId, int year,
        [FromBody] LohnausweisData payload)
    {
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        // Swissdec-Stammdaten für den Barcode aus DB nachfüllen — das Vorschau-
        // Modal sendet nur editierbare Felder zurück, die strukturierten Stamm-
        // daten (CompanyName, HR-Person, MaLastname, etc.) sind dort nicht
        // sichtbar und kommen darum leer beim POST an. Wir überschreiben nicht
        // wenn das Frontend einen Wert gesetzt hat, sondern füllen nur die
        // leeren Felder auf (Walter kann theoretisch im Modal überschreiben).
        var (fresh, _, freshUid) = await BuildDataAsync(emp, year);
        if (fresh != null)
        {
            MergeStammdaten(payload, fresh);
        }

        var (signaturePng, signerName) = await GetSignerAsync();

        byte[] bytes;
        try { bytes = _pdf.Generate(payload, signaturePng, signerName, freshUid); }
        catch (Exception ex) { return Problem("PDF konnte nicht erstellt werden: " + ex.Message); }

        var filename = $"Lohnausweis_{year}_{emp.LastName}_{emp.FirstName}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    /// <summary>
    /// Übernimmt die Swissdec-Stammdaten aus <paramref name="fresh"/> ins
    /// <paramref name="payload"/>, aber nur wenn das Payload-Feld leer ist
    /// (= Frontend hat nichts geschickt). So bleiben editierte Bemerkungen
    /// und Beträge unangetastet, fehlende Stammdaten werden ergänzt.
    /// </summary>
    private static void MergeStammdaten(LohnausweisData payload, LohnausweisData fresh)
    {
        static string? Fill(string? frontend, string? db)
            => string.IsNullOrWhiteSpace(frontend) ? db : frontend;

        payload.CompanyUidFormatted    = Fill(payload.CompanyUidFormatted,    fresh.CompanyUidFormatted);
        payload.CompanyName            = Fill(payload.CompanyName,            fresh.CompanyName);
        payload.BranchName             = Fill(payload.BranchName,             fresh.BranchName);
        payload.CompanyStreet          = Fill(payload.CompanyStreet,          fresh.CompanyStreet);
        payload.CompanyZip             = Fill(payload.CompanyZip,             fresh.CompanyZip);
        payload.CompanyCity            = Fill(payload.CompanyCity,            fresh.CompanyCity);
        payload.CompanyCountry         = Fill(payload.CompanyCountry,         fresh.CompanyCountry);
        payload.CompanyPhone           = Fill(payload.CompanyPhone,           fresh.CompanyPhone);
        payload.HrVerantwortlicherName = Fill(payload.HrVerantwortlicherName, fresh.HrVerantwortlicherName);
        payload.MaLastname             = Fill(payload.MaLastname,             fresh.MaLastname);
        payload.MaFirstname            = Fill(payload.MaFirstname,            fresh.MaFirstname);
        payload.MaStreet               = Fill(payload.MaStreet,               fresh.MaStreet);
        payload.MaZip                  = Fill(payload.MaZip,                  fresh.MaZip);
        payload.MaCity                 = Fill(payload.MaCity,                 fresh.MaCity);
        payload.MaCountry              = Fill(payload.MaCountry,              fresh.MaCountry);
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET /api/lohnausweis/{empId}/{year}/pdf
    // Convenience: aggregiert + generiert PDF in einem Schritt (ohne Vorschau-
    // Roundtrip). Für Bulk-Generierung oder Direktdruck.
    // ════════════════════════════════════════════════════════════════════════
    [HttpGet("{employeeId}/{year}/pdf")]
    public async Task<IActionResult> PdfDirect(int employeeId, int year)
    {
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { error = "Mitarbeiter nicht gefunden." });

        var (data, anzahlMonate, companyUid) = await BuildDataAsync(emp, year);
        if (data == null)
            return BadRequest(new {
                error = $"Keine Lohnabrechnungen für {emp.FirstName} {emp.LastName} im Jahr {year} gefunden."
            });

        var (signaturePng, signerName) = await GetSignerAsync();

        byte[] bytes;
        try { bytes = _pdf.Generate(data, signaturePng, signerName, companyUid); }
        catch (Exception ex) { return Problem("PDF konnte nicht erstellt werden: " + ex.Message); }

        var filename = $"Lohnausweis_{year}_{emp.LastName}_{emp.FirstName}.pdf";
        return File(bytes, "application/pdf", filename);
    }

    // ════════════════════════════════════════════════════════════════════════
    // INTERNAL HELPERS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Baut das LohnausweisData-DTO aus den PayrollSnapshots + Stammdaten.
    /// Gibt (null, 0, null) zurück wenn für das Jahr keine Snapshots existieren.
    /// Letzter Tuple-Eintrag ist die UID der Filiale (für den Barcode).
    /// </summary>
    private async Task<(LohnausweisData? Data, int Months, string? Uid)> BuildDataAsync(Employee emp, int year)
    {
        var aggregated = await AggregateAsync(emp.Id, year);
        if (aggregated.AnzahlSnapshots == 0) return (null, 0, null);

        var company = await _db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == aggregated.CompanyProfileId);

        // HR-Verantwortliche/r für Rückfragen (Reihenfolge analog QST-Anmeldung):
        //   1. UserBranchAccess.Role = "HR_VERANTWORTLICH" für diese Filiale
        //   2. User.IsHrTeam = true
        //   3. UserBranchAccess.IsDefault = true
        // Walter-Vorgabe: NIE den Geschäftsführer als HR-Ansprechperson.
        AppUser? hrUser = null;
        if (company != null)
        {
            var branchUsers = await _db.UserBranchAccesses
                .Include(uba => uba.User)
                .Where(uba => uba.CompanyProfileId == company.Id && uba.User.IsActive)
                .ToListAsync();
            var signatoryUba = branchUsers.FirstOrDefault(uba => uba.Role == "HR_VERANTWORTLICH")
                            ?? branchUsers.FirstOrDefault(uba => uba.User.IsHrTeam)
                            ?? branchUsers.FirstOrDefault(uba => uba.IsDefault);
            hrUser = signatoryUba?.User;
        }

        var (periodeVon, periodeBis, istGanzesJahr) = ResolveAnstellungsperiode(emp, year);

        // Ziffer 2.1 Verpflegung: Filial-Default × Anstellungsmonate
        decimal? z21 = null;
        if (company?.LohnausweisPos21VerpflegungMonat is decimal monatsBetrag && monatsBetrag > 0)
            z21 = Math.Round(monatsBetrag * aggregated.AnzahlSnapshots, 2);

        var empfaengerAdresse = BuildEmployeeAddress(emp);
        var bestaetigung      = BuildBestaetigungsBlock(company, hrUser);

        // Ziffer 14 Bemerkungen — Mirus-Konvention 1:1 nachgebildet:
        //   Zeile 1: "Krankengeldversicherung CHF X.XX"  (statt KTG in Ziffer 9)
        //   Zeile 2: "L-GAV-Vollzugsbeitrag: CHF Y.YY"   (Berufskosten für MA)
        var swissNum = new System.Globalization.NumberFormatInfo
        {
            NumberDecimalSeparator = ".",
            NumberGroupSeparator   = "'",
            NumberGroupSizes       = new[] { 3 }
        };
        string? ktgBemerkung = aggregated.KtgTotal > 0
            ? $"Krankengeldversicherung CHF {aggregated.KtgTotal.ToString("N2", swissNum)}"
            : null;
        string? lgavBemerkung = aggregated.LgavTotal > 0
            ? $"L-GAV-Vollzugsbeitrag: CHF {aggregated.LgavTotal.ToString("N2", swissNum)}"
            : null;

        // HR-Verantwortliche/r für <Company Person="..."/> im Barcode + visuell
        // im Bestätigungs-Block. Format: "Nachname Vorname" (Mirus-Konvention).
        string? hrFullName = null;
        if (hrUser != null)
        {
            var combined = $"{hrUser.LastName} {hrUser.FirstName}".Trim();
            if (!string.IsNullOrWhiteSpace(combined)) hrFullName = combined;
        }

        // HR-RC-Name = nur die Hauptfirma (Walter-Vorgabe 13.05.2026: "Schaub
        // Restaurants GmbH" reicht, die Filiale ergibt sich aus dem Ort).
        string? firmenname = company?.CompanyName;

        // CL bleibt leer — Filiale wird über die Adresse (City) sichtbar.
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
            // Ziffer 14 = "Weitere Gehaltsnebenleistungen" (Art/Genre/Kind) — bleibt leer.
            // Ziffer 15 = "Bemerkungen" → KTG + LGAV (Mirus-Konvention 13.05.2026).
            Ziffer141Bemerkungen     = null,
            Ziffer142Bemerkungen     = null,
            Ziffer151Bemerkungen     = ktgBemerkung,
            Ziffer152Bemerkungen     = lgavBemerkung,
            Ziffer151Ort             = company?.City ?? "Meggen",
            Ziffer152Datum           = DateTime.Today.ToString("dd.MM.yyyy"),
            BestaetigungAgBlock      = bestaetigung,

            // ── Swissdec-Barcode-Felder ────────────────────────────────────
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

        return (data, aggregated.AnzahlSnapshots, company?.UidNummer);
    }

    private async Task<(byte[]? signaturePng, string? signerName)> GetSignerAsync()
    {
        byte[]? png = null;
        string? name = null;
        var loggedInIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(loggedInIdStr, out var loggedInId))
        {
            var u = await _db.AppUsers
                .Where(x => x.Id == loggedInId)
                .Select(x => new { x.SignaturePng, x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                png = u.SignaturePng;
                var fullName = $"{u.FirstName} {u.LastName}".Trim();
                name = string.IsNullOrWhiteSpace(fullName) ? u.Username : fullName;
            }
        }
        return (png, name);
    }

    private async Task<AggregatedYear> AggregateAsync(int employeeId, int year)
    {
        // Date-Range statt .Year-Property — Npgsql kann DateOnly.Year nicht
        // immer übersetzen. Snapshots aller Perioden, die im Kalenderjahr starten.
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd   = new DateOnly(year, 12, 31);

        var snapshots = await _db.PayrollSnapshots
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

        // Lokale Akkumulatoren — Properties können nicht direkt als ref übergeben werden.
        decimal sv   = 0m;    // Ziffer 9: AHV/IV/EO + ALV + NBU (OHNE KTG)
        decimal ktg  = 0m;    // KTG separat → Ziffer 14 Bemerkung
        decimal bvg  = 0m;    // Ziffer 10.1
        decimal lgav = 0m;    // L-GAV → Ziffer 14 Bemerkung

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

    /// <summary>
    /// Extrahiert die Abzüge aus dem Slip-JSON. PayrollController-Konvention:
    /// Abzüge im Array `abzugLines`, jedes Element hat `bezeichnung` (deutscher
    /// Name) und `betrag` (negativer Wert). Code-Feld existiert nicht — Match
    /// läuft rein über die Bezeichnung.
    ///
    /// Zuordnung (validiert gegen Mirus-Referenz-Lohnausweis 13.05.2026):
    ///   Ziffer 9    = AHV/IV/EO + ALV + NBUV (OHNE KTG — ESTV-Wegleitung Rz 35)
    ///   Ziffer 10.1 = BVG ordentlich (GastroSocial Uno Basis, Uno Int McD Zusatz)
    ///   Ziffer 14   = KTG als Bemerkung "Krankengeldversicherung CHF X.XX"
    ///                 + LGAV als Bemerkung "L-GAV-Vollzugsbeitrag CHF Y.YY"
    ///   QST         = aus s.QstBetrag (separat) → hier ignorieren.
    ///   Lohnabtretungen → ignorieren.
    /// </summary>
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

                // ── BVG (Ziffer 10.1) — zuerst, vor allgemeineren Patterns ──
                // GastroSocial = BVG-Träger im Gastgewerbe (Basis + Kader-Zusatz "Uno Int").
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

                // ── LGAV-Beitrag separat — geht in Ziffer 14 als Bemerkung ──
                if (key.Contains("lgav") || key.Contains("l-gav") || key.Contains("gav-beitrag")
                    || key.Contains("vollzugsbeitrag"))
                {
                    lgavBetrag += amount.Value;
                    continue;
                }

                // ── KTG separat — Mirus-Konvention: NICHT in Ziffer 9, sondern
                //    in Ziffer 14 als Bemerkung "Krankengeldversicherung CHF X" ──
                if (key.Contains("krankentaggeld") || key.StartsWith("ktg")
                    || key.Contains("krankengeld"))
                {
                    ktgBetrag += amount.Value;
                    continue;
                }

                // ── SV-Abzüge (Ziffer 9): AHV/IV/EO + ALV + NBUV (OHNE KTG) ──
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

                // Alle anderen (Lohnabtretung, sonstige) → ignorieren für Ziffer 9/10.1
            }
        }
        catch
        {
            // Snapshot-JSON nicht parsbar → Abzüge bleiben 0. Walter kann manuell überschreiben.
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

    /// <summary>
    /// Periode für Ziffer E. Ist der MA das ganze Jahr durchgehend angestellt
    /// → 01.01. - 31.12. (Box A "ganzes Jahr" angekreuzt). Sonst: konkrete
    /// Eintritts-/Austritts-Schnittmenge mit dem Kalenderjahr.
    /// </summary>
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
            // Spätestes ContractEndDate (NULL bleibt offen = aktiv)
            exit = emp.Employments
                .Where(e => e.ContractEndDate.HasValue)
                .OrderByDescending(e => e.ContractEndDate)
                .Select(e => e.ContractEndDate)
                .FirstOrDefault();
        }

        var effFrom = entry.HasValue && entry.Value > yearStart ? entry.Value : yearStart;
        var effTo   = exit.HasValue  && exit.Value  < yearEnd   ? exit.Value  : yearEnd;

        var ganzesJahr = effFrom <= yearStart && effTo >= yearEnd;

        return (
            effFrom.ToString("dd.MM.yyyy"),
            effTo.ToString("dd.MM.yyyy"),
            ganzesJahr
        );
    }

    /// <summary>
    /// Mappt den systemweiten Land-Code („CH", evtl. Altdaten „Schweiz") auf
    /// den vom Swissdec-Lohnausweis erwarteten Klartext „SWITZERLAND".
    /// Andere Länder werden gross geschrieben durchgereicht.
    /// </summary>
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

    /// <summary>
    /// Baut den Bestätigungs-Block (unten rechts auf dem Lohnausweis) auf.
    /// Reihenfolge der Zeilen (Walter-Vorgabe 13.05.2026):
    ///   1. UID (zuoberst, Pflichtangabe nach Lohnausweis-Wegleitung Rz 9)
    ///   2. Firmenname + Filialname
    ///   3. HR-Verantwortliche/r (Vorname Nachname — für Rückfragen)
    ///   4. Strasse + Hausnummer
    ///   5. PLZ + Ort
    ///   6. Tel: +41 XX XXX XX XX (HR-Verantwortliche)
    /// </summary>
    private static string BuildBestaetigungsBlock(CompanyProfile? company, AppUser? hrUser)
    {
        if (company == null) return "";
        // Nur Hauptfirma (Walter-Vorgabe 13.05.2026: "Schaub Restaurants GmbH",
        // ohne Filial-Suffix — die Filiale ist über die Adresse ersichtlich).
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
        // Walter-Vorgabe 13.05.2026: AUF FORMULAREN immer Filial-Telefon, NIE
        // die persönliche Nummer der HR-Verantwortlichen — Datenschutz +
        // einheitliches Auftreten gegenüber Behörden/Steuerämtern.
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
        public decimal SvAbzuegeTotal   { get; set; }   // Ziffer 9: AHV+IV+EO+ALV+NBU OHNE KTG
        public decimal KtgTotal         { get; set; }   // Ziffer 14 Bemerkung "Krankengeldversicherung"
        public decimal BvgAbzuege       { get; set; }   // Ziffer 10.1 BVG ordentlich
        public decimal LgavTotal        { get; set; }   // Ziffer 14 Bemerkung "L-GAV-Vollzugsbeitrag"
        public decimal QstBetrag        { get; set; }   // Ziffer 12
    }
}
