using System.Globalization;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Übernahme der Gesprächsdaten in den Mitarbeiter (Walter 03.09.2026):
/// «GF sendet an HR → Gespräch verschwindet beim GF → HR bearbeitet weiter →
/// daraus entsteht der Mitarbeiter.» Beim Verknüpfen des Kandidaten mit dem
/// (aus easy@work importierten) MA werden die strukturierten Antworten aus
/// dem Bewerbungsgespräch in die Personalakte übernommen — NUR in leere
/// Felder, damit der easy@work-Import und Handpflege nie überschrieben
/// werden. Zusätzlich: Bankverbindung, Partner und Kinder, falls noch keine
/// erfasst sind.
/// </summary>
public class GespraechUebernahmeService
{
    private readonly AppDbContext _db;
    public GespraechUebernahmeService(AppDbContext db) => _db = db;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public sealed record Ergebnis(int Felder, bool Bank, int Familie, List<string> Uebernommen);

    public async Task<Ergebnis?> UebernehmenAsync(int employeeId, int gespraechId, string? actor)
    {
        var g = await _db.Bewerbungsgespraeche.AsNoTracking().FirstOrDefaultAsync(x => x.Id == gespraechId);
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (g == null || emp == null) return null;

        Dictionary<string, JsonElement> a;
        try { a = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(g.AntwortenJson, JsonOpts) ?? new(); }
        catch { a = new(); }

        string? S(string k) => a.TryGetValue(k, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "ja",
            JsonValueKind.False => "nein",
            _ => null
        } : null;
        bool? B(string k) => a.TryGetValue(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        DateTime? D(string k) => DateTime.TryParseExact(S(k), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        DateOnly? DO(string k) => DateOnly.TryParseExact(S(k), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        var log = new List<string>();
        int felder = 0;
        void Set<T>(string label, T? aktuell, T? neu, Action<T> setter) where T : class
        {
            if (neu == null) return;
            if (aktuell != null && !(aktuell is string s && string.IsNullOrWhiteSpace(s))) return;
            setter(neu); felder++; log.Add(label);
        }

        // ── Personalien (nur leere Felder) ──
        Set("Geburtsdatum", emp.DateOfBirth.HasValue ? "x" : null, D("geburtsdatum")?.ToString("o"), _ => emp.DateOfBirth = D("geburtsdatum"));
        Set("Geschlecht", emp.Gender, GeschlechtCode(S("geschlecht")), v => emp.Gender = v);
        Set("Strasse", emp.Street, S("adresse"), v => emp.Street = v);
        Set("PLZ", emp.ZipCode, S("plz"), v => emp.ZipCode = v);
        Set("Ort", emp.City, S("ort"), v => emp.City = v);
        if (string.IsNullOrWhiteSpace(emp.Country)) emp.Country = "CH";
        Set("Telefon", emp.PhoneMobile, S("mobile"), v => emp.PhoneMobile = v);
        Set("E-Mail", emp.Email, S("email"), v => emp.Email = v);
        Set("Zivilstand", emp.MaritalStatus, ZivilstandCode(S("zivilstand")), v => emp.MaritalStatus = v);
        if (emp.MaritalStatusSince == null && DO("zivilstand_seit") != null) { emp.MaritalStatusSince = DO("zivilstand_seit"); felder++; log.Add("Zivilstand seit"); }
        Set("AHV-Nummer", emp.SocialSecurityNumber, AhvNorm(S("ahv")), v => emp.SocialSecurityNumber = v);
        Set("Konfession", emp.Religion, KonfessionCode(S("konfession")), v => emp.Religion = v);
        Set("Anrede", emp.Salutation, S("geschlecht") == "Weiblich" ? "Frau" : (S("geschlecht") == "Männlich" ? "Herr" : null), v => emp.Salutation = v);

        // Nationalität: Name aus dem Gespräch → nationality-Tabelle
        var natName = S("nationalitaet");
        if (emp.NationalityId == null && !string.IsNullOrWhiteSpace(natName))
        {
            var nn = natName.ToLowerInvariant();
            var nat = await _db.Nationalities.AsNoTracking()
                .Where(n => n.IsActive && n.NameDe != null && n.NameDe.ToLower() == nn)
                .FirstOrDefaultAsync()
                ?? (nn is "ch" or "schweiz" or "schweizer" or "schweizerin"
                    ? await _db.Nationalities.AsNoTracking().FirstOrDefaultAsync(n => n.Code == "CH")
                    : null);
            if (nat != null) { emp.NationalityId = nat.Id; felder++; log.Add("Nationalität"); }
        }

        // Bewilligung
        var bew = S("bewilligung");
        if (emp.PermitTypeId == null && !string.IsNullOrWhiteSpace(bew))
        {
            var pt = await _db.PermitTypes.AsNoTracking().FirstOrDefaultAsync(p => p.Code == bew);
            if (pt != null)
            {
                emp.PermitTypeId = pt.Id; felder++; log.Add("Bewilligung");
                var bis = DO("bewilligung_bis");
                var hatHistory = await _db.EmployeePermitHistories.AnyAsync(h => h.EmployeeId == emp.Id);
                if (!hatHistory)
                    _db.EmployeePermitHistories.Add(new EmployeePermitHistory
                    {
                        EmployeeId = emp.Id,
                        PermitTypeId = pt.Id,
                        ValidFrom = DateOnly.FromDateTime(DateTime.Today),
                        ValidTo = bis,
                        Note = "aus Bewerbungsgespräch übernommen",
                        CreatedAt = DateTime.Now,
                    });
            }
        }

        // Notfallkontakt: gesetzlicher Vertreter (Minderjährige) als freie Person
        if (string.IsNullOrWhiteSpace(emp.NotfallName) && emp.NotfallFamilyMemberId == null
            && !string.IsNullOrWhiteSpace(S("vertreter_name")) && !string.IsNullOrWhiteSpace(S("vertreter_telefon")))
        {
            emp.NotfallName = S("vertreter_name");
            emp.NotfallBeziehung = "Gesetzl. Vertreter";
            emp.NotfallTelefon = S("vertreter_telefon");
            felder++; log.Add("Notfallkontakt");
        }

        // ── Bankverbindung ──
        bool bank = false;
        var iban = (S("iban") ?? "").Replace(" ", "").ToUpperInvariant();
        if (iban.Length >= 15 && !await _db.EmployeeBankAccounts.AnyAsync(b => b.EmployeeId == emp.Id))
        {
            _db.EmployeeBankAccounts.Add(new EmployeeBankAccount
            {
                EmployeeId = emp.Id,
                Iban = iban,
                BankName = S("bank"),
                Kontoinhaber = $"{emp.FirstName} {emp.LastName}".Trim(),
                IsHauptbank = true,
                AufteilungTyp = "VOLL",
                ValidFrom = DateOnly.FromDateTime(DateTime.Today),
                Bemerkung = "aus Bewerbungsgespräch übernommen",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
            });
            bank = true; log.Add("Bankverbindung");
        }

        // ── Familie: Partner + Kinder (nur wenn noch keine Familie erfasst) ──
        int fam = 0;
        if (!await _db.EmployeeFamilyMembers.AnyAsync(f => f.EmployeeId == emp.Id))
        {
            var zivil = ZivilstandCode(S("zivilstand"));
            var pName = S("partner_nachname"); var pVor = S("partner_vorname");
            if ((zivil is "verheiratet" or "eingetragene_partnerschaft" or "getrennt") && (pName != null || pVor != null))
            {
                _db.EmployeeFamilyMembers.Add(new EmployeeFamilyMember
                {
                    EmployeeId = emp.Id,
                    MemberType = "Ehepartner",
                    LastName = pName,
                    FirstName = pVor,
                    Gender = GeschlechtCode(S("partner_geschlecht")),
                    SocialSecurityNumber = AhvNorm(S("partner_ahv")),
                    LivesInSwitzerland = true,
                    LebtImHaushalt = zivil != "getrennt",
                    Erwerbstaetig = B("partner_arbeitet"),
                    ArbeitgeberName = S("partner_arbeitgeber"),
                    Stellenantritt = D("partner_stellenantritt"),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                });
                fam++;
            }
            if (a.TryGetValue("kinder", out var kinder) && kinder.ValueKind == JsonValueKind.Array)
            {
                foreach (var kind in kinder.EnumerateArray())
                {
                    if (kind.ValueKind != JsonValueKind.Object) continue;
                    string? K(string p) => kind.TryGetProperty(p, out var pv) && pv.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pv.GetString()) ? pv.GetString()!.Trim() : null;
                    if (K("vorname") == null && K("nachname") == null && K("geburtsdatum") == null) continue;
                    _db.EmployeeFamilyMembers.Add(new EmployeeFamilyMember
                    {
                        EmployeeId = emp.Id,
                        MemberType = "Kind",
                        LastName = K("nachname") ?? emp.LastName,
                        FirstName = K("vorname"),
                        Gender = K("geschlecht") == "W" ? "female" : (K("geschlecht") == "M" ? "male" : null),
                        DateOfBirth = DateTime.TryParseExact(K("geburtsdatum"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var gd) ? gd : null,
                        LebtImHaushalt = K("haushalt") != "nein",
                        LivesInSwitzerland = K("ch") != "nein",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                    });
                    fam++;
                }
            }
            if (fam > 0) log.Add($"Familie ({fam})");
        }

        await _db.SaveChangesAsync();
        return new Ergebnis(felder, bank, fam, log);
    }

    private static string? GeschlechtCode(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "weiblich" or "w" or "female" => "female",
        "männlich" or "m" or "male" => "male",
        _ => null
    };

    private static string? ZivilstandCode(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "ledig" => "ledig",
        "konkubinat" => "konkubinat",
        "verheiratet" => "verheiratet",
        "geschieden" => "geschieden",
        "verwitwet" => "verwitwet",
        "getrennt" => "getrennt",
        "eingetragene partnerschaft" => "eingetragene_partnerschaft",
        _ => null
    };

    private static string? KonfessionCode(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "evang.-reformiert" => "evangelisch_reformiert",
        "röm.-katholisch" => "roemisch_katholisch",
        "christ-katholisch" => "christ_katholisch",
        "israelitisch" => "israelitisch",
        "andere" => "andere",
        "keine" => "keine",
        _ => null
    };

    private static string? AhvNorm(string? s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        if (d.Length != 13) return null;
        return $"{d[..3]}.{d.Substring(3, 4)}.{d.Substring(7, 4)}.{d.Substring(11, 2)}";
    }
}
