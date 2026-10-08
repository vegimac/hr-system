using System.Reflection;
using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Darf der eingeloggte Benutzer diesen Mitarbeiter sehen/ändern? (Walter-Vorgabe 08.10.2026)
///
/// Eingeschränkt sind GF (<c>user</c>), <c>lowuser</c> und Buchhaltung (hat den superuser-Claim,
/// bleibt aber auf ihre Filialen beschränkt) — wie die MA-Liste (EmployeesController.GetAll).
/// Admin und Superuser sehen alle Filialen; die
/// MA-Rolle <c>employee</c> prüft ihre Eigentümerschaft in den wenigen offenen Endpunkten selbst.
///
/// Erlaubt ist ein MA, wenn er irgendeine Anstellung in einer Filiale des Benutzers hat
/// (auch eine frühere — Übertritts-MA bleiben in der alten Filiale unter «Inaktiv» sichtbar)
/// oder noch gar keine Anstellung (frisch erfasster MA im Import, Vertrag folgt).
/// </summary>
public sealed class MaFilialZugriff
{
    private readonly AppDbContext _db;
    private HashSet<int>? _filialen;
    private readonly Dictionary<int, bool> _erlaubt = new();

    public MaFilialZugriff(AppDbContext db) => _db = db;

    public static bool IstEingeschraenkt(ClaimsPrincipal u)
    {
        if (u.IsInRole("admin")) return false;
        if (u.IsInRole("buchhaltung")) return true;
        if (u.IsInRole("superuser")) return false;
        return u.IsInRole("user") || u.IsInRole("lowuser");
    }

    public async Task<HashSet<int>> FilialenAsync(ClaimsPrincipal u)
    {
        if (_filialen != null) return _filialen;
        var uid = int.TryParse(u.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var v) ? v : 0;
        _filialen = (await _db.UserBranchAccesses.AsNoTracking()
            .Where(a => a.UserId == uid).Select(a => a.CompanyProfileId).ToListAsync()).ToHashSet();
        return _filialen;
    }

    public async Task<bool> DarfFilialeAsync(ClaimsPrincipal u, int companyProfileId)
        => !IstEingeschraenkt(u) || (await FilialenAsync(u)).Contains(companyProfileId);

    public async Task<bool> DarfMitarbeiterAsync(ClaimsPrincipal u, int employeeId)
    {
        if (!IstEingeschraenkt(u)) return true;
        if (_erlaubt.TryGetValue(employeeId, out var ok)) return ok;
        var filialen = await FilialenAsync(u);
        var vertraege = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => e.CompanyProfileId)
            .ToListAsync();
        ok = vertraege.Count == 0 || vertraege.Any(c => c.HasValue && filialen.Contains(c.Value));
        _erlaubt[employeeId] = ok;
        return ok;
    }

    /// <summary>EmployeeId eines Eintrags (Bankverbindung, Absenz, Dokument …) über seine Id; null = gibt es nicht.</summary>
    public async Task<int?> MitarbeiterVonEintragAsync(Type typ, int id)
    {
        if (typ == typeof(Employee)) return id;
        var m = typeof(MaFilialZugriff).GetMethod(nameof(EmployeeIdVon), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typ);
        return await (Task<int?>)m.Invoke(null, new object[] { _db, id })!;
    }

    private static Task<int?> EmployeeIdVon<T>(AppDbContext db, int id) where T : class
        => db.Set<T>().AsNoTracking()
            .Where(e => EF.Property<int>(e, "Id") == id)
            .Select(e => (int?)EF.Property<int>(e, "EmployeeId"))
            .FirstOrDefaultAsync();

    public static ObjectResult KeinZugriff() => new(new
    {
        error = "MA_ANDERE_FILIALE",
        message = "Dieser Mitarbeiter gehört nicht zu deinen Filialen.",
    }) { StatusCode = StatusCodes.Status403Forbidden };
}

/// <summary>
/// Die Route-Id <see cref="Parameter"/> dieser Aktion (oder aller Aktionen des Controllers)
/// gehört zu einem Eintrag vom Typ <see cref="Typ"/> mit Spalte EmployeeId — der globale
/// <see cref="MaFilialFilter"/> prüft dann den MA dieses Eintrags. Typ <c>Employee</c> = die Id
/// ist selbst die MA-Id.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MaEintragAttribute : Attribute
{
    public MaEintragAttribute(Type typ) => Typ = typ;
    public Type Typ { get; }
    public string Parameter { get; init; } = "id";
}

/// <summary>Aktion prüft die Filiale selbst (z.B. Vertrag anlegen beim Übertritt) — Filter überspringt sie.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class OhneMaFilialPruefungAttribute : Attribute { }

/// <summary>
/// Global registriert (wie der Virenscanner): für GF und Buchhaltung wird jeder MA geprüft,
/// den eine Aktion anfasst — Parameter <c>employeeId</c>/<c>empId</c> (Route, Query, Formular),
/// die Eigenschaft <c>EmployeeId</c> eines übergebenen Objekts (JSON-Body/Formular) und die Route-Id bei <see cref="MaEintragAttribute"/>.
/// Neue Endpunkte mit diesen Namen sind damit automatisch geschützt.
/// </summary>
public sealed class MaFilialFilter : IAsyncActionFilter
{
    private static readonly string[] MaParameter = { "employeeId", "empId" };
    private readonly MaFilialZugriff _zugriff;

    public MaFilialFilter(MaFilialZugriff zugriff) => _zugriff = zugriff;

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var user = ctx.HttpContext.User;
        if (!MaFilialZugriff.IstEingeschraenkt(user) || ctx.ActionDescriptor is not ControllerActionDescriptor ad
            || Hat<OhneMaFilialPruefungAttribute>(ad, out _))
        {
            await next();
            return;
        }

        foreach (var id in await MitarbeiterAsync(ctx, ad))
        {
            if (!await _zugriff.DarfMitarbeiterAsync(user, id))
            {
                ctx.Result = MaFilialZugriff.KeinZugriff();
                return;
            }
        }
        await next();
    }

    private async Task<List<int>> MitarbeiterAsync(ActionExecutingContext ctx, ControllerActionDescriptor ad)
    {
        var ids = new List<int>();
        foreach (var p in ad.Parameters)
        {
            if (!ctx.ActionArguments.TryGetValue(p.Name, out var wert) || wert == null) continue;
            if (MaParameter.Any(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (wert is int i) ids.Add(i);
            }
            else if (wert is not string && wert.GetType().IsClass
                     && wert.GetType().GetProperty("EmployeeId") is { } prop
                     && prop.GetValue(wert) is int bodyId && bodyId > 0)
            {
                ids.Add(bodyId);
            }
        }

        if (Hat<MaEintragAttribute>(ad, out var eintrag)
            && ctx.ActionArguments.TryGetValue(eintrag!.Parameter, out var roh) && roh is int eintragId
            && await _zugriff.MitarbeiterVonEintragAsync(eintrag.Typ, eintragId) is int maId)
        {
            ids.Add(maId);
        }
        return ids;
    }

    private static bool Hat<T>(ControllerActionDescriptor ad, out T? attr) where T : Attribute
    {
        attr = ad.MethodInfo.GetCustomAttribute<T>() ?? ad.ControllerTypeInfo.GetCustomAttribute<T>();
        return attr != null;
    }
}
