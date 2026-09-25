using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.DokumentAblage;

/// <summary>
/// Ablage nach Angabe statt nach Ordner (Walter 25.09.2026, Konzept
/// docs/dokument-ablage-konzept.md). Beim Hochladen in der Dokumentverwaltung
/// wählt man zuerst, WOFÜR das Dokument ist (Ausweis, AHV-Karte, Vertrag X,
/// Absenz Y …); die Kategorie ergibt sich daraus im Hintergrund und wird erst
/// am Schluss angezeigt.
///
/// Die EINZIGE Liste der Ablageziele steht hier. Jedes Ziel kennt:
///   • das Feld, das es setzt (Verknüpfung am MA, am Vertrag, an der Absenz …)
///   • den Feld-Code (<see cref="DokumentTyp.LinkedFieldCode"/>), über den die
///     Kategorie gefunden wird — NIE über den Namen des Typs.
///
/// Regeln (Walter 25.09.2026):
///   • Angaben OHNE Historie (Ausweis, AHV-Karte, Kündigung …): neues Dokument ersetzt das
///     alte ohne Rückfrage; das alte bleibt in den Dokumenten, nur unverknüpft.
///   • Angaben MIT Historie (Bewilligung, Vertrag, Bank, Absenz): das Dokument
///     gehört zu genau einem Eintrag und bleibt dort. «Ersetzen» heisst nur
///     noch: dasselbe Dokument für denselben Eintrag austauschen.
///   • Mehrfachauswahl: ein Dokument darf an mehreren Angaben hängen
///     (z.B. ein Scan mit Ausweis und AHV-Karte). Formulare («neue Bewilligung»
///     usw.), das Foto und «Anderes» stehen jeweils allein.
/// </summary>
public class DokumentAblageService
{
    private readonly AppDbContext _db;
    public DokumentAblageService(AppDbContext db) => _db = db;

    /// <summary>
    /// Die Arten von Ablagezielen. <see cref="Codes"/> sind die Feld-Codes, über
    /// die der Dokument-Typ gesucht wird (der erste gefundene gewinnt).
    /// </summary>
    public record Art(string Schluessel, string Gruppe, string Label, string[] Codes,
                      bool Historie = false, bool Formular = false, bool NurBild = false, bool Anderes = false);

    public static readonly IReadOnlyList<Art> Arten = new List<Art>
    {
        // ── Mitarbeiter/in (ohne Historie → ersetzen) ──
        new("ausweis",              "Mitarbeiter/in", "Ausweis",                       new[] { "passport", "id_card" }),
        new("ahv_karte",            "Mitarbeiter/in", "AHV-Karte",                     new[] { "ahv_card" }),
        new("geburtsurkunde",       "Mitarbeiter/in", "Geburtsurkunde",                new[] { "birth_cert" }),
        new("zivilstand",           "Mitarbeiter/in", "Zivilstandsdokument",           new[] { "marriage_cert" }),
        new("foto",                 "Mitarbeiter/in", "Mitarbeiterfoto",               new[] { "employee_photo" }, Formular: true, NurBild: true),
        new("nachtarbeit_zeugnis",  "Mitarbeiter/in", "Nachtarbeit: Arztzeugnis",      new[] { "night_work_exam" }),
        new("nachtarbeit_ausnahme", "Mitarbeiter/in", "Nachtarbeit: Ausnahmeregelung", new[] { "night_work_ausnahme" }),
        // ── mit Historie ──
        new("bank",                 "Bank",           "Bankbeleg",                     new[] { "bank_card" }, Historie: true),
        new("bank_neu",             "Bank",           "Bankbeleg neue Bank",           new[] { "bank_card" }, Formular: true),
        new("vertrag",              "Vertrag",        "Unterschriebener Vertrag",      new[] { "contract" }, Historie: true),
        // Kündigung (Walter 25.09.2026): gehört zur aktuellen Kündigung → ersetzen wie ohne Historie.
        new("kuendigung",           "Kündigung",      "Kündigung",                     new[] { "termination" }),
        new("absenz_neu",           "Absenz",         "Neue Absenz erfassen",          new[] { "absence" }, Formular: true),
        new("absenz",               "Absenz",         "Absenz",                        new[] { "absence" }, Historie: true),
        new("bewilligung_neu",      "Bewilligung",    "Neue Bewilligung",              new[] { "permit" }, Formular: true),
        new("bewilligung",          "Bewilligung",    "Bestehende Bewilligung",          new[] { "permit" }, Historie: true),
        // ── Familie (je Person ein Feld, ohne Historie) ──
        new("ausweis_partner",      "Familie",        "Ausweis Partner/in",            new[] { "spouse" }),
        new("ausweis_kind",         "Familie",        "Ausweis Kind",                  new[] { "child_id" }),
        new("geburtsurkunde_kind",  "Familie",        "Geburtsurkunde Kind",           new[] { "birth_cert" }),
        // ── Anderes: kurze Liste statt der ganzen Dokumentstruktur ──
        new("anderes:korrespondenz",  "Anderes", "Korrespondenz",               new[] { "andere_korrespondenz" }, Anderes: true),
        new("anderes:arztzeugnis",    "Anderes", "Arztzeugnis (ohne Absenz)",   new[] { "andere_arztzeugnis" }, Anderes: true),
        new("anderes:lohn",           "Anderes", "Lohn / Lohnabrechnung",       new[] { "andere_lohn" }, Anderes: true),
        new("anderes:weiterbildung",  "Anderes", "Weiterbildung / Zertifikat",  new[] { "andere_weiterbildung" }, Anderes: true),
        new("anderes:sonstiges",      "Anderes", "Sonstiges",                   new[] { "andere_sonstiges" }, Anderes: true),
    };

    /// <summary>Art zu einem Ziel-Schlüssel («vertrag:12» → Art «vertrag»).</summary>
    public static Art? FindeArt(string ziel)
    {
        ziel = (ziel ?? "").Trim();
        var exakt = Arten.FirstOrDefault(a => a.Schluessel == ziel);
        if (exakt != null) return exakt;
        var doppelpunkt = ziel.IndexOf(':');
        if (doppelpunkt <= 0) return null;
        var art = Arten.FirstOrDefault(a => a.Schluessel == ziel[..doppelpunkt]);
        // «anderes:x» ist nur mit den festen Einträgen gültig (oben exakt gefunden).
        return art != null && !art.Anderes && int.TryParse(ziel[(doppelpunkt + 1)..], out _) ? art : null;
    }

    private static int? ReferenzId(string ziel)
    {
        var i = ziel.IndexOf(':');
        return i > 0 && int.TryParse(ziel[(i + 1)..], out var id) ? id : null;
    }

    /// <summary>Ziele aus dem Formularfeld «ablageZiele» (durch ; getrennt).</summary>
    public static List<string> ParseZiele(string? text)
        => (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Distinct().ToList();

    // ── Optionen für einen Mitarbeiter ───────────────────────────────────────

    public record Option(
        string Key, string Gruppe, string Label, string? Sub, string Code,
        int? CurrentDokumentId, bool Historie, bool Formular, bool NurBild, bool Anderes);

    public record TypInfo(int TypId, string TypName, string KategorieName);

    /// <summary>
    /// Alle Ablageziele, die bei diesem Mitarbeiter möglich sind — mit dem heute
    /// verknüpften Dokument, damit das UI «schon verknüpft» zeigen kann.
    /// </summary>
    public async Task<List<Option>?> OptionenAsync(int employeeId)
    {
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return null;

        var heute = DateOnly.FromDateTime(DateTime.Now);
        var konten = await _db.EmployeeBankAccounts.AsNoTracking()
            .Where(k => k.EmployeeId == employeeId && (k.ValidTo == null || k.ValidTo >= heute))
            .OrderByDescending(k => k.IsHauptbank).ThenBy(k => k.Id).ToListAsync();
        var vertraege = await _db.Employments.AsNoTracking()
            .Where(v => v.EmployeeId == employeeId)
            .OrderByDescending(v => v.ContractStartDate).Take(4).ToListAsync();
        var absenzen = await _db.Absences.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.AbsenceType != "FERIEN")
            .OrderByDescending(a => a.DateFrom).Take(5).ToListAsync();
        var familie = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(m => m.EmployeeId == employeeId && m.DateOfDeath == null).ToListAsync();
        var bewilligungen = await _db.EmployeePermitHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId && h.PermitTypeId != null)
            .OrderByDescending(h => h.ValidFrom).Take(4)
            .Select(h => new { h.Id, h.ValidFrom, h.ValidTo, h.DokumentId, Code = h.PermitType != null ? h.PermitType.Code : null })
            .ToListAsync();
        var istCh = emp.NationalityId != null && await _db.Nationalities.AsNoTracking()
            .AnyAsync(n => n.Id == emp.NationalityId && n.Code.ToUpper() == "CH");

        var liste = new List<Option>();
        Option O(string key, string? sub = null, int? current = null, string? label = null)
        {
            var art = FindeArt(key)!;
            return new Option(key, art.Gruppe, label ?? art.Label, sub, art.Codes[0], current,
                              art.Historie, art.Formular, art.NurBild, art.Anderes);
        }

        liste.Add(O("ausweis", "Pass oder ID", emp.IdPassDokumentId));
        liste.Add(O("ahv_karte", null, emp.AhvKarteDokumentId));
        liste.Add(O("geburtsurkunde", null, emp.GeburtsurkundeDokumentId));
        liste.Add(O("zivilstand", "Ehe, Scheidung, Partnerschaft", emp.ZivilstandDokumentId));
        liste.Add(O("foto", "Ausschnitt wählen", emp.FotoDokumentId));
        liste.Add(O("nachtarbeit_zeugnis", "oder Verzichtserklärung", emp.NightWorkExamDokumentId));
        liste.Add(O("nachtarbeit_ausnahme", "Tag-/Nachtarbeit", emp.NightWorkAusnahmeDokumentId));

        foreach (var k in konten)
        {
            var iban = (k.Iban ?? "").Replace(" ", "");
            var sub = string.Join(" · ", new[]
            {
                iban.Length >= 4 ? "…" + iban[^4..] : null,
                k.BankName,
                k.IsHauptbank ? "Hauptbank" : null,
            }.Where(s => !string.IsNullOrWhiteSpace(s)));
            liste.Add(O($"bank:{k.Id}", sub, k.DokumentId));
        }
        liste.Add(O("bank_neu", "Bankverbindung erfassen"));

        foreach (var v in vertraege)
        {
            var modell = string.Join(" · ", new[] { v.EmploymentModel, v.JobTitle }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var zeit = $"{v.ContractStartDate:dd.MM.yyyy} – {(v.ContractEndDate.HasValue ? v.ContractEndDate.Value.ToString("dd.MM.yyyy") : "offen")}";
            liste.Add(O($"vertrag:{v.Id}", string.IsNullOrEmpty(modell) ? zeit : $"{modell} · {zeit}", v.VertragDokumentId));
        }

        liste.Add(O("kuendigung",
            emp.KuendigungPer.HasValue ? $"per {emp.KuendigungPer.Value:dd.MM.yyyy}"
            : emp.KuendigungAusgesprochenAm.HasValue ? $"vom {emp.KuendigungAusgesprochenAm.Value:dd.MM.yyyy}"
            : "Kündigungsschreiben",
            emp.KuendigungDokumentId));

        liste.Add(O("absenz_neu", "Krankheit, Unfall … mit Von/Bis"));
        foreach (var a in absenzen)
            liste.Add(O($"absenz:{a.Id}", $"{a.DateFrom:dd.MM.yy} – {a.DateTo:dd.MM.yy}", a.DokumentId, AbsenzLabel(a.AbsenceType)));

        string FamName(EmployeeFamilyMember m)
            => $"{m.FirstName} {m.LastName}".Trim() + (m.DateOfBirth.HasValue ? $" · {m.DateOfBirth.Value.Year}" : "");
        foreach (var m in familie.Where(m => m.MemberType is "Ehepartner" or "Konkubinatspartner"))
            liste.Add(O($"ausweis_partner:{m.Id}", FamName(m), m.DokumentId));
        foreach (var m in familie.Where(m => m.MemberType == "Kind").OrderBy(m => m.DateOfBirth))
        {
            liste.Add(O($"ausweis_kind:{m.Id}", FamName(m), m.DokumentId));
            liste.Add(O($"geburtsurkunde_kind:{m.Id}", FamName(m), m.GeburtsurkundeDokumentId));
        }

        // Bewilligung nur bei Ausländer/innen; bestehende Einträge = Dokument austauschen.
        if (!istCh)
        {
            liste.Add(O("bewilligung_neu", "erfassen und Ausweis einlesen"));
            foreach (var b in bewilligungen)
                liste.Add(O($"bewilligung:{b.Id}",
                    $"{b.Code ?? "?"} · {b.ValidFrom:dd.MM.yyyy} – {(b.ValidTo.HasValue ? b.ValidTo.Value.ToString("dd.MM.yyyy") : "offen")}",
                    b.DokumentId));
        }

        foreach (var a in Arten.Where(a => a.Anderes))
            liste.Add(O(a.Schluessel));
        return liste;
    }

    // Gleiche Bezeichnungen wie ABSENCE_LABELS in employees.js.
    private static string AbsenzLabel(string typ) => (typ ?? "").ToUpperInvariant() switch
    {
        "KRANK" => "Krankheit",
        "UNFALL" => "Unfall",
        "SCHULUNG" => "Schulung",
        "NACHT_KOMP" => "Nacht-Kompensation",
        "MILITAER" => "Militär",
        "FEIERTAG" => "Feiertag",
        "MUTT_VATER" => "Mutter-/Vaterschaftsurlaub",
        "FREI_KOMP" => "Frei-Kompensation",
        "BEZ_ABSENZ" => "Bezahlte Absenz",
        _ => string.IsNullOrWhiteSpace(typ) ? "Absenz" : typ,
    };

    /// <summary>
    /// Feld-Code → Dokument-Typ (erster aktiver Typ nach Reihenfolge der Kategorie
    /// und des Typs). Codes ohne Typ fehlen im Ergebnis — dort wählt man die
    /// Kategorie am Schluss selbst (und ein Admin kann sie sich merken lassen).
    /// </summary>
    public async Task<Dictionary<string, TypInfo>> TypenFuerCodesAsync()
    {
        var zeilen = await _db.DokumentTypen.AsNoTracking()
            .Where(t => t.Aktiv && t.LinkedFieldCode != null)
            .Join(_db.DokumentKategorien.AsNoTracking().Where(k => k.Aktiv),
                  t => t.KategorieId, k => k.Id,
                  (t, k) => new { t.Id, t.Name, t.SortOrder, t.LinkedFieldCode, Kat = k.Name, KatSort = k.SortOrder })
            .ToListAsync();
        var ergebnis = new Dictionary<string, TypInfo>();
        foreach (var z in zeilen.OrderBy(z => z.KatSort).ThenBy(z => z.SortOrder).ThenBy(z => z.Id))
            ergebnis.TryAdd(z.LinkedFieldCode!.Trim(), new TypInfo(z.Id, z.Name, z.Kat));
        return ergebnis;
    }

    /// <summary>Typ für eine Art: der erste ihrer Codes, der einen Typ hat.</summary>
    public static TypInfo? TypFuerArt(Art art, IReadOnlyDictionary<string, TypInfo> typen)
        => art.Codes.Select(c => typen.TryGetValue(c, out var t) ? t : null).FirstOrDefault(t => t != null);

    // ── Verknüpfen ───────────────────────────────────────────────────────────

    /// <summary>
    /// Hängt das Dokument an alle genannten Ziele. Setzt nur Felder, speichert
    /// NICHT — der Aufrufer speichert zusammen mit dem Dokument (eine Transaktion).
    /// Formular-Ziele, Foto und «Anderes» setzen kein Feld und sind hier verboten:
    /// die erledigt das UI nach dem Hochladen.
    /// </summary>
    /// <returns>Fehlertext oder null; bei Erfolg die Liste der verknüpften Angaben.</returns>
    public async Task<(string? Fehler, List<string> Verknuepft)> VerknuepfeAsync(
        int employeeId, int dokumentId, IEnumerable<string> ziele)
    {
        var verknuepft = new List<string>();
        var jetzt = DateTime.Now;
        Employee? emp = null;
        async Task<Employee> Ma() => emp ??= await _db.Employees.FirstAsync(e => e.Id == employeeId);

        foreach (var ziel in ziele)
        {
            var art = FindeArt(ziel);
            if (art == null || art.Formular || art.Anderes)
                return ($"Ablageziel «{ziel}» ist unbekannt oder setzt kein Feld.", verknuepft);
            var refId = ReferenzId(ziel);

            switch (art.Schluessel)
            {
                case "ausweis":              (await Ma()).IdPassDokumentId = dokumentId; break;
                case "ahv_karte":            (await Ma()).AhvKarteDokumentId = dokumentId; break;
                case "geburtsurkunde":       (await Ma()).GeburtsurkundeDokumentId = dokumentId; break;
                case "zivilstand":           (await Ma()).ZivilstandDokumentId = dokumentId; break;
                case "nachtarbeit_zeugnis":  (await Ma()).NightWorkExamDokumentId = dokumentId; break;
                case "nachtarbeit_ausnahme": (await Ma()).NightWorkAusnahmeDokumentId = dokumentId; break;
                case "kuendigung":           (await Ma()).KuendigungDokumentId = dokumentId; break;
                case "bank":
                {
                    var k = await _db.EmployeeBankAccounts.FirstOrDefaultAsync(x => x.Id == refId && x.EmployeeId == employeeId);
                    if (k == null) return ("Die gewählte Bankverbindung gehört nicht zu diesem Mitarbeiter.", verknuepft);
                    k.DokumentId = dokumentId; k.UpdatedAt = jetzt; break;
                }
                case "vertrag":
                {
                    var v = await _db.Employments.FirstOrDefaultAsync(x => x.Id == refId && x.EmployeeId == employeeId);
                    if (v == null) return ("Der gewählte Vertrag gehört nicht zu diesem Mitarbeiter.", verknuepft);
                    v.VertragDokumentId = dokumentId; break;
                }
                case "absenz":
                {
                    var a = await _db.Absences.FirstOrDefaultAsync(x => x.Id == refId && x.EmployeeId == employeeId);
                    if (a == null) return ("Die gewählte Absenz gehört nicht zu diesem Mitarbeiter.", verknuepft);
                    a.DokumentId = dokumentId; a.UpdatedAt = jetzt; break;
                }
                case "bewilligung":
                {
                    var h = await _db.EmployeePermitHistories.FirstOrDefaultAsync(x => x.Id == refId && x.EmployeeId == employeeId);
                    if (h == null) return ("Die gewählte Bewilligung gehört nicht zu diesem Mitarbeiter.", verknuepft);
                    h.DokumentId = dokumentId; break;
                }
                case "ausweis_partner":
                case "ausweis_kind":
                case "geburtsurkunde_kind":
                {
                    var m = await _db.EmployeeFamilyMembers.FirstOrDefaultAsync(x => x.Id == refId && x.EmployeeId == employeeId);
                    if (m == null) return ("Das gewählte Familienmitglied gehört nicht zu diesem Mitarbeiter.", verknuepft);
                    if (art.Schluessel == "geburtsurkunde_kind") m.GeburtsurkundeDokumentId = dokumentId;
                    else m.DokumentId = dokumentId;
                    m.UpdatedAt = jetzt; break;
                }
                default:
                    return ($"Ablageziel «{ziel}» wird nicht unterstützt.", verknuepft);
            }
            verknuepft.Add(art.Label);
        }
        return (null, verknuepft);
    }
}
