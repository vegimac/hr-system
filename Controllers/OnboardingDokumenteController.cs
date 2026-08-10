using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Onboarding-Dokumente pro Filiale (Walter-Vorgabe 09.08.2026): ein ORDNER
/// pro Restaurant im Dokumenten-Storage (Documents:StoragePath/onboarding/
/// {companyProfileId}/) — NICHT im Code/Repo. Die PDFs (AGB, Job-Profil,
/// Hygiene, Datenschutz, Versicherungen, Reglemente …) hängen automatisch am
/// öffentlichen Vertrags-Link (ContractShareController) der jeweiligen
/// Filiale. Pflege im Filial-Detail → Einstellungen. Keine Lohndaten.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/onboarding-dokumente")]
public class OnboardingDokumenteController : ControllerBase
{
    private readonly string _root;

    public OnboardingDokumenteController(IConfiguration config, IWebHostEnvironment env)
        => _root = ResolveRoot(config, env);

    /// <summary>Wurzel …/onboarding (gleicher Storage wie die Personalakten).</summary>
    public static string ResolveRoot(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        return Path.Combine(configured, "onboarding");
    }

    /// <summary>Filial-Ordner — auch vom ContractShareController genutzt.</summary>
    public static string BranchDir(IConfiguration config, IWebHostEnvironment env, int companyProfileId)
        => Path.Combine(ResolveRoot(config, env), companyProfileId.ToString());

    private string Dir(int companyProfileId)
    {
        var d = Path.Combine(_root, companyProfileId.ToString());
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Dateiname säubern — kein Pfad, kein «..».</summary>
    private static string? SafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Path.GetFileName(name.Trim());
        if (n.Length == 0 || n.Contains("..")) return null;
        return n;
    }

    [HttpGet]
    public IActionResult List([FromQuery] int companyProfileId)
    {
        var files = new DirectoryInfo(Dir(companyProfileId)).GetFiles("*.pdf")
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new { name = f.Name, size = f.Length })
            .ToList();
        return Ok(files);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Upload([FromForm] int companyProfileId, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "DATEI_FEHLT" });
        var name = SafeName(file.FileName);
        if (name == null || !name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "NUR_PDF", message = "Nur PDF-Dateien erlaubt." });
        var path = Path.Combine(Dir(companyProfileId), name);
        await using var fs = System.IO.File.Create(path);
        await file.CopyToAsync(fs);
        return Ok(new { ok = true });
    }

    [HttpDelete]
    public IActionResult Delete([FromQuery] int companyProfileId, [FromQuery] string name)
    {
        var n = SafeName(name);
        if (n == null) return BadRequest(new { error = "NAME_UNGUELTIG" });
        var path = Path.Combine(Dir(companyProfileId), n);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        return Ok(new { ok = true });
    }

    public class CopyDto
    {
        public int CompanyProfileId { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>Dokument in die Ordner ALLER anderen Filialen kopieren (Einmal-Verteilung).</summary>
    [HttpPost("copy-to-all")]
    public async Task<IActionResult> CopyToAll([FromBody] CopyDto dto, [FromServices] AppDbContext db)
    {
        var n = SafeName(dto.Name);
        if (n == null) return BadRequest(new { error = "NAME_UNGUELTIG" });
        var src = Path.Combine(Dir(dto.CompanyProfileId), n);
        if (!System.IO.File.Exists(src)) return NotFound();
        var ids = await db.CompanyProfiles.AsNoTracking().Select(c => c.Id).ToListAsync();
        int kopiert = 0;
        foreach (var id in ids.Where(i => i != dto.CompanyProfileId))
        {
            System.IO.File.Copy(src, Path.Combine(Dir(id), n), overwrite: true);
            kopiert++;
        }
        return Ok(new { ok = true, kopiert });
    }

    /// <summary>Vorschau für HR (inline-PDF).</summary>
    [HttpGet("file")]
    public IActionResult GetFile([FromQuery] int companyProfileId, [FromQuery] string name)
    {
        var n = SafeName(name);
        if (n == null) return BadRequest(new { error = "NAME_UNGUELTIG" });
        var path = Path.Combine(Dir(companyProfileId), n);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/pdf");
    }
}
