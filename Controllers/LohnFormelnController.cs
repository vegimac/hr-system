using HrSystem.Services.LohnFormelwerk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>Entwicklung → Lohn-Formeln — read-only aus der Registry (eine Wahrheit).</summary>
[ApiController]
[Route("api/lohn-formeln")]
[Authorize(Roles = "admin")]
public class LohnFormelnController : ControllerBase
{
    [HttpGet]
    public IActionResult Liste()
    {
        var formeln = LohnFormelwerk.Alle.Select(f => new
        {
            f.Id,
            f.Titel,
            f.FormelText,
            f.ParameterQuellen,
            f.CodeAnker,
            f.SchemaArt,
            f.SwissdecCode,
            f.MirusCode,
        }).ToList();

        var inventar = LohnFormelInventar.Alle.Select(z => new
        {
            z.AlterCode,
            z.SwissdecCode,
            z.FormelId,
            z.SchemaArt,
            z.Bemerkung,
        }).ToList();

        return Ok(new
        {
            formeln,
            inventar,
            hinweis = "Formeln sind Code — nicht in der UI editierbar. Parameter (Ferien-%, SV-Sätze, Nachtfenster) bleiben in den Stammdaten.",
        });
    }

    [HttpGet("{id}")]
    public IActionResult Eine(string id)
    {
        var f = LohnFormelwerk.Finde(id);
        if (f == null) return NotFound(new { message = $"Formel «{id}» unbekannt." });
        return Ok(f);
    }
}
