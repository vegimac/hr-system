// DeductionRulesController – deaktiviert
// Die deduction_rule Tabelle wurde entfernt.
// SV-Sätze werden ausschliesslich über SocialInsuranceRatesController verwaltet.
// Dieser Controller ist ein leerer Stub um Compile-Fehler zu vermeiden.

using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/deduction-rules")]
[Authorize]
public class DeductionRulesController : ControllerBase
{
    public DeductionRulesController(AppDbContext db) { }

    [HttpGet]
    public IActionResult GetAll() =>
        Ok(new { message = "deduction_rule entfernt. Bitte SV-Sätze unter /api/social-insurance-rates verwalten." });
}
