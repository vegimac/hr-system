using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HrSystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Gemeinsame Basis für HR-Controller: bündelt die zwei Helfer, die in fast
/// jedem Controller in leichten Varianten kopiert wurden — und schliesst dabei
/// einen subtilen Sicherheits-Drift, den manche Kopien hatten (FindFirst statt
/// FindAll → nur erster Role-Claim gelesen).
///
/// Walter-Vorgabe 09.06.2026.
///
/// Sub-Klassen:
///   public class FooController(AppDbContext db) : HrControllerBase(db) { ... }
/// oder:
///   public FooController(AppDbContext db, ...) : base(db) { ... }
/// </summary>
public abstract class HrControllerBase : ControllerBase
{
    protected readonly AppDbContext _db;

    protected HrControllerBase(AppDbContext db) => _db = db;

    /// <summary>JWT-Subject-Id als int (oder null wenn der Token kein NameIdentifier hat).</summary>
    protected int? GetCurrentUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var v) ? v : null;

    /// <summary>
    /// Filial-Zugriff prüfen.
    ///
    /// Walter-Vorgabe 24.05.2026 (PayrollController) übernommen, gilt jetzt
    /// zentral: admin und superuser sehen ALLE Filialen — AUSSER ein User hat
    /// zusätzlich den buchhaltung-Claim. Buchhaltung-User bekommen via
    /// AuthController.GenerateToken zwei Rollen-Claims (buchhaltung + superuser),
    /// damit sie auf alle [Authorize(Roles="admin,superuser")]-Endpunkte
    /// kommen — sollen aber DENNOCH nur ihre zugeteilten Filialen sehen.
    /// Buchhaltung wird daher ZUERST gegen die UserBranchAccess-Liste geprüft;
    /// der globale Zugriff aus dem superuser-Claim wird unterdrückt.
    ///
    /// `FindAll` (nicht `FindFirst`) liest ALLE Role-Claims — das ist wichtig
    /// für den Doppel-Claim-Fall.
    /// </summary>
    protected async Task<bool> CanAccessBranchAsync(int companyProfileId)
    {
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet();

        // Buchhaltung ZUERST prüfen: hat zwar den superuser-Claim mit, soll
        // aber NUR ihre zugeteilten Filialen sehen.
        if (!roles.Contains("buchhaltung")
            && (roles.Contains("admin") || roles.Contains("superuser")))
        {
            return true;
        }

        var uid = GetCurrentUserId();
        if (uid is null) return false;
        return await _db.UserBranchAccesses
            .AnyAsync(uba => uba.UserId == uid.Value && uba.CompanyProfileId == companyProfileId);
    }
}
