using System.Globalization;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Elm;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Anmeldung Familienzulagen → Angaben in OneCrew nachtragen (Walter 11.10.2026).
/// Nur Felder, die in OneCrew LEER sind, werden gefüllt — nie überschrieben.
/// Bewusst nicht: Name, Adresse, Nationalität, Bewilligung, Arbeitgeber-Zeilen
/// (Historie/Zusammensetzung → Familie-Maske). Alles oder nichts: ein
/// ungültiger Wert ⇒ nichts gespeichert.
/// </summary>
public class FamzRueckschreiben
{
    /// <summary>Feld-Schlüssel ohne Präfix, die an einem Elternteil gespeichert werden.</summary>
    public static readonly string[] ElternteilFelder =
    {
        "geschlecht", "geburtsdatum", "ahvNummer", "zivilstand", "angestellt", "arbeitsortKanton",
        "arbeitspensum", "einkommenHoeher", "einkommenUeberMinimum", "seNe", "ausgleichskasse",
        "leistungBezug", "leistungVon1",
    };
    public static readonly string[] KindFelder = { "geschlecht", "geburtsdatum", "beziehung" };
    public static readonly string[] AntragstellerFelder = { "zivilstandSeit", "ahvNummer" };

    static readonly string[] Zivilstaende =
        { "ledig", "verheiratet", "getrennt", "geschieden", "verwitwet", "eingetragene_partnerschaft", "aufgeloeste_partnerschaft" };

    /// <summary>Ist dieser Formular-Pfad grundsätzlich speicherbar? (gleiche Liste wie im Browser)</summary>
    public static bool Speicherbar(string pfad)
    {
        var t = pfad.Split('.');
        return t.Length switch
        {
            1 => t[0] is "sorge" or "lebenBei",
            2 when t[0] == "antragsteller" => AntragstellerFelder.Contains(t[1]),
            2 when t[0] is "andererElternteil" or "partner" => ElternteilFelder.Contains(t[1]),
            3 when t[0] == "kinder" && int.TryParse(t[1], out _) => KindFelder.Contains(t[2]),
            _ => false,
        };
    }

    public sealed record Ergebnis(List<string> Gespeichert, List<string> Fehler);

    private readonly AppDbContext _db;
    public FamzRueckschreiben(AppDbContext db) { _db = db; }

    public async Task<Ergebnis> SpeichernAsync(int empId, FamzAnmeldungDto dto, IEnumerable<string> felder)
    {
        var gespeichert = new List<string>();
        var fehler = new List<string>();
        var pfade = felder.Where(Speicherbar).Distinct().ToList();
        if (pfade.Count == 0) return new(gespeichert, fehler);

        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId)
                ?? throw new KeyNotFoundException("EMP_NOT_FOUND");
        var familie = await _db.EmployeeFamilyMembers.Where(m => m.EmployeeId == empId).ToListAsync();
        var neueErwerb = new List<FamilyMemberErwerbHistory>();
        var geaendert = new HashSet<EmployeeFamilyMember>();

        foreach (var pfad in pfade)
        {
            var t = pfad.Split('.');
            int vorher = gespeichert.Count;
            if (t.Length == 1)
                KinderGemeinsam(t[0], dto, familie, gespeichert, geaendert);
            else if (t[0] == "antragsteller")
                Antragsteller(e, dto.Antragsteller, t[1], gespeichert, fehler);
            else if (t[0] is "andererElternteil" or "partner")
            {
                var p = t[0] == "partner" ? dto.Partner : dto.AndererElternteil;
                var m = p?.FamilyMemberId is int id ? familie.FirstOrDefault(x => x.Id == id) : null;
                if (p == null || m == null || !FamzEltern.Contains(m.MemberType)) continue;
                Elternteil(m, p, t[1], gespeichert, fehler, neueErwerb);
                if (gespeichert.Count > vorher) geaendert.Add(m);
            }
            else
            {
                var i = int.Parse(t[1]);
                var k = dto.Kinder != null && i < dto.Kinder.Count ? dto.Kinder[i] : null;
                var m = k?.FamilyMemberId is int id ? familie.FirstOrDefault(x => x.Id == id && x.MemberType == "Kind") : null;
                if (k == null || m == null) continue;
                Kind(m, k, t[2], gespeichert, fehler);
                if (gespeichert.Count > vorher) geaendert.Add(m);
            }
        }

        // Alles oder nichts: ohne SaveChanges bleibt die Datenbank unberührt.
        if (fehler.Count > 0) return new(new List<string>(), fehler);
        foreach (var m in geaendert) m.UpdatedAt = DateTime.Now;
        _db.FamilyMemberErwerbHistories.AddRange(neueErwerb);
        await _db.SaveChangesAsync();
        return new(gespeichert, fehler);
    }

    static readonly string[] FamzEltern = { "Ehepartner", "Konkubinatspartner", "AndererElternteil" };

    static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static DateOnly? Datum(string? s)
    {
        s = Leer(s);
        if (s == null) return null;
        return DateOnly.TryParseExact(s, new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    /// <summary>AHV-Nummer prüfen und als 756.xxxx.xxxx.xx formatieren; null = ungültig.</summary>
    public static string? Ahv(string? s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        if (d.Length != 13 || !d.StartsWith("756") || !ElmPlausibilitaet.AhvPruefzifferStimmt(d)) return null;
        return $"{d[..3]}.{d.Substring(3, 4)}.{d.Substring(7, 4)}.{d.Substring(11, 2)}";
    }

    static string? Geschlecht(string? s) => Leer(s)?.ToUpperInvariant() switch
    {
        "F" => "Weiblich",
        "M" => "Männlich",
        _ => null,
    };

    static void Antragsteller(Employee e, FamzPersonDto? p, string feld, List<string> ok, List<string> fehler)
    {
        if (p == null) return;
        switch (feld)
        {
            case "zivilstandSeit":
            {
                var zivil = (e.MaritalStatus ?? "").Trim().ToLowerInvariant();
                if (zivil is "" or "ledig") return;
                bool getrennt = zivil == "getrennt";
                if ((getrennt ? e.SeparatedSince : e.MaritalStatusSince) != null || Leer(p.ZivilstandSeit) == null) return;
                var d = Datum(p.ZivilstandSeit);
                if (d == null || d > DateOnly.FromDateTime(DateTime.Today)) { fehler.Add($"Zivilstand seit «{p.ZivilstandSeit}» ist kein gültiges Datum."); return; }
                if (getrennt) e.SeparatedSince = d; else e.MaritalStatusSince = d;
                ok.Add($"Zivilstand seit {d:dd.MM.yyyy}");
                break;
            }
            case "ahvNummer":
            {
                if (Leer(e.SocialSecurityNumber) != null || Leer(p.AhvNummer) == null) return;
                var a = Ahv(p.AhvNummer);
                if (a == null) { fehler.Add($"AHV-Nr. «{p.AhvNummer}» ist ungültig (756.xxxx.xxxx.xx, Prüfziffer)."); return; }
                e.SocialSecurityNumber = a;
                ok.Add($"AHV-Nr. {a}");
                break;
            }
        }
    }

    static void Elternteil(EmployeeFamilyMember m, FamzElternteilDto p, string feld, List<string> ok, List<string> fehler,
        List<FamilyMemberErwerbHistory> neueErwerb)
    {
        string wer = m.FirstName ?? "Elternteil";
        switch (feld)
        {
            case "geschlecht":
                if (Leer(m.Gender) != null) return;
                if (Geschlecht(p.Geschlecht) is { } g) { m.Gender = g; ok.Add($"{wer}: Geschlecht"); }
                break;
            case "geburtsdatum":
                if (m.DateOfBirth != null || Leer(p.Geburtsdatum) == null) return;
                if (Datum(p.Geburtsdatum) is not { } gd || gd > DateOnly.FromDateTime(DateTime.Today))
                { fehler.Add($"{wer}: Geburtsdatum «{p.Geburtsdatum}» ist ungültig."); return; }
                m.DateOfBirth = gd.ToDateTime(TimeOnly.MinValue);
                ok.Add($"{wer}: Geburtsdatum");
                break;
            case "ahvNummer":
                if (Leer(m.SocialSecurityNumber) != null || Leer(p.AhvNummer) == null) return;
                if (Ahv(p.AhvNummer) is not { } a) { fehler.Add($"{wer}: AHV-Nr. «{p.AhvNummer}» ist ungültig (756.xxxx.xxxx.xx, Prüfziffer)."); return; }
                m.SocialSecurityNumber = a;
                ok.Add($"{wer}: AHV-Nr.");
                break;
            case "zivilstand":
                if (Leer(m.FamilyStatus) != null) return;
                var z = Leer(p.Zivilstand)?.ToLowerInvariant();
                if (z != null && Zivilstaende.Contains(z)) { m.FamilyStatus = z; ok.Add($"{wer}: Zivilstand"); }
                break;
            case "angestellt":
                if (m.Erwerbstaetig != null || p.Angestellt == null) return;
                m.Erwerbstaetig = p.Angestellt;
                neueErwerb.Add(new FamilyMemberErwerbHistory
                {
                    FamilyMemberId = m.Id,
                    Erwerbstaetig = p.Angestellt,
                    ValidFrom = DateOnly.FromDateTime(DateTime.Today),
                    Note = "Ersterfassung (Anmeldung Familienzulagen)",
                    CreatedAt = DateTime.Now,
                });
                ok.Add($"{wer}: angestellt {(p.Angestellt == true ? "ja" : "nein")}");
                break;
            case "arbeitsortKanton":
                if (Leer(m.ArbeitgeberKanton) != null || Leer(p.ArbeitsortKanton) == null) return;
                var kt = p.ArbeitsortKanton!.Trim().ToUpperInvariant();
                if (kt.Length != 2 || !kt.All(char.IsLetter)) { fehler.Add($"{wer}: Arbeitsort Kanton «{p.ArbeitsortKanton}» — bitte als Kürzel (z.B. ZG)."); return; }
                m.ArbeitgeberKanton = kt;
                ok.Add($"{wer}: Arbeitsort Kanton");
                break;
            case "arbeitspensum":
                if (m.Arbeitspensum != null || Leer(p.Arbeitspensum) == null) return;
                var txt = p.Arbeitspensum!.Replace("%", "").Replace(',', '.').Trim();
                if (!decimal.TryParse(txt, NumberStyles.Number, CultureInfo.InvariantCulture, out var pensum) || pensum < 0 || pensum > 100)
                { fehler.Add($"{wer}: Pensum «{p.Arbeitspensum}» — Zahl zwischen 0 und 100."); return; }
                m.Arbeitspensum = pensum;
                ok.Add($"{wer}: Pensum {pensum:0.##} %");
                break;
            case "einkommenHoeher":
                if (m.MaHatHoeheresEinkommen != null || p.EinkommenHoeher == null) return;
                m.MaHatHoeheresEinkommen = !p.EinkommenHoeher.Value;
                ok.Add($"{wer}: verdient mehr als MA {(p.EinkommenHoeher.Value ? "ja" : "nein")}");
                break;
            case "einkommenUeberMinimum":
                if (m.EinkommenUeberMinimum != null || p.EinkommenUeberMinimum == null) return;
                m.EinkommenUeberMinimum = p.EinkommenUeberMinimum;
                ok.Add($"{wer}: Einkommen über 7'560/Jahr {(p.EinkommenUeberMinimum.Value ? "ja" : "nein")}");
                break;
            case "seNe":
                if (Leer(m.SeNeAngemeldet) != null) return;
                var se = Leer(p.SeNe)?.ToUpperInvariant();
                if (se is "NEIN" or "SE" or "NE") { m.SeNeAngemeldet = se; ok.Add($"{wer}: SE/NE"); }
                break;
            case "ausgleichskasse":
                if (Leer(m.SeNeAusgleichskasse) != null || Leer(p.Ausgleichskasse) == null) return;
                if ((Leer(p.SeNe)?.ToUpperInvariant() ?? m.SeNeAngemeldet) is not ("SE" or "NE")) return;
                m.SeNeAusgleichskasse = p.Ausgleichskasse!.Trim();
                ok.Add($"{wer}: Ausgleichskasse");
                break;
            case "leistungBezug":
                if (m.BeziehtLeistung != null || p.LeistungBezug == null) return;
                m.BeziehtLeistung = p.LeistungBezug;
                ok.Add($"{wer}: bezieht Leistungen {(p.LeistungBezug.Value ? "ja" : "nein")}");
                break;
            case "leistungVon1":
                if (Leer(m.LeistungVon) != null || Leer(p.LeistungVon1) == null) return;
                if ((p.LeistungBezug ?? m.BeziehtLeistung) != true) return;
                m.LeistungVon = p.LeistungVon1!.Trim();
                ok.Add($"{wer}: Leistungen von");
                break;
        }
    }

    /// <summary>
    /// Formular-Angaben, die für alle Kinder gelten. «sorge» füllt die elterliche Sorge
    /// jedes Kindes, bei dem sie leer ist. «lebenBei» = beide Eltern verknüpft Kinder
    /// im Haushalt ohne anderen Elternteil mit dem anderen Elternteil des Formulars —
    /// nur wenn dieser selbst im Haushalt lebt (sonst widerspräche es «beide Eltern»).
    /// Mutter/Vater allein ergibt sich aus dem Haushalt und wird nicht gespeichert.
    /// </summary>
    static void KinderGemeinsam(string feld, FamzAnmeldungDto dto, List<EmployeeFamilyMember> familie,
        List<string> ok, HashSet<EmployeeFamilyMember> geaendert)
    {
        var kinder = (dto.Kinder ?? new())
            .Select(k => k.FamilyMemberId is int id ? familie.FirstOrDefault(x => x.Id == id && x.MemberType == "Kind") : null)
            .Where(k => k != null).Select(k => k!).ToList();
        if (feld == "sorge")
        {
            var s = Leer(dto.Sorge)?.ToUpperInvariant();
            if (s == null || !HrSystem.Controllers.EmployeeFamilyMembersController.Sorgen.Contains(s)) return;
            foreach (var k in kinder.Where(k => Leer(k.ElterlicheSorge) == null))
            {
                k.ElterlicheSorge = s;
                geaendert.Add(k);
                ok.Add($"{k.FirstName}: elterliche Sorge");
            }
            return;
        }
        if (Leer(dto.LebenBei)?.ToUpperInvariant() != "ELTERN") return;
        var ae = dto.AndererElternteil?.FamilyMemberId is int aeId
            ? familie.FirstOrDefault(x => x.Id == aeId && FamzEltern.Contains(x.MemberType)) : null;
        if (ae == null || !ae.LebtImHaushalt || ae.AlternativeAddressId != null) return;
        foreach (var k in kinder.Where(k => k.AndererElternteilId == null && k.LebtImHaushalt && k.AlternativeAddressId == null))
        {
            k.AndererElternteilId = ae.Id;
            k.GemeinsamesKindMitPartner = ae.MemberType == "Konkubinatspartner";
            geaendert.Add(k);
            ok.Add($"{k.FirstName}: anderer Elternteil {ae.FirstName}");
        }
    }

    static void Kind(EmployeeFamilyMember m, FamzKindDto k, string feld, List<string> ok, List<string> fehler)
    {
        string wer = m.FirstName ?? "Kind";
        switch (feld)
        {
            case "geschlecht":
                if (Leer(m.Gender) != null) return;
                if (Geschlecht(k.Geschlecht) is { } g) { m.Gender = g; ok.Add($"{wer}: Geschlecht"); }
                break;
            case "geburtsdatum":
                if (m.DateOfBirth != null || Leer(k.Geburtsdatum) == null) return;
                if (Datum(k.Geburtsdatum) is not { } gd || gd > DateOnly.FromDateTime(DateTime.Today))
                { fehler.Add($"{wer}: Geburtsdatum «{k.Geburtsdatum}» ist ungültig."); return; }
                m.DateOfBirth = gd.ToDateTime(TimeOnly.MinValue);
                ok.Add($"{wer}: Geburtsdatum");
                break;
            case "beziehung":
                if (Leer(m.BeziehungZumMa) != null) return;
                var b = Leer(k.Beziehung)?.ToUpperInvariant();
                if (b != null && HrSystem.Controllers.EmployeeFamilyMembersController.Beziehungen.Contains(b))
                { m.BeziehungZumMa = b; ok.Add($"{wer}: Beziehung zum Kind"); }
                break;
        }
    }
}
