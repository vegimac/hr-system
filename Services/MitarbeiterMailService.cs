using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Gemeinsamer Unterbau für die «schönen» Einzel-E-Mails an einen MA
/// (Walter 03.09.2026): gleicher Rahmen wie die Gruppen-E-Mail, Briefanrede,
/// wählbare Kopie an OneCrew-Benutzer (Vorschlag: s.ittig + GF der Filiale)
/// und Ablage der gesendeten Mail als PDF in der MA-Akte.
/// Erste Anwendung: fehlende Ehepartner-Angaben / fehlender Partner-Ausweis
/// bei der Quellensteuer. Die Bewilligungs-Mail im
/// EmployeePermitHistoryController hat denselben Aufbau.
/// </summary>
public class MitarbeiterMailService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly string _docStorage;

    public MitarbeiterMailService(AppDbContext db, EmailService email, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db; _email = email;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _docStorage = configured;
    }

    public sealed class Mail
    {
        public string To { get; init; } = "";
        public string Name { get; init; } = "";
        public string Betreff { get; init; } = "";
        public string Text { get; init; } = "";
        public string Html { get; init; } = "";
        public int? CompanyProfileId { get; init; }
        public string? BranchCode { get; init; }
        public string? BranchName { get; init; }
    }

    public sealed record Benutzer(int Id, string Email, string Name, string Rolle, bool Vorgeschlagen);
    public sealed record Kopie(string Email, string Name, string Rolle);
    public sealed record SendeErgebnis(List<string> KopieOk, List<string> KopieFehler);

    /// <summary>Briefanrede des MA — immer die hinterlegte, sonst «Hallo Vorname».</summary>
    public static string Briefanrede(Employee emp)
    {
        if (!string.IsNullOrWhiteSpace(emp.LetterSalutation)) return emp.LetterSalutation!.Trim();
        var v = (emp.FirstName ?? "").Trim();
        return v.Length > 0 ? $"Hallo {v}" : "Hallo";
    }

    public async Task<string> SenderNameAsync(int? userId)
    {
        if (userId == null) return "";
        var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId.Value);
        return (u?.FirstName ?? "").Trim();
    }

    public async Task<(int? CpId, string? Code, string? Name)> FilialeAsync(int employeeId)
    {
        var cpId = await _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync()
            ?? await _db.Employments.AsNoTracking()
                .Where(em => em.EmployeeId == employeeId)
                .OrderByDescending(em => em.ContractStartDate)
                .Select(em => (int?)em.CompanyProfileId)
                .FirstOrDefaultAsync();
        if (cpId == null) return (null, null, null);
        var b = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == cpId.Value).Select(c => new { c.RestaurantCode, c.City, c.BranchName }).FirstOrDefaultAsync();
        return (cpId, b?.RestaurantCode, b == null ? null : (b.City ?? b.BranchName));
    }

    public static string HtmlAusText(string betreff, string text)
    {
        var textHtml = $@"      <div style=""font-size:14px;line-height:1.6;color:#0f172a;white-space:pre-line"">{System.Net.WebUtility.HtmlEncode(text)}</div>";
        return EmailService.HtmlRahmen(betreff, textHtml, "");
    }

    /// <summary>Alle aktiven OneCrew-Benutzer mit E-Mail; Vorschlag = s.ittig + GF der Filiale.</summary>
    public async Task<List<Benutzer>> BenutzerAsync(int? companyProfileId)
    {
        var gfIds = companyProfileId == null ? new HashSet<int>() :
            (await _db.UserBranchAccesses.AsNoTracking()
                .Where(a => a.CompanyProfileId == companyProfileId.Value)
                .Select(a => a.UserId).ToListAsync()).ToHashSet();
        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && u.Email != "" && u.Role != "employee")
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Role })
            .ToListAsync();
        return users.Select(u => new Benutzer(
            u.Id, u.Email.Trim(), $"{u.FirstName} {u.LastName}".Trim(), u.Role,
            string.Equals((u.LastName ?? "").Trim(), "ittig", StringComparison.OrdinalIgnoreCase)
            || (u.Role == "user" && gfIds.Contains(u.Id)))).ToList();
    }

    public async Task<List<Kopie>> KopienAsync(int? companyProfileId, List<int>? kopieUserIds)
    {
        var alle = await BenutzerAsync(companyProfileId);
        var gewaehlt = kopieUserIds == null
            ? alle.Where(b => b.Vorgeschlagen)
            : alle.Where(b => kopieUserIds.Contains(b.Id));
        return gewaehlt.Select(b => new Kopie(b.Email, b.Name, b.Rolle == "user" ? "GF" : "HR")).ToList();
    }

    /// <summary>Hauptmail + Kopien (eigene Mails mit Hinweiszeile — EmailService kennt kein CC).</summary>
    public async Task<SendeErgebnis?> SendenAsync(Mail m, List<Kopie> kopien, VersandKategorie kategorie, int employeeId)
    {
        var ok = await _email.SendAsync(m.To, m.Name, m.Betreff, m.Html, m.Text, kategorie, employeeId: employeeId);
        if (!ok) return null;
        var kopieOk = new List<string>();
        var kopieFehler = new List<string>();
        var hinweis = $@"      <div style=""font-size:12px;color:#8b8b8b;margin-bottom:12px"">Kopie der E-Mail an {System.Net.WebUtility.HtmlEncode(m.Name)} &lt;{System.Net.WebUtility.HtmlEncode(m.To)}&gt;{(string.IsNullOrWhiteSpace(m.BranchName) ? "" : " · Filiale " + System.Net.WebUtility.HtmlEncode(m.BranchName))}</div>";
        var kopieHtml = m.Html.Replace(@"<div style=""font-size:14px;line-height:1.6;color:#0f172a;white-space:pre-line"">", hinweis + @"<div style=""font-size:14px;line-height:1.6;color:#0f172a;white-space:pre-line"">");
        foreach (var k in kopien)
        {
            if (string.Equals(k.Email, m.To, StringComparison.OrdinalIgnoreCase)) continue;
            var kok = await _email.SendAsync(k.Email, k.Name, $"Kopie: {m.Betreff}", kopieHtml,
                $"Kopie der E-Mail an {m.Name} <{m.To}>\n\n{m.Text}", kategorie, employeeId: employeeId);
            if (kok) kopieOk.Add($"{k.Name} ({k.Rolle})"); else kopieFehler.Add($"{k.Name} ({k.Rolle})");
        }
        return new SendeErgebnis(kopieOk, kopieFehler);
    }

    /// <summary>Gesendete Mail als PDF in die MA-Dokumente legen (best-effort, false wenn kein Dokumenttyp passt).</summary>
    public async Task<bool> AblegenAsync(int employeeId, Mail m, DokumentTyp? typ, string titel, string dateiPrefix,
                                         IEnumerable<Kopie> kopien, int? userId)
    {
        if (typ == null) return false;
        var kopieText = string.Join(", ", kopien.Select(k => $"{k.Name} <{k.Email}>"));
        var wann = DateTime.Now;
        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(28); page.MarginBottom(28); page.MarginHorizontal(36);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor("#222"));
                page.Content().Column(col =>
                {
                    col.Item().Text(titel).SemiBold().FontSize(14).FontColor("#1a1a1a");
                    col.Item().PaddingTop(6).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(80); c.RelativeColumn(); });
                        void Z(string l, string v)
                        {
                            t.Cell().Padding(2).Text(l).FontSize(9).FontColor("#666");
                            t.Cell().Padding(2).Text(v).FontSize(9.5f);
                        }
                        Z("Datum", wann.ToString("dd.MM.yyyy HH:mm"));
                        Z("An", $"{m.Name} <{m.To}>");
                        if (kopieText.Length > 0) Z("Kopie an", kopieText);
                        Z("Betreff", m.Betreff);
                    });
                    col.Item().PaddingTop(10).LineHorizontal(0.6f).LineColor("#ccc");
                    col.Item().PaddingTop(12).Text(m.Text).FontSize(11).LineHeight(1.5f);
                });
            });
        }).GeneratePdf();

        var safeBranch = new string((m.BranchCode ?? "0").Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        if (safeBranch.Length == 0) safeBranch = "0";
        var empDir = Path.Combine(_docStorage, safeBranch, employeeId.ToString());
        Directory.CreateDirectory(empDir);
        var storageName = Guid.NewGuid().ToString("N") + ".pdf";
        await File.WriteAllBytesAsync(Path.Combine(empDir, storageName), pdf);
        _db.EmployeeDokumente.Add(new EmployeeDokument
        {
            EmployeeId = employeeId,
            DokumentTypId = typ.Id,
            BranchCode = safeBranch,
            FilenameOriginal = $"{dateiPrefix}_{wann:yyyyMMdd_HHmm}.pdf",
            FilenameStorage = storageName,
            MimeType = "application/pdf",
            GroesseBytes = pdf.LongLength,
            Bemerkung = $"{titel} — gesendet {wann:dd.MM.yyyy HH:mm}",
            HochgeladenVon = userId,
            HochgeladenAm = wann,
            ErstelltAm = wann,
        });
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Dokumenttyp «Ausweis Ehegatte» (linked_field_code = spouse), Fallbacks über den Namen.</summary>
    public async Task<DokumentTyp?> DokumentTypEhepartnerAsync()
        => await _db.DokumentTypen.AsNoTracking().Where(t => t.Aktiv && t.LinkedFieldCode == "spouse").FirstOrDefaultAsync()
        ?? await _db.DokumentTypen.AsNoTracking().Where(t => t.Aktiv && (t.Name.Contains("Ehegatte") || t.Name.Contains("Ehepartner"))).FirstOrDefaultAsync()
        ?? await _db.DokumentTypen.AsNoTracking().Where(t => t.Aktiv && t.LinkedFieldCode == "permit").FirstOrDefaultAsync();
}
