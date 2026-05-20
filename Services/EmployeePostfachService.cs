using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Verwaltung der Mitarbeiter-Postfach-Accounts. Jeder aktive MA bekommt
/// automatisch einen AppUser-Account mit Rolle "employee", über den er
/// sich einloggen und sein persönliches Postfach (Lohnzettel, Dokumente)
/// einsehen kann.
///
/// Walter-Anforderung: keine separate Verwaltung — Postfach = Mitarbeiter-ID.
///   • Beim Anlegen eines MA wird der Account automatisch erzeugt.
///   • Initial-Passwort = EmployeeNumber selbst (Walter-Vorgabe 17.05.2026,
///     Variante B). MA muss sich nur EINE Sache merken (z.B. "750009"
///     als Username UND als Initial-Passwort). Sicherheit kommt durch
///     MustChangePassword=true, das beim ersten Login einen Wechsel erzwingt.
///   • Pflicht-Wechsel beim ersten Login.
///   • Bei MA-Inaktivität wird der Login-Zugang gesperrt (Account.IsActive=false),
///     das Postfach bleibt aber für 1 Jahr für HR/Admin einsehbar.
/// </summary>
public class EmployeePostfachService
{
    private readonly AppDbContext _db;

    public EmployeePostfachService(AppDbContext db) => _db = db;

    /// <summary>
    /// Erzeugt einen Postfach-Account für den MA, falls noch keiner existiert.
    /// Gibt das Initial-Passwort als Klartext zurück, damit das aufrufende
    /// UI es einmalig dem GF/Admin zeigen kann (zum Aushändigen an den MA).
    /// Falls schon ein Account existiert: gibt null zurück (kein Reset).
    /// </summary>
    public async Task<string?> EnsureAccountAsync(Employee emp, CompanyProfile? primaryCompany)
    {
        // Bestehender Account → nichts tun
        var existing = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == emp.Id);
        if (existing != null) return null;

        var initialPassword = BuildInitialPassword(emp, primaryCompany);
        var account = new AppUser
        {
            EmployeeId         = emp.Id,
            Username           = emp.EmployeeNumber ?? $"emp-{emp.Id}",
            Email              = $"mp-{emp.EmployeeNumber ?? emp.Id.ToString()}@schaub.local",
            FirstName          = emp.FirstName,
            LastName           = emp.LastName,
            Role               = "employee",
            IsActive           = emp.IsActive,
            PasswordHash       = BCrypt.Net.BCrypt.HashPassword(initialPassword),
            MustChangePassword = true,
            CreatedAt          = DateTime.UtcNow
        };
        _db.AppUsers.Add(account);
        await _db.SaveChangesAsync();
        return initialPassword;
    }

    /// <summary>
    /// Setzt das Passwort des Postfach-Accounts auf das Initial-Passwort
    /// zurück (z.B. wenn der MA es vergessen hat). MustChangePassword=true
    /// erzwingt den Wechsel beim nächsten Login. Gibt das neue Passwort
    /// einmalig zurück; falls kein Account existiert wird einer angelegt.
    /// </summary>
    public async Task<string> ResetPasswordAsync(Employee emp, CompanyProfile? primaryCompany)
    {
        var initialPassword = BuildInitialPassword(emp, primaryCompany);
        var account = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == emp.Id);
        if (account == null)
        {
            // Edge-Case: alter MA ohne Account → jetzt nachträglich anlegen
            await EnsureAccountAsync(emp, primaryCompany);
            return initialPassword;
        }

        account.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(initialPassword);
        account.MustChangePassword = true;
        account.FailedLoginCount   = 0;
        account.LockedUntil        = null;
        // Reset hebt auch eine evtl. manuelle Sperre auf
        account.IsActive           = emp.IsActive;
        await _db.SaveChangesAsync();
        return initialPassword;
    }

    /// <summary>
    /// Synchronisiert den AppUser-Aktiv-Status mit dem MA-Status. Bei MA-
    /// Inaktivität (Austritt) wird der Login gesperrt, das Postfach bleibt
    /// aber bestehen. Idempotent — kann nach jedem MA-Update aufgerufen
    /// werden.
    /// </summary>
    public async Task SyncActiveStateAsync(Employee emp)
    {
        var account = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == emp.Id);
        if (account == null) return;
        if (account.IsActive != emp.IsActive)
        {
            account.IsActive = emp.IsActive;
            // Bei Reaktivierung Lockout-Counter zurücksetzen
            if (emp.IsActive)
            {
                account.FailedLoginCount = 0;
                account.LockedUntil      = null;
            }
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Manuelles Aufheben einer Lockout-Sperre (5+ Fehlversuche). Setzt
    /// FailedLoginCount=0 und LockedUntil=NULL — Account bleibt aktiv.
    /// </summary>
    public async Task UnlockAsync(Employee emp)
    {
        var account = await _db.AppUsers.FirstOrDefaultAsync(u => u.EmployeeId == emp.Id);
        if (account == null) return;
        account.FailedLoginCount = 0;
        account.LockedUntil      = null;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Initial-Passwort = EmployeeNumber (Walter-Vorgabe 17.05.2026, Variante B).
    /// Beispiel: MA mit Personalnummer "750009" → Username "750009", Initial-
    /// Passwort "750009". MustChangePassword=true (gesetzt in EnsureAccount/
    /// ResetPassword) erzwingt sofortigen Wechsel beim ersten Login.
    /// CompanyProfile.LoginPasswordPrefix wird nicht mehr genutzt (Dead Code).
    /// Der zweite Parameter bleibt aus Backwards-Compatibility, ist aber
    /// jetzt irrelevant.
    /// </summary>
    private static string BuildInitialPassword(Employee emp, CompanyProfile? primaryCompany)
    {
        return emp.EmployeeNumber ?? emp.Id.ToString();
    }

    /// <summary>
    /// Sucht die Hauptfiliale eines MA über sein aktives Employment.
    /// Wird vom Service intern und vom Controller bei Create/Reset genutzt.
    /// </summary>
    public async Task<CompanyProfile?> GetPrimaryCompanyAsync(int employeeId)
    {
        var primaryId = await _db.Employments
            .Where(e => e.EmployeeId == employeeId && e.IsActive && e.CompanyProfileId.HasValue)
            .OrderByDescending(e => e.ContractStartDate)
            .Select(e => e.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (!primaryId.HasValue) return null;
        return await _db.CompanyProfiles.FindAsync(primaryId.Value);
    }

    /// <summary>
    /// Status-DTO für die Anzeige im MA-Detail. Wird auch vom Backfill
    /// genutzt um zu prüfen ob ein Account schon existiert.
    /// </summary>
    public async Task<PostfachAccountStatus> GetStatusAsync(int employeeId)
    {
        var account = await _db.AppUsers
            .Where(u => u.EmployeeId == employeeId)
            .Select(u => new { u.IsActive, u.MustChangePassword, u.LockedUntil, u.LastLoginAt, u.FailedLoginCount })
            .FirstOrDefaultAsync();

        if (account == null)
            return new PostfachAccountStatus(false, false, false, null, null, 0);

        bool locked = account.LockedUntil.HasValue && account.LockedUntil.Value > DateTime.UtcNow;
        return new PostfachAccountStatus(
            Exists:             true,
            IsActive:           account.IsActive,
            Locked:             locked,
            LockedUntil:        locked ? account.LockedUntil : null,
            LastLoginAt:        account.LastLoginAt,
            FailedLoginCount:   account.FailedLoginCount
        );
    }
}

public record PostfachAccountStatus(
    bool      Exists,
    bool      IsActive,
    bool      Locked,
    DateTime? LockedUntil,
    DateTime? LastLoginAt,
    int       FailedLoginCount
);
