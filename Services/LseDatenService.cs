using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// BFS Lohnstrukturerhebung — Datensammlung + Validierung (Walter 13.08.2026).
///
/// Grundprinzip: die LSE wird möglichst vollständig AUTOMATISCH aus den
/// bestehenden OneCrew-Daten erstellt (Employee, Employment, TimeEntries,
/// PayrollSnapshot/SlipJson). Es wird NICHTS doppelt gespeichert — nur die
/// LSE-Ergänzungsfelder (employee_lse) und die vom Benutzer bestätigten
/// Mappings (lse_lohnart_mapping, lse_code_mapping).
///
/// Referenzmonat = Oktober (aus lse_version.config). Relevante MA = alle mit
/// im Oktober laufendem Vertrag und Oktober-Lohnlauf (Snapshot).
///
/// KEINE Auto-Zuordnung von Lohnarten oder Stellungen anhand von Namen:
/// unbestätigte/fehlende Zuordnungen erscheinen als «BFS-Zuordnung fehlt».
/// Codes/Wertebereiche/Pflichtfelder kommen aus der Versions-Konfiguration
/// (lse_version) — LSE 2026 = neue Konfigzeile, keine Code-Änderung.
/// </summary>
public class LseDatenService
{
    private readonly AppDbContext _db;
    public LseDatenService(AppDbContext db) => _db = db;

    // BFS-Kategorien des Lohnarten-Mappings (Kapitel 9 der Vorgabe).
    public static readonly string[] BfsKategorien =
    {
        "GRUNDLOHN", "ZULAGEN", "FAMILIENZULAGEN", "SV_AN", "BVG_AN",
        "DREIZEHNTER", "UEBERSTUNDEN", "UNREGELMAESSIG", "NEBENLEISTUNGEN",
        "KAPITALLEISTUNGEN", "WEITERE", "NICHT_RELEVANT",
    };

    // Lesbare Berufs-VORSCHLÄGE pro JobGroup (AD practicedProfessionOct) —
    // nur Vorschlag, pro MA via employee_lse übersteuerbar. Bewusst keine
    // internen Jobcodes exportieren.
    private static readonly Dictionary<string, string> BerufVorschlag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CREW"] = "Restaurantmitarbeiter/in",
        ["HOST_CT"] = "Gästebetreuer/in",
        ["SWING"] = "Schichtführer/in",
        ["SHIFT_LEADER_1_6"] = "Schichtführer/in",
        ["SHIFT_LEADER_7_PLUS"] = "Schichtführer/in",
        ["ASST_1"] = "Assistenz-Restaurantleiter/in",
        ["ASST_2"] = "Assistenz-Restaurantleiter/in",
        ["REST_MANAGER"] = "Restaurantleiter/in",
    };

    public class LseFeld
    {
        public string Feld { get; set; } = "";
        public object? Wert { get; set; }
        public bool Fehlt { get; set; }
        public string? Hinweis { get; set; }
    }

    public class LseMaRow
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; } = "";
        public string EmployeeNumber { get; set; } = "";
        public string Filiale { get; set; } = "";
        public int? CompanyProfileId { get; set; }
        /// <summary>GRUEN | ORANGE | ROT</summary>
        public string Status { get; set; } = "GRUEN";
        public Dictionary<string, object?> Werte { get; } = new();
        /// <summary>Pflichtangaben, die fehlen/ungültig sind (Klartext).</summary>
        public List<string> Fehler { get; } = new();
        /// <summary>Zu prüfende Punkte (z.B. BFS-Zuordnung fehlt, berechnete Werte).</summary>
        public List<string> Hinweise { get; } = new();
    }

    public class LseBuildResult
    {
        public int SurveyYear { get; set; }
        public string? SpecVersion { get; set; }
        public List<LseMaRow> Rows { get; } = new();
        /// <summary>Im Jahr vorkommende Lohnarten OHNE bestätigte BFS-Zuordnung.</summary>
        public List<UnmappedLohnart> UnmappedLohnarten { get; } = new();
        public List<string> UnmappedStellung { get; } = new();
        public List<string> UnmappedVertrag { get; } = new();
    }

    public class UnmappedLohnart
    {
        public string Key { get; set; } = "";
        public string Bezeichnung { get; set; } = "";
        public int Vorkommen { get; set; }
    }

    /// <summary>Versions-Konfig laden (aktive Version zum Jahr).</summary>
    public async Task<LseVersion?> GetVersionAsync(int surveyYear)
        => await _db.LseVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.SurveyYear == surveyYear && v.IsActive);

    /// <summary>
    /// Mapping-Schlüssel einer Slip-Zeile: Lohnart-Code wenn vorhanden, sonst
    /// die normalisierte Bezeichnung (Text vor der ersten Klammer). Damit ist
    /// «Festlohn (150.57h Soll − …)» stabil «Festlohn».
    /// </summary>
    public static string LohnartKey(string? code, string? bezeichnung)
    {
        if (!string.IsNullOrWhiteSpace(code)) return code.Trim();
        var b = (bezeichnung ?? "").Trim();
        var i = b.IndexOf('(');
        if (i > 0) b = b[..i].Trim();
        return b.TrimEnd('-', '–', ' ', ':');
    }

    public async Task<LseBuildResult> BuildAsync(int surveyYear, int? companyProfileId)
    {
        var version = await GetVersionAsync(surveyYear)
            ?? throw new InvalidOperationException($"Keine aktive LSE-Version für {surveyYear} konfiguriert.");
        var cfg = JsonDocument.Parse(version.ConfigJson).RootElement;
        var refMonth = cfg.TryGetProperty("referenceMonth", out var rm) ? rm.GetInt32() : 10;
        var mandatory = cfg.TryGetProperty("mandatory", out var md)
            ? md.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet()
            : new HashSet<string>();
        long vnMin = 7560000000001, vnMax = 7569999999999;
        int arMin = 1, arMax = 175, lvMin = 0, lvMax = 99;
        if (cfg.TryGetProperty("ranges", out var rg))
        {
            if (rg.TryGetProperty("vnMin", out var v1)) vnMin = v1.GetInt64();
            if (rg.TryGetProperty("vnMax", out var v2)) vnMax = v2.GetInt64();
            if (rg.TryGetProperty("activityRateMin", out var a1)) arMin = a1.GetInt32();
            if (rg.TryGetProperty("activityRateMax", out var a2)) arMax = a2.GetInt32();
            if (rg.TryGetProperty("leaveMin", out var l1)) lvMin = l1.GetInt32();
            if (rg.TryGetProperty("leaveMax", out var l2)) lvMax = l2.GetInt32();
        }

        var result = new LseBuildResult { SurveyYear = surveyYear, SpecVersion = version.SpecVersion };

        var oktFrom = new DateTime(surveyYear, refMonth, 1);
        var oktTo = new DateTime(surveyYear, refMonth, DateTime.DaysInMonth(surveyYear, refMonth));
        var oktFromD = DateOnly.FromDateTime(oktFrom);
        var oktToD = DateOnly.FromDateTime(oktTo);

        // ── Relevante MA: Vertrag läuft im Referenzmonat ──────────────────
        var employments = await _db.Employments.AsNoTracking()
            .Include(e => e.Employee)
            .Include(e => e.JobGroup)
            .Where(e => e.Employee != null && !e.Employee.IsPayrollExcluded
                     && e.ContractStartDate <= oktTo
                     && (e.ContractEndDate == null || e.ContractEndDate >= oktFrom)
                     && (companyProfileId == null || e.CompanyProfileId == companyProfileId))
            .ToListAsync();
        var proMa = employments
            .GroupBy(e => e.EmployeeId)
            .Select(g => g.OrderByDescending(e => e.ContractStartDate).First())
            .OrderBy(e => e.Employee!.FirstName).ThenBy(e => e.Employee!.LastName)
            .ToList();
        var empIds = proMa.Select(e => e.EmployeeId).ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .ToDictionaryAsync(b => b.Id);

        // ── Snapshots: Oktober + ganzes Jahr (für Jahreswerte) ────────────
        var perioden = await _db.PayrollPerioden.AsNoTracking()
            .Where(p => p.Year == surveyYear)
            .Select(p => new { p.Id, p.Month })
            .ToListAsync();
        var periodeMonth = perioden.ToDictionary(p => p.Id, p => p.Month);
        var periodeIds = perioden.Select(p => p.Id).ToList();
        var snaps = await _db.PayrollSnapshots.AsNoTracking()
            .Where(s => periodeIds.Contains(s.PayrollPeriodeId)
                     && empIds.Contains(s.EmployeeId)
                     && s.Status != "STORNIERT")
            .Select(s => new { s.EmployeeId, s.PayrollPeriodeId, s.SlipJson })
            .ToListAsync();
        var oktSnapByEmp = snaps
            .Where(s => periodeMonth.TryGetValue(s.PayrollPeriodeId, out var m) && m == refMonth)
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var yearSnapsByEmp = snaps
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── Oktober-Ist-Stunden (Stunden-/Lektionslöhner, Spalte AA) ──────
        var oktStunden = (await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => empIds.Contains(t.EmployeeId)
                         && t.EntryDate >= oktFromD && t.EntryDate <= oktToD)
                .ToListAsync())
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => TimeEntryHours.SumAbsolute(g));

        // ── Mappings + Ergänzungsfelder ───────────────────────────────────
        var stichtag = oktToD;
        var lohnartMap = (await _db.LseLohnartMappings.AsNoTracking().ToListAsync())
            .Where(m => m.Confirmed && !string.IsNullOrWhiteSpace(m.BfsKategorie)
                     && (m.GueltigAb == null || m.GueltigAb <= stichtag)
                     && (m.GueltigBis == null || m.GueltigBis >= stichtag))
            .GroupBy(m => m.LohnartCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().BfsKategorie!, StringComparer.OrdinalIgnoreCase);
        var codeMaps = await _db.LseCodeMappings.AsNoTracking().ToListAsync();
        var stellungMap = codeMaps.Where(m => m.MappingTyp == "STELLUNG" && m.Confirmed && m.BfsCode != null)
            .ToDictionary(m => m.SourceCode, m => m.BfsCode!.Value, StringComparer.OrdinalIgnoreCase);
        var vertragMap = codeMaps.Where(m => m.MappingTyp == "VERTRAG" && m.Confirmed && m.BfsCode != null)
            .ToDictionary(m => m.SourceCode, m => m.BfsCode!.Value, StringComparer.OrdinalIgnoreCase);
        var lseByEmp = (await _db.EmployeeLse.AsNoTracking()
                .Where(x => empIds.Contains(x.EmployeeId)).ToListAsync())
            .ToDictionary(x => x.EmployeeId);

        var unmapped = new Dictionary<string, UnmappedLohnart>(StringComparer.OrdinalIgnoreCase);
        var unmappedStellung = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmappedVertrag = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var em in proMa)
        {
            var emp = em.Employee!;
            var branch = em.CompanyProfileId.HasValue && branches.TryGetValue(em.CompanyProfileId.Value, out var b) ? b : null;
            var row = new LseMaRow
            {
                EmployeeId = emp.Id,
                Name = $"{emp.FirstName} {emp.LastName}".Trim(),
                EmployeeNumber = emp.EmployeeNumber ?? "",
                Filiale = branch?.WorkLocation ?? branch?.City ?? "",
                CompanyProfileId = em.CompanyProfileId,
            };
            lseByEmp.TryGetValue(emp.Id, out var lse);

            void Fehler(string feld, string msg) { row.Fehler.Add(msg); row.Werte[feld + "_fehlt"] = true; }
            void Set(string feld, object? wert) => row.Werte[feld] = wert;

            // T vn — AHV-Nummer 13-stellig ohne Punkte (bestehendes Feld).
            var vnDigits = new string((emp.SocialSecurityNumber ?? "").Where(char.IsDigit).ToArray());
            Set("vn", vnDigits);
            if (mandatory.Contains("vn"))
            {
                if (vnDigits.Length != 13 || !long.TryParse(vnDigits, out var vnNum) || vnNum < vnMin || vnNum > vnMax)
                    Fehler("vn", vnDigits.Length == 0 ? "AHV-Nummer fehlt" : $"AHV-Nummer ungültig ({vnDigits})");
            }

            // U education / V universityDegree — LSE-Ergänzungsfelder.
            Set("education", lse?.Education);
            if (mandatory.Contains("education") && lse?.Education == null)
                Fehler("education", "Ausbildung fehlt");
            Set("universityDegree", lse?.UniversityDegree);
            // Hochschultitel nur relevant/verlangt bei Uni/FH (Codes 1–2).
            if (lse?.Education is 1 or 2 && lse.UniversityDegree == null)
                Fehler("universityDegree", "Hochschultitel fehlt (bei Uni/FH-Ausbildung)");

            // W entryDate — bestehendes Eintrittsdatum.
            var entry = emp.EntryDate;
            Set("entryDate", entry?.ToString("yyyy-MM-dd"));
            if (mandatory.Contains("entryDate") && entry == null)
                Fehler("entryDate", "Eintrittsdatum fehlt");

            // X position — Override oder gepflegtes Mapping JobGroup→Stellung.
            var jgCode = em.JobGroup?.Code ?? em.JobTitle ?? "";
            int? position = lse?.PositionOverride;
            if (position == null && jgCode.Length > 0 && stellungMap.TryGetValue(jgCode, out var pos)) position = pos;
            Set("position", position);
            if (position == null)
            {
                if (jgCode.Length > 0) unmappedStellung.Add(jgCode);
                if (mandatory.Contains("position"))
                    Fehler("position", jgCode.Length > 0
                        ? $"Berufliche Stellung: BFS-Zuordnung für Funktion «{jgCode}» fehlt"
                        : "Berufliche Stellung fehlt (keine Funktion hinterlegt)");
            }

            // Y contract — Mapping Vertragsmodell (+Befristung) → BFS-Code.
            var befristet = em.ContractEndDate.HasValue;
            var vertragKey = (em.EmploymentModel ?? "") + (befristet ? "_BEFRISTET" : "");
            int? contract = vertragMap.TryGetValue(vertragKey, out var vc) ? vc : null;
            Set("contract", contract);
            Set("contractKey", vertragKey);
            if (contract == null)
            {
                unmappedVertrag.Add(vertragKey);
                if (mandatory.Contains("contract"))
                    Fehler("contract", $"Vertragsart: BFS-Zuordnung für «{vertragKey}» fehlt");
            }

            // Z basisOfSalaryCalculation — aus dem Vertragsmodell (Monats- vs.
            // Stundenlohn ist im Modell definiert, keine Betrags-Vermutung).
            int basis = em.EmploymentModel is "FIX" or "FIX-M" ? 1 : 2;
            Set("basisOfSalaryCalculation", basis);

            // AA contractualWorkingTime — Monatslohn: vertragliche Wochenstunden;
            // Stundenlohn: effektive Oktober-Stunden.
            decimal? arbeitszeit = null;
            if (basis == 1)
            {
                arbeitszeit = em.WeeklyHours
                    ?? (em.EmploymentPercentage.HasValue && branch?.NormalWeeklyHours != null
                        ? Math.Round(branch.NormalWeeklyHours.Value * em.EmploymentPercentage.Value / 100m, 2)
                        : null);
                if (arbeitszeit == null && mandatory.Contains("contractualWorkingTime"))
                    Fehler("contractualWorkingTime", "Vertragliche Wochenarbeitszeit fehlt");
            }
            else
            {
                arbeitszeit = oktStunden.TryGetValue(emp.Id, out var h) ? Math.Round(h, 2) : 0m;
                if (arbeitszeit == 0m)
                    row.Hinweise.Add("Keine gestempelten Oktober-Stunden (Stundenlohn) — Arbeitszeit 0.00 prüfen");
            }
            Set("contractualWorkingTime", arbeitszeit);

            // AB activityRateOct — vertraglicher Beschäftigungsgrad Oktober.
            var normal = branch?.NormalWeeklyHours ?? 42m;
            int? activityRate = null;
            if (em.EmploymentModel is "FIX" or "FIX-M")
                activityRate = em.EmploymentPercentage.HasValue ? (int)Math.Round(em.EmploymentPercentage.Value) : null;
            else if (em.EmploymentModel == "MTP" && em.GuaranteedHoursPerWeek.HasValue && normal > 0)
                activityRate = (int)Math.Round(em.GuaranteedHoursPerWeek.Value / normal * 100m);
            else if (normal > 0 && oktStunden.TryGetValue(emp.Id, out var ist) && ist > 0)
            {
                activityRate = (int)Math.Round(ist / (normal * 4.33m) * 100m);
                row.Hinweise.Add("Beschäftigungsgrad aus Oktober-Ist-Stunden berechnet (FLEX) — prüfen");
            }
            if (activityRate is < 1) activityRate = 1;
            Set("activityRateOct", activityRate);
            if (activityRate == null)
            {
                if (mandatory.Contains("activityRateOct")) Fehler("activityRateOct", "Beschäftigungsgrad Oktober fehlt");
            }
            else if (activityRate < arMin || activityRate > arMax)
                Fehler("activityRateOct", $"Beschäftigungsgrad {activityRate} ausserhalb {arMin}–{arMax}");

            // AC leaveEntitlement — vertraglicher Jahresanspruch in Tagen:
            // L-GAV 5 Wochen (25 Tage), ab Alter 50 6 Wochen (30 Tage).
            int leave = 25;
            if (emp.DateOfBirth.HasValue)
            {
                var alter = surveyYear - emp.DateOfBirth.Value.Year;
                if (emp.DateOfBirth.Value.Date.AddYears(alter) > oktTo) alter--;
                if (alter >= 50) leave = 30;
            }
            Set("leaveEntitlement", leave);
            if (leave < lvMin || leave > lvMax) Fehler("leaveEntitlement", $"Ferientage {leave} ausserhalb {lvMin}–{lvMax}");

            // AD practicedProfessionOct — Klartext (Ergänzungsfeld, sonst Vorschlag).
            var beruf = lse?.PracticedProfession;
            if (string.IsNullOrWhiteSpace(beruf) && jgCode.Length > 0 && BerufVorschlag.TryGetValue(jgCode, out var vor))
            {
                beruf = vor;
                row.Hinweise.Add($"Beruf aus Funktion vorgeschlagen («{vor}») — bei Bedarf anpassen");
            }
            Set("practicedProfessionOct", beruf);
            if (mandatory.Contains("practicedProfessionOct") && string.IsNullOrWhiteSpace(beruf))
                Fehler("practicedProfessionOct", "Ausgeübter Beruf fehlt");
            else if ((beruf?.Length ?? 0) > 255)
                Fehler("practicedProfessionOct", "Ausgeübter Beruf länger als 255 Zeichen");

            // ── Oktober-Lohndaten AE–AI aus dem Oktober-Snapshot ──────────
            var kat = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var hatOktSnap = oktSnapByEmp.TryGetValue(emp.Id, out var oktSnaps);
            if (hatOktSnap)
                foreach (var s in oktSnaps!)
                    SummiereSlip(s.SlipJson, lohnartMap, kat, unmapped, row.Hinweise);
            else
                row.Hinweise.Add("Kein Oktober-Lohnlauf gefunden — Oktober-Lohnwerte leer");
            decimal Kat(string k) => kat.TryGetValue(k, out var v) ? Math.Round(v, 2) : 0m;
            Set("salaryOct", Kat("GRUNDLOHN"));
            Set("allowancesOct", Kat("ZULAGEN"));
            Set("familyAllowanceOct", Kat("FAMILIENZULAGEN"));
            Set("socialContributionsOct", Kat("SV_AN"));
            Set("bvgLPPRegularContributionsOct", Kat("BVG_AN"));
            if (mandatory.Contains("salaryOct") && !hatOktSnap)
                Fehler("salaryOct", "Grundlohn Oktober fehlt (kein Lohnlauf)");
            if (mandatory.Contains("socialContributionsOct") && !hatOktSnap)
                Fehler("socialContributionsOct", "Sozialversicherungsbeiträge Oktober fehlen (kein Lohnlauf)");

            // AJ from / AK until — Beschäftigungszeitraum im Erhebungsjahr.
            var jahrVon = new DateTime(surveyYear, 1, 1);
            var jahrBis = new DateTime(surveyYear, 12, 31);
            var beschVon = entry.HasValue && entry.Value > jahrVon ? entry.Value : jahrVon;
            var austritt = emp.ExitDate ?? em.ContractEndDate;
            var beschBis = austritt.HasValue && austritt.Value < jahrBis ? austritt.Value : jahrBis;
            Set("from", beschVon.ToString("yyyy-MM-dd"));
            Set("until", beschBis.ToString("yyyy-MM-dd"));

            // AL–AQ Jahreslohnbestandteile aus allen Jahres-Snapshots.
            var jkat = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (yearSnapsByEmp.TryGetValue(emp.Id, out var ySnaps))
                foreach (var s in ySnaps)
                    SummiereSlip(s.SlipJson, lohnartMap, jkat, unmapped, null);
            decimal JKat(string k) => jkat.TryGetValue(k, out var v) ? Math.Round(v, 2) : 0m;
            Set("earnings13th", JKat("DREIZEHNTER"));
            Set("overtime", JKat("UEBERSTUNDEN"));
            Set("irregularPayments", JKat("UNREGELMAESSIG"));
            Set("fringeBenefits", JKat("NEBENLEISTUNGEN"));
            Set("capitalPayments", JKat("KAPITALLEISTUNGEN"));
            Set("othersBenefits", JKat("WEITERE"));

            // AR burNr / AS inHouseID (optional).
            Set("burNr", branch?.BurNr);
            if (string.IsNullOrWhiteSpace(branch?.BurNr))
                row.Hinweise.Add("BUR-Nummer der Filiale nicht hinterlegt (optional, Spalte AR)");
            Set("inHouseID", lse?.InHouseId);

            // Negative Lohnwerte prüfen (BFS erlaubt keine, ausser explizit).
            foreach (var f in new[] { "salaryOct", "allowancesOct", "familyAllowanceOct",
                     "earnings13th", "overtime", "irregularPayments" })
                if (row.Werte.TryGetValue(f, out var w) && w is decimal d && d < 0)
                    Fehler(f, $"Negativer Lohnwert in {f} ({d:0.00})");

            row.Status = row.Fehler.Count > 0 ? "ROT" : row.Hinweise.Count > 0 ? "ORANGE" : "GRUEN";
            result.Rows.Add(row);
        }

        result.UnmappedLohnarten.AddRange(unmapped.Values.OrderByDescending(u => u.Vorkommen));
        result.UnmappedStellung.AddRange(unmappedStellung.OrderBy(x => x));
        result.UnmappedVertrag.AddRange(unmappedVertrag.OrderBy(x => x));
        return result;
    }

    /// <summary>
    /// Summiert die Zeilen eines SlipJson in BFS-Kategorien gemäss bestätigtem
    /// Lohnarten-Mapping. Lohn-/Zulagen-Zeilen positiv, Abzugszeilen als
    /// AN-Beträge (absolut). NICHT gemappte Zeilen → unmapped-Liste
    /// («BFS-Zuordnung fehlt»), niemals automatisch einer Kategorie zuordnen.
    /// </summary>
    private static void SummiereSlip(
        string slipJson,
        IReadOnlyDictionary<string, string> lohnartMap,
        Dictionary<string, decimal> kat,
        Dictionary<string, UnmappedLohnart> unmapped,
        List<string>? hinweise)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(slipJson).RootElement; }
        catch { return; }

        void Zeilen(string prop, bool abzug)
        {
            if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            foreach (var line in arr.EnumerateArray())
            {
                var code = line.TryGetProperty(abzug ? "categoryCode" : "code", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() : null;
                var bez = line.TryGetProperty("bezeichnung", out var bz) && bz.ValueKind == JsonValueKind.String
                    ? bz.GetString() : null;
                var betrag = line.TryGetProperty("betrag", out var bt) && bt.ValueKind == JsonValueKind.Number
                    ? bt.GetDecimal() : 0m;
                if (betrag == 0m) continue;
                var key = LohnartKey(code, bez);
                if (key.Length == 0) continue;
                if (lohnartMap.TryGetValue(key, out var kategorie))
                {
                    if (kategorie == "NICHT_RELEVANT") continue;
                    var wert = abzug ? Math.Abs(betrag) : betrag;
                    kat[kategorie] = (kat.TryGetValue(kategorie, out var v) ? v : 0m) + wert;
                }
                else
                {
                    if (!unmapped.TryGetValue(key, out var u))
                        unmapped[key] = u = new UnmappedLohnart { Key = key, Bezeichnung = bez ?? key };
                    u.Vorkommen++;
                    hinweise?.Add($"BFS-Zuordnung fehlt: «{key}»");
                }
            }
        }
        Zeilen("lohnLines", false);
        Zeilen("zulagenExtraLines", false);
        Zeilen("abzugLines", true);
    }
}
