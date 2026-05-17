using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HrSystem.Controllers;

/// <summary>
/// Dashboard-Cockpit: liefert Alarme/Erinnerungen.
/// GET /api/dashboard?companyProfileId=X (optional, sonst alle)
/// </summary>
[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _svc;
    public DashboardController(DashboardService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? companyProfileId)
    {
        var data = await _svc.BuildAsync(companyProfileId);
        return Ok(data);
    }
}
