using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.Personaldaten;

/// <summary>
/// Doppelte AHV-Nummern und doppelte Personen (gleicher Name + Geburtsdatum) über alle MA.
/// Nicht mitgezählt: versteckte MA, «MA ohne Lohn» und Archivnummern (99…, …alt) — die sind
/// bewusst eigene Datensätze derselben Person.
/// </summary>
public sealed class PersonaldatenDuplikate
{
    public sealed record Eintrag(int Id, string Nummer, string Name);

    private readonly Dictionary<string, List<Eintrag>> _ahv = new();
    private readonly Dictionary<string, List<Eintrag>> _person = new();

    public static bool IstArchiv(string? nummer)
    {
        var n = (nummer ?? "").Trim();
        return n.StartsWith("99") || n.EndsWith("alt", StringComparison.OrdinalIgnoreCase);
    }

    public void Hinzufuegen(int id, string? nummer, string? vor, string? nach, DateOnly? geb, string? ahv)
    {
        var e = new Eintrag(id, (nummer ?? "").Trim(), $"{vor} {nach}".Trim());
        var a = PersonaldatenPruefung.Ziffern(ahv);
        if (a.Length == 13) Add(_ahv, a, e);
        if (PersonSchluessel(vor, nach, geb) is string p) Add(_person, p, e);
    }

    private static void Add(Dictionary<string, List<Eintrag>> d, string k, Eintrag e)
    {
        if (!d.TryGetValue(k, out var l)) d[k] = l = new List<Eintrag>();
        l.Add(e);
    }

    /// <summary>Nur Buchstaben und Ziffern, klein. «Anna-Maria» und «Anna Maria» sind derselbe Name.</summary>
    public static string NormName(string? s)
        => new string((s ?? "").Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string? PersonSchluessel(string? vor, string? nach, DateOnly? geb)
    {
        var v = NormName(vor);
        var n = NormName(nach);
        return v.Length == 0 || n.Length == 0 || geb == null ? null : $"{v}|{n}|{geb:yyyy-MM-dd}";
    }

    /// <summary>Befunde für eine Person; <paramref name="eigeneId"/> = sie selbst (NULL bei neuem MA).</summary>
    public List<PdBefund> Pruefe(int? eigeneId, string? ahv, string? vor, string? nach, DateOnly? geb)
    {
        var b = new List<PdBefund>();
        var a = PersonaldatenPruefung.Ziffern(ahv);
        if (a.Length == 13 && _ahv.TryGetValue(a, out var la) && la.FirstOrDefault(x => x.Id != eigeneId) is { } andere)
            b.Add(new("AHV_DOPPELT", "AHV-Nummer", PdStufe.Fehler,
                $"AHV-Nummer {ahv?.Trim()} gehört auch zu Nr. {andere.Nummer} {andere.Name} — doppelt angelegt?", a));
        if (PersonSchluessel(vor, nach, geb) is string p && _person.TryGetValue(p, out var lp)
            && lp.FirstOrDefault(x => x.Id != eigeneId) is { } gleich)
            b.Add(new("PERSON_DOPPELT", "Name", PdStufe.Fehler,
                $"Gleicher Name und Geburtsdatum wie Nr. {gleich.Nummer} {gleich.Name} — doppelt angelegt?", p));
        return b;
    }
}

/// <summary>
/// Lädt die Daten für <see cref="PersonaldatenPruefung"/> aus der Datenbank: Personaldaten,
/// Nationalität, gültige Bewilligung, Familie, Duplikate, Bestätigungen. Für To-do, MA-Maske,
/// Speichern und Import — überall dieselbe Auswertung.
/// </summary>
public class PersonaldatenAuswertung
{
    private readonly AppDbContext _db;
    public PersonaldatenAuswertung(AppDbContext db) => _db = db;

    public static PdPerson AusEmployee(Employee e, string? natCode, string? natUnbekannt, string? bewilligung) => new()
    {
        Vorname = e.FirstName,
        Nachname = e.LastName,
        Geburt = e.DateOfBirth is DateTime g ? DateOnly.FromDateTime(g) : null,
        Geschlecht = e.Gender,
        NationalitaetCode = natCode,
        NationalitaetUnbekannt = natUnbekannt,
        Ahv = e.SocialSecurityNumber,
        Telefon = e.PhoneMobile,
        Telefon2 = e.Phone2,
        Email = e.Email,
        Land = e.Country,
        Plz = e.ZipCode,
        Ort = e.City,
        Strasse = e.Street,
        Eintritt = e.EntryDate is DateTime ein ? DateOnly.FromDateTime(ein) : null,
        Austritt = e.ExitDate is DateTime aus ? DateOnly.FromDateTime(aus) : null,
        BewilligungCode = bewilligung,
    };

    public async Task<Dictionary<int, string>> NationalitaetenAsync(CancellationToken ct)
        => await _db.Nationalities.AsNoTracking().ToDictionaryAsync(n => n.Id, n => (n.Code ?? "").Trim().ToUpperInvariant(), ct);

    /// <summary>Nationalität einer gespeicherten Person: (Code, unbekannter Freitext).</summary>
    public static (string? Code, string? Unbekannt) Nationalitaet(Employee e, IReadOnlyDictionary<int, string> nat)
    {
        if (e.NationalityId is int id && nat.TryGetValue(id, out var c) && c.Length > 0) return (c, null);
        var frei = e.Nationality?.Trim();
        if (string.IsNullOrEmpty(frei)) return (null, null);
        var code = nat.Values.FirstOrDefault(v => string.Equals(v, frei, StringComparison.OrdinalIgnoreCase));
        return code != null ? (code, null) : (null, frei);
    }

    /// <summary>Schweizer PLZ aus dem Ortschaftsverzeichnis; NULL, wenn es nicht geladen ist.</summary>
    public async Task<HashSet<string>?> ChPlzAsync(CancellationToken ct)
    {
        var plz = await _db.SwissLocations.AsNoTracking().Select(l => l.Plz4).Distinct().ToListAsync(ct);
        return plz.Count == 0 ? null : plz.Select(x => x.Trim()).ToHashSet();
    }

    public async Task<PersonaldatenDuplikate> DuplikateAsync(CancellationToken ct)
    {
        var alle = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && !e.IsPayrollExcluded)
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth, e.SocialSecurityNumber })
            .ToListAsync(ct);
        var d = new PersonaldatenDuplikate();
        foreach (var e in alle.Where(e => !PersonaldatenDuplikate.IstArchiv(e.EmployeeNumber)))
            d.Hinzufuegen(e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.DateOfBirth is DateTime g ? DateOnly.FromDateTime(g) : null, e.SocialSecurityNumber);
        return d;
    }

    /// <summary>Gültige Bewilligung (Code) je MA am Stichtag; <paramref name="ids"/> NULL = alle MA.</summary>
    public async Task<Dictionary<int, string>> BewilligungenAsync(IReadOnlyCollection<int>? ids, DateOnly stichtag, CancellationToken ct)
    {
        var q = _db.EmployeePermitHistories.AsNoTracking()
            .Where(p => p.ValidFrom <= stichtag && (p.ValidTo == null || p.ValidTo >= stichtag));
        if (ids != null) q = q.Where(p => ids.Contains(p.EmployeeId));
        var rows = await q
            .Select(p => new { p.EmployeeId, p.ValidFrom, p.Id, Code = p.PermitType != null ? p.PermitType.Code : null })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.EmployeeId)
            .Select(g => g.OrderByDescending(r => r.ValidFrom).ThenByDescending(r => r.Id).First())
            .Where(r => !string.IsNullOrWhiteSpace(r.Code))
            .ToDictionary(r => r.EmployeeId, r => r.Code!.Trim());
    }

    public async Task<HashSet<string>> BestaetigteAsync(int? employeeId, int? eawId, CancellationToken ct)
    {
        var rows = await _db.PersonaldatenBestaetigungen.AsNoTracking()
            .Where(b => (employeeId != null && b.EmployeeId == employeeId) || (eawId != null && b.EasyAtWorkEmployeeId == eawId))
            .Select(b => new { b.Code, b.Wert })
            .ToListAsync(ct);
        return rows.Select(r => r.Code + "|" + r.Wert).ToHashSet();
    }

    public async Task<string?> BenutzerNameAsync(int? userId, CancellationToken ct)
    {
        if (userId == null) return null;
        var u = await _db.AppUsers.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.FirstName, x.LastName, x.Email }).FirstOrDefaultAsync(ct);
        if (u == null) return null;
        var n = $"{u.FirstName} {u.LastName}".Trim();
        return n.Length > 0 ? n : u.Email;
    }

    public async Task BestaetigenAsync(int? employeeId, int? eawId, string code, string wert, string? von, CancellationToken ct)
    {
        var schon = await _db.PersonaldatenBestaetigungen.AnyAsync(b =>
            b.Code == code && b.Wert == wert
            && ((employeeId != null && b.EmployeeId == employeeId) || (eawId != null && b.EasyAtWorkEmployeeId == eawId)), ct);
        if (schon) return;
        _db.PersonaldatenBestaetigungen.Add(new PersonaldatenBestaetigung
        {
            EmployeeId = employeeId,
            EasyAtWorkEmployeeId = eawId,
            Code = code,
            Wert = wert,
            BestaetigtVon = von,
            BestaetigtAm = DateTime.Now,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Speichern eines MA: was die Änderung NEU falsch macht. Fehler = nicht speichern,
    /// Offen = nur nach «Bist du sicher?». «MA ohne Lohn» wird nicht geprüft (keine Lohnmeldung).
    /// </summary>
    public async Task<(List<PdBefund> Fehler, List<PdBefund> Offen)> NeueBeimSpeichernAsync(
        Employee neu, Employee? vorher, CancellationToken ct)
    {
        if (neu.IsPayrollExcluded) return (new(), new());
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var nat = await NationalitaetenAsync(ct);
        string? bew = null;
        if (neu.Id > 0)
            bew = (await BewilligungenAsync(new[] { neu.Id }, heute, ct)).GetValueOrDefault(neu.Id);
        var dup = await DuplikateAsync(ct);
        var chPlz = await ChPlzAsync(ct);
        bool archiv = PersonaldatenDuplikate.IstArchiv(neu.EmployeeNumber);

        List<PdBefund> Alle(Employee e)
        {
            var (c, u) = Nationalitaet(e, nat);
            var p = AusEmployee(e, c, u, bew);
            var l = PersonaldatenPruefung.Pruefe(p, heute, null, chPlz);
            if (!archiv)
                l.AddRange(dup.Pruefe(neu.Id > 0 ? neu.Id : null, e.SocialSecurityNumber, e.FirstName, e.LastName, p.Geburt));
            return l;
        }

        var befunde = PersonaldatenPruefung.NurNeue(Alle(neu), vorher != null ? Alle(vorher) : null);
        var ok = await BestaetigteAsync(neu.Id > 0 ? neu.Id : null, neu.EasyAtWorkEmployeeId, ct);
        return (befunde.Where(b => b.Stufe == PdStufe.Fehler).ToList(),
                PersonaldatenPruefung.OhneBestaetigte(befunde.Where(b => b.Stufe == PdStufe.Bestaetigen), ok));
    }

    /// <summary>Bestätigte Schlüssel aus dem Kopf «X-Personaldaten-Bestaetigt» (kommagetrennt, URL-kodiert).</summary>
    public static HashSet<string> SchluesselAusKopf(string? kopf)
        => (kopf ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToHashSet();

    /// <summary>Alle offenen Befunde (ohne bestätigte) je MA.</summary>
    public async Task<Dictionary<int, List<PdBefund>>> BefundeAsync(IReadOnlyCollection<int> ids, CancellationToken ct,
        PersonaldatenDuplikate? duplikate = null)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var emps = await _db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        var nat = await NationalitaetenAsync(ct);
        var bew = await BewilligungenAsync(ids, heute, ct);
        duplikate ??= await DuplikateAsync(ct);
        var chPlz = await ChPlzAsync(ct);
        var fam = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => ids.Contains(f.EmployeeId))
            .Select(f => new { f.EmployeeId, f.MemberType, f.FirstName, f.DateOfBirth, f.SocialSecurityNumber })
            .ToListAsync(ct);
        var best = await _db.PersonaldatenBestaetigungen.AsNoTracking()
            .Where(b => b.EmployeeId != null && ids.Contains(b.EmployeeId.Value))
            .Select(b => new { b.EmployeeId, b.Code, b.Wert })
            .ToListAsync(ct);
        var eawBest = await _db.PersonaldatenBestaetigungen.AsNoTracking()
            .Where(b => b.EasyAtWorkEmployeeId != null)
            .Select(b => new { b.EasyAtWorkEmployeeId, b.Code, b.Wert })
            .ToListAsync(ct);

        var res = new Dictionary<int, List<PdBefund>>();
        foreach (var e in emps)
        {
            var (code, unbekannt) = Nationalitaet(e, nat);
            var person = AusEmployee(e, code, unbekannt, bew.GetValueOrDefault(e.Id));
            var befunde = PersonaldatenPruefung.Pruefe(person, heute, null, chPlz);
            if (!e.IsPayrollExcluded && !PersonaldatenDuplikate.IstArchiv(e.EmployeeNumber))
                befunde.AddRange(duplikate.Pruefe(e.Id, e.SocialSecurityNumber, e.FirstName, e.LastName, person.Geburt));
            foreach (var f in fam.Where(f => f.EmployeeId == e.Id))
                befunde.AddRange(PersonaldatenPruefung.PruefeFamilie(new PdFamilie
                {
                    Art = f.MemberType,
                    Vorname = f.FirstName,
                    Geburt = f.DateOfBirth is DateTime fg ? DateOnly.FromDateTime(fg) : null,
                    Ahv = f.SocialSecurityNumber,
                }, person.Geburt, heute));

            var ok = best.Where(b => b.EmployeeId == e.Id).Select(b => b.Code + "|" + b.Wert)
                .Concat(eawBest.Where(b => e.EasyAtWorkEmployeeId != null && b.EasyAtWorkEmployeeId == e.EasyAtWorkEmployeeId)
                               .Select(b => b.Code + "|" + b.Wert))
                .ToHashSet();
            var offen = PersonaldatenPruefung.OhneBestaetigte(befunde, ok);
            if (offen.Count > 0) res[e.Id] = offen;
        }
        return res;
    }
}
