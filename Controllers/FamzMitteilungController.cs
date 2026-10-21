using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Eine neue GastroSocial-Mitteilung (Entscheid oder Verfallanzeige) einlesen,
/// den Vorschlag zeigen und erst nach Bestätigung speichern. Der Beleg hängt
/// danach am Zulagen-Eintrag. Nicht für den ganzen Bestand.
/// </summary>
[Authorize]
[ApiController]
[Route("api/famz-mitteilung")]
public class FamzMitteilungController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MaFilialZugriff _zugriff;
    private readonly LohnEditLockService _editLock;
    private readonly FamzKorrekturService _korrektur;
    private readonly FamilienzulagenResolverService _tarif;
    private readonly string _storagePath;

    public FamzMitteilungController(
        AppDbContext db, MaFilialZugriff zugriff, LohnEditLockService editLock,
        FamzKorrekturService korrektur, FamilienzulagenResolverService tarif,
        IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _zugriff = zugriff;
        _editLock = editLock;
        _korrektur = korrektur;
        _tarif = tarif;
        var configured = config["Documents:StoragePath"];
        _storagePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "data", "documents")
            : configured;
    }

    public sealed record AuswahlDto(string Schluessel, string Aktion);

    [HttpPost("vorschau")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Vorschau(IFormFile? file)
    {
        var (bytes, name, fehler) = await LiesDatei(file);
        if (fehler != null) return BadRequest(new { error = fehler });
        FamzMitteilung mitteilung;
        try { mitteilung = FamzMitteilungLeser.Lies(FamzMitteilungLeser.TextAusPdf(bytes!)); }
        catch (Exception) { return BadRequest(new { error = "Das PDF konnte nicht gelesen werden." }); }

        if (mitteilung.Ahv != null)
        {
            var (emp, keinZugriff) = await MitarbeiterAsync(mitteilung.Ahv);
            if (keinZugriff) return MaFilialZugriff.KeinZugriff();
            if (emp?.Id == -1)
                return Ok(Mehrere(mitteilung, name));
            if (emp != null)
            {
                var v = await VorschlagAsync(mitteilung, emp);
                return Ok(Antwort(v, name));
            }
        }
        var leer = FamzMitteilungVorschlag.Erstelle(mitteilung, new FamzKontext(
            null, null, null, null, null, false, null, [], []));
        return Ok(Antwort(leer, name));
    }

    [HttpPost("uebernehmen")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Uebernehmen(IFormFile? file, [FromForm] string? auswahl)
    {
        var gewaehlt = LiesAuswahl(auswahl);
        if (gewaehlt.Count == 0)
            return BadRequest(new { error = "Nichts ausgewählt." });

        var (bytes, name, fehler) = await LiesDatei(file);
        if (fehler != null) return BadRequest(new { error = fehler });
        FamzMitteilung mitteilung;
        try { mitteilung = FamzMitteilungLeser.Lies(FamzMitteilungLeser.TextAusPdf(bytes!)); }
        catch (Exception) { return BadRequest(new { error = "Das PDF konnte nicht gelesen werden." }); }

        if (mitteilung.Ahv == null)
            return BadRequest(new { error = mitteilung.Fehler ?? "Keine AHV-Nummer auf der Mitteilung." });
        var (emp, keinZugriff) = await MitarbeiterAsync(mitteilung.Ahv);
        if (keinZugriff) return MaFilialZugriff.KeinZugriff();
        if (emp == null || emp.Id < 0)
            return BadRequest(new { error = emp?.Id == -1
                ? $"Mehrere Mitarbeiter haben die AHV-Nummer {mitteilung.Ahv}."
                : $"Kein Mitarbeiter mit der AHV-Nummer {mitteilung.Ahv}." });

        var v = await VorschlagAsync(mitteilung, emp);
        var zeilen = new List<FamzZeile>();
        foreach (var w in gewaehlt)
        {
            var z = v.Zeilen.FirstOrDefault(x => x.Schluessel == w.Schluessel);
            if (z == null || !z.Waehlbar || z.Aktion != w.Aktion)
                return Conflict(new { error = "GEAENDERT", message = "Die Mitteilung passt nicht mehr zum Vorschlag. Bitte nochmals einlesen." });
            zeilen.Add(z);
        }

        var gespeichert = new List<string>();
        var belegen = new List<FamilyMemberAllowance>();
        var korrigieren = new List<FamilyMemberAllowance>();
        string? dateiPfad = null;
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var z in zeilen)
            {
                switch (z.Modus)
                {
                    case FamzMitteilungVorschlag.ModusNeu:
                    {
                        var neu = NeueZulage(z);
                        _db.FamilyMemberAllowances.Add(neu);
                        belegen.Add(neu);
                        korrigieren.Add(neu);
                        gespeichert.Add(z.Text);
                        break;
                    }
                    case FamzMitteilungVorschlag.ModusTeilen:
                    {
                        var alt = await _db.FamilyMemberAllowances
                            .FirstOrDefaultAsync(a => a.Id == z.AllowanceId && a.FamilyMemberId == z.FamilyMemberId);
                        if (alt == null || z.Von == null || z.Von.Value <= alt.ValidFrom)
                            return Conflict(new { error = "GEAENDERT", message = "Die bisherige Zulage hat sich geändert. Bitte nochmals einlesen." });
                        alt.ValidTo = z.Von.Value.AddDays(-1);
                        alt.UpdatedAt = DateTime.Now;
                        korrigieren.Add(alt);
                        var neu = NeueZulage(z);
                        _db.FamilyMemberAllowances.Add(neu);
                        belegen.Add(neu);
                        korrigieren.Add(neu);
                        gespeichert.Add(z.Text);
                        break;
                    }
                    case FamzMitteilungVorschlag.ModusErsetzen:
                    {
                        // Erneuter Entscheid mit früherem Beginn: alte Zulage beenden (im Verlauf
                        // sichtbar), neuen Eintrag anlegen — nicht löschen (Walter 21.10.2026).
                        // Ende = Tag vor dem alten Beginn, damit keine Überlappung mit dem neuen.
                        var alt = await _db.FamilyMemberAllowances
                            .FirstOrDefaultAsync(a => a.Id == z.AllowanceId && a.FamilyMemberId == z.FamilyMemberId);
                        if (alt == null || z.Von == null || z.Bis == null || z.Von.Value >= alt.ValidFrom)
                            return Conflict(new { error = "GEAENDERT", message = "Die bisherige Zulage hat sich geändert. Bitte nochmals einlesen." });
                        var benutzt = await _korrektur.VerwendetBisAsync(emp.Id, new[] { alt });
                        if (benutzt.ContainsKey(alt.Id))
                            return Conflict(new { error = "SCHON_IM_LOHN", message = "Die bisherige Zulage ist schon im Lohn — bitte von Hand prüfen." });
                        alt.ValidTo = alt.ValidFrom.AddDays(-1);
                        alt.UpdatedAt = DateTime.Now;
                        var neu = NeueZulage(z);
                        _db.FamilyMemberAllowances.Add(neu);
                        belegen.Add(neu);
                        korrigieren.Add(neu);
                        gespeichert.Add(z.Text);
                        break;
                    }
                    case FamzMitteilungVorschlag.ModusSetzen:
                    {
                        var alt = await _db.FamilyMemberAllowances
                            .FirstOrDefaultAsync(a => a.Id == z.AllowanceId && a.FamilyMemberId == z.FamilyMemberId);
                        if (alt == null || z.Von == null || z.Bis == null)
                            return Conflict(new { error = "GEAENDERT", message = "Die bisherige Zulage hat sich geändert. Bitte nochmals einlesen." });
                        var benutzt = await _korrektur.VerwendetBisAsync(emp.Id, new[] { alt });
                        alt.ValidTo = z.Bis;
                        if (!benutzt.ContainsKey(alt.Id))
                        {
                            alt.AllowanceType = z.AllowanceType;
                            alt.TarifSatzNr = z.TarifSatzNr;
                            alt.MonthlyAmount = z.Betrag ?? alt.MonthlyAmount;
                            alt.ErfahrenAm = z.ErfahrenAm;
                        }
                        alt.UpdatedAt = DateTime.Now;
                        belegen.Add(alt);
                        korrigieren.Add(alt);
                        gespeichert.Add(z.Text);
                        break;
                    }
                    case FamzMitteilungVorschlag.ModusEnde:
                    {
                        var alt = await _db.FamilyMemberAllowances
                            .FirstOrDefaultAsync(a => a.Id == z.AllowanceId && a.FamilyMemberId == z.FamilyMemberId);
                        if (alt == null || z.Bis == null)
                            return Conflict(new { error = "GEAENDERT", message = "Die bisherige Zulage hat sich geändert. Bitte nochmals einlesen." });
                        alt.ValidTo = z.Bis;
                        alt.UpdatedAt = DateTime.Now;
                        belegen.Add(alt);
                        korrigieren.Add(alt);
                        gespeichert.Add(z.Text);
                        break;
                    }
                    case FamzMitteilungVorschlag.ModusBeleg:
                    {
                        var alt = await _db.FamilyMemberAllowances
                            .FirstOrDefaultAsync(a => a.Id == z.AllowanceId && a.FamilyMemberId == z.FamilyMemberId);
                        if (alt == null)
                            return Conflict(new { error = "GEAENDERT", message = "Die bisherige Zulage hat sich geändert. Bitte nochmals einlesen." });
                        belegen.Add(alt);
                        gespeichert.Add(z.Text);
                        break;
                    }
                }
            }

            await _db.SaveChangesAsync();
            int? dokumentId = null;
            string? dokHinweis = null;
            if (belegen.Count > 0)
            {
                var (id, pfad, hinweis) = await BelegAblegenAsync(emp, v, bytes!, name!, zeilen);
                dokumentId = id;
                dateiPfad = pfad;
                dokHinweis = hinweis;
                if (id != null)
                {
                    foreach (var e in belegen)
                        e.DokumentId = id;
                    await _db.SaveChangesAsync();
                }
            }
            await tx.CommitAsync();
            dateiPfad = null;

            var actor = User.FindFirst(ClaimTypes.Name)?.Value
                        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            foreach (var e in korrigieren.Distinct())
                await _korrektur.ErzeugeKorrekturenAsync(e, actor);

            return Ok(new
            {
                mitarbeiterId = emp.Id,
                gespeichert,
                dokumentId,
                hinweis = dokHinweis
            });
        }
        catch
        {
            await tx.RollbackAsync();
            if (dateiPfad != null)
            {
                try { if (System.IO.File.Exists(dateiPfad)) System.IO.File.Delete(dateiPfad); } catch { /* Datei bleibt sonst liegen */ }
            }
            throw;
        }
    }

    async Task<FamzVorschlag> VorschlagAsync(FamzMitteilung m, EmpTreffer emp)
    {
        var rohVertraege = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == emp.Id && e.CompanyProfileId != null)
            .Select(e => new
            {
                e.ContractStartDate, e.ContractEndDate, e.CompanyProfileId,
                Kanton = e.CompanyProfile != null ? e.CompanyProfile.KantonCode : null,
                Code = e.CompanyProfile != null ? e.CompanyProfile.RestaurantCode : null,
                Ort = e.CompanyProfile != null ? e.CompanyProfile.City : null,
                Filiale = e.CompanyProfile != null ? e.CompanyProfile.BranchName : null
            })
            .ToListAsync();
        var vertraege = rohVertraege.Select(e => new Vertrag(
            e.ContractStartDate, e.ContractEndDate, e.CompanyProfileId!.Value,
            e.Kanton, e.Code, string.IsNullOrWhiteSpace(e.Ort) ? e.Filiale : e.Ort)).ToList();

        var kinderRoh = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == emp.Id && f.MemberType == "Kind")
            .Select(f => new { f.Id, f.FirstName, f.DateOfBirth })
            .ToListAsync();
        var kinderIds = kinderRoh.Select(f => f.Id).ToList();
        var zulagen = kinderIds.Count == 0
            ? []
            : await _db.FamilyMemberAllowances.AsNoTracking()
                .Where(a => kinderIds.Contains(a.FamilyMemberId))
                .ToListAsync();
        var verwendet = zulagen.Count == 0
            ? new Dictionary<int, (int Jahr, int Monat)>()
            : await _korrektur.VerwendetBisAsync(emp.Id, zulagen);

        var kinder = kinderRoh
            .Where(f => f.DateOfBirth != null && !string.IsNullOrWhiteSpace(f.FirstName))
            .Select(f => new FamzKind(
                f.Id,
                f.FirstName!.Trim(),
                DateOnly.FromDateTime(f.DateOfBirth!.Value),
                zulagen.Where(a => a.FamilyMemberId == f.Id)
                    .Select(a => new FamzZulage(
                        a.Id, a.AllowanceType, a.TarifSatzNr, a.ValidFrom, a.ValidTo,
                        verwendet.ContainsKey(a.Id), a.DokumentId != null))
                    .ToList()))
            .ToList();

        var tage = m.Kinder.Select(k => k.Von ?? k.Bis).Where(t => t != null).Select(t => t!.Value).Distinct().ToList();
        var ersterCache = new Dictionary<int, DateOnly?>();
        var tarife = new List<TarifAmTag>();
        foreach (var tag in tage)
        {
            var filial = FilialeAm(vertraege, tag);
            DateOnly? offen = null;
            if (filial != null)
            {
                if (!ersterCache.TryGetValue(filial.CompanyProfileId, out offen))
                {
                    offen = await _editLock.GetFirstAllowedDateForContractsAsync(filial.CompanyProfileId);
                    ersterCache[filial.CompanyProfileId] = offen;
                }
            }
            var stand = filial?.Kanton == null ? null : await _tarif.GetTarifAsync(filial.Kanton, tag);
            tarife.Add(new TarifAmTag(
                tag, filial?.Kanton, filial?.Name ?? "", filial?.Code, filial?.CompanyProfileId, offen, Saetze(stand)));
        }

        var kopf = tarife.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Filiale)) ?? tarife.FirstOrDefault();
        var kontext = new FamzKontext(
            emp.Id, emp.Vorname, emp.Nachname, kopf?.Filiale, kopf?.Kanton, emp.OhneLohn,
            kopf?.ErsterOffener, kinder, tarife);
        return FamzMitteilungVorschlag.Erstelle(m, kontext);
    }

    static List<SatzOption> Saetze(FamilienzulagenTarif? t)
    {
        var liste = new List<SatzOption>();
        if (t == null) return liste;
        if (t.KinderzulageSatz1 is decimal kz1)
            liste.Add(new SatzOption("KZ", 1, kz1, "Kinderzulage Satz 1"));
        if (t.KinderzulageSatz2 is decimal kz2)
        {
            var ab = t.KinderzulageSatz2AbAlter is int alter ? $" ab {alter} J." : "";
            liste.Add(new SatzOption("KZ", 2, kz2, "Kinderzulage Satz 2" + ab));
        }
        if (t.AusbildungszulageSatz1 is decimal az1)
            liste.Add(new SatzOption("AZ", 1, az1, "Ausbildungszulage Satz 1"));
        if (t.AusbildungszulageSatz2 is decimal az2)
        {
            var ab = t.AusbildungszulageSatz2AbAlter is int alter ? $" ab {alter} J." : "";
            liste.Add(new SatzOption("AZ", 2, az2, "Ausbildungszulage Satz 2" + ab));
        }
        if (t.GeburtszulageBetrag is decimal gz && gz > 0)
            liste.Add(new SatzOption("GZ", null, gz, "Geburtszulage"));
        if (t.AdoptionszulageBetrag is decimal ad && ad > 0)
            liste.Add(new SatzOption("AdoptZ", null, ad, "Adoptionszulage"));
        return liste;
    }

    static Filiale? FilialeAm(List<Vertrag> vertraege, DateOnly tag)
    {
        bool Deckt(Vertrag v)
        {
            var von = DateOnly.FromDateTime(v.Start);
            var bis = v.Ende.HasValue ? DateOnly.FromDateTime(v.Ende.Value) : (DateOnly?)null;
            return von <= tag && (bis == null || bis.Value >= tag);
        }
        var treffer = vertraege.Where(Deckt).OrderByDescending(v => v.Start).FirstOrDefault()
            ?? vertraege.Where(v => DateOnly.FromDateTime(v.Start) <= tag).OrderByDescending(v => v.Start).FirstOrDefault()
            ?? vertraege.OrderByDescending(v => v.Start).FirstOrDefault();
        if (treffer == null) return null;
        var name = string.IsNullOrWhiteSpace(treffer.Name) ? (treffer.Kanton ?? "") : treffer.Name!;
        return new Filiale(treffer.CompanyProfileId, treffer.Kanton, treffer.Code, name);
    }

    static FamilyMemberAllowance NeueZulage(FamzZeile z) => new()
    {
        FamilyMemberId = z.FamilyMemberId!.Value,
        ValidFrom = z.Von!.Value,
        ValidTo = z.Bis,
        ErfahrenAm = z.ErfahrenAm,
        MonthlyAmount = z.Betrag ?? 0m,
        AllowanceType = z.AllowanceType,
        TarifSatzNr = z.TarifSatzNr,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now
    };

    async Task<(int? Id, string? Pfad, string? Hinweis)> BelegAblegenAsync(
        EmpTreffer emp, FamzVorschlag v, byte[] bytes, string dateiname, IReadOnlyList<FamzZeile> zeilen)
    {
        var typId = await _db.DokumentTypen.AsNoTracking()
            .Where(t => t.Aktiv && t.LinkedFieldCode == "family_allowance")
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync();
        if (typId == null)
            return (null, null, "Zulagen gespeichert. Der Beleg wurde nicht abgelegt, der Dokumenttyp Familienzulage fehlt.");

        var gleich = await GleicheDateiAsync(emp.Id, bytes);
        if (gleich != null) return (gleich, null, null);

        var filial = v.Kontext.Tarife.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.BranchCode));
        var branch = Sicher(filial?.BranchCode);
        var ordner = Path.Combine(_storagePath, branch, emp.Id.ToString());
        Directory.CreateDirectory(ordner);
        var speicher = Guid.NewGuid().ToString("N") + ".pdf";
        var pfad = Path.Combine(ordner, speicher);
        await System.IO.File.WriteAllBytesAsync(pfad, bytes);

        var von = zeilen.Select(z => z.Von).Where(d => d != null).Select(d => d!.Value).DefaultIfEmpty().Min();
        var bisWerte = zeilen.Where(z => z.Bis != null).Select(z => z.Bis!.Value).ToList();
        var art = v.Mitteilung.Art == FamzMitteilungArt.Verfall ? "Verfallanzeige" : "Entscheid";
        var brief = v.Mitteilung.Briefdatum?.ToString("dd.MM.yyyy");
        var doc = new EmployeeDokument
        {
            EmployeeId = emp.Id,
            DokumentTypId = typId.Value,
            BranchCode = branch,
            FilenameOriginal = Path.GetFileName(dateiname),
            FilenameStorage = speicher,
            MimeType = "application/pdf",
            GroesseBytes = bytes.Length,
            Bemerkung = brief == null ? $"Familienzulagen {art}" : $"Familienzulagen {art} {brief}",
            GueltigVon = von == default ? null : von,
            GueltigBis = bisWerte.Count == 0 ? null : bisWerte.Max(),
            HochgeladenVon = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null,
            HochgeladenAm = DateTime.Now
        };
        _db.EmployeeDokumente.Add(doc);
        await _db.SaveChangesAsync();
        return (doc.Id, pfad, null);
    }

    async Task<int?> GleicheDateiAsync(int employeeId, byte[] bytes)
    {
        var gleichGross = await _db.EmployeeDokumente
            .Where(d => d.EmployeeId == employeeId && d.GroesseBytes == bytes.Length)
            .ToListAsync();
        if (gleichGross.Count == 0) return null;
        byte[] neu;
        using (var sha = SHA256.Create()) neu = sha.ComputeHash(bytes);
        foreach (var kand in gleichGross)
        {
            var pfad = PfadVon(kand);
            if (pfad == null || !System.IO.File.Exists(pfad)) continue;
            byte[] alt;
            using (var sha = SHA256.Create())
            using (var st = System.IO.File.OpenRead(pfad))
                alt = await sha.ComputeHashAsync(st);
            if (alt.AsSpan().SequenceEqual(neu)) return kand.Id;
        }
        return null;
    }

    string? PfadVon(EmployeeDokument d)
    {
        if (string.IsNullOrWhiteSpace(d.FilenameStorage)) return null;
        if (!string.IsNullOrWhiteSpace(d.BranchCode))
            return Path.Combine(_storagePath, d.BranchCode, d.EmployeeId.ToString(), d.FilenameStorage);
        return Path.Combine(_storagePath, d.EmployeeId.ToString(), d.FilenameStorage);
    }

    async Task<(EmpTreffer? Emp, bool KeinZugriff)> MitarbeiterAsync(string ahv)
    {
        var soll = new string(ahv.Where(char.IsDigit).ToArray());
        var liste = await _db.Employees.AsNoTracking()
            .Where(e => e.SocialSecurityNumber != null && e.SocialSecurityNumber.Contains("756"))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.SocialSecurityNumber, e.IsPayrollExcluded })
            .ToListAsync();
        var treffer = liste
            .Where(e => new string(e.SocialSecurityNumber!.Where(char.IsDigit).ToArray()) == soll)
            .ToList();
        if (treffer.Count == 0) return (null, false);
        if (treffer.Count > 1) return (new EmpTreffer(-1, "", "", false), false);
        var e1 = treffer[0];
        if (!await _zugriff.DarfMitarbeiterAsync(User, e1.Id)) return (null, true);
        return (new EmpTreffer(e1.Id, e1.FirstName ?? "", e1.LastName ?? "", e1.IsPayrollExcluded), false);
    }

    static async Task<(byte[]? Bytes, string? Name, string? Fehler)> LiesDatei(IFormFile? file)
    {
        if (file == null || file.Length == 0) return (null, null, "Keine Datei.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return (null, null, "Bitte die Mitteilung als PDF.");
        if (DokumentUploadRegel.Pruefe(file) is { } abgelehnt)
        {
            var msg = abgelehnt.GetType().GetProperty("message")?.GetValue(abgelehnt) as string
                      ?? "Diese Datei kann nicht gelesen werden.";
            return (null, null, msg);
        }
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return (ms.ToArray(), file.FileName, null);
    }

    static List<AuswahlDto> LiesAuswahl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<AuswahlDto>>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException) { return []; }
    }

    static object Mehrere(FamzMitteilung m, string? dateiname) => new
    {
        dateiname,
        art = m.Art.ToString(),
        briefdatum = m.Briefdatum,
        ahv = m.Ahv,
        berechtigte = string.Join(' ', new[] { m.Vorname, m.Nachname }.Where(s => !string.IsNullOrWhiteSpace(s))),
        fakKanton = m.FakKanton,
        hinweis = $"Mehrere Mitarbeiter haben die AHV-Nummer {m.Ahv}.",
        mitarbeiter = (object?)null,
        zeilen = Array.Empty<object>()
    };

    static object Antwort(FamzVorschlag v, string? dateiname)
    {
        var m = v.Mitteilung;
        var k = v.Kontext;
        string? hinweis = m.Fehler;
        if (hinweis == null && k.EmployeeId == null && m.Ahv != null)
            hinweis = $"Kein Mitarbeiter mit der AHV-Nummer {m.Ahv}.";
        return new
        {
            dateiname,
            art = m.Art.ToString(),
            briefdatum = m.Briefdatum,
            ahv = m.Ahv,
            berechtigte = string.Join(' ', new[] { m.Vorname, m.Nachname }.Where(s => !string.IsNullOrWhiteSpace(s))),
            fakKanton = m.FakKanton,
            hinweis,
            mitarbeiter = k.EmployeeId == null ? null : new
            {
                id = k.EmployeeId,
                name = string.Join(' ', new[] { k.Vorname, k.Nachname }.Where(s => !string.IsNullOrWhiteSpace(s))),
                filiale = k.Filiale,
                kanton = k.FilialKanton,
                ohneLohn = k.OhneLohn
            },
            zeilen = v.Zeilen.Select(z => new
            {
                schluessel = z.Schluessel,
                aktion = z.Aktion,
                waehlbar = z.Waehlbar,
                kind = z.Kind,
                text = z.Text
            })
        };
    }

    static string Sicher(string? code)
    {
        var s = new string((code ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        return string.IsNullOrEmpty(s) ? "filiale" : s;
    }

    sealed record EmpTreffer(int Id, string Vorname, string Nachname, bool OhneLohn);
    sealed record Vertrag(DateTime Start, DateTime? Ende, int CompanyProfileId, string? Kanton, string? Code, string? Name);
    sealed record Filiale(int CompanyProfileId, string? Kanton, string? Code, string Name);
}
