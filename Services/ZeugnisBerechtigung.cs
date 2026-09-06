using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Wer darf Arbeitszeugnisse DRUCKEN (Walter 06.09.2026)? Die Funktionen
/// sind eine Leiter: Crew → Crew-Trainer → Schichtkoordinator →
/// Geschäftsführer. Pro Benutzer eine Stufe «Zeugnisse drucken bis Funktion»
/// (app_user.zeugnis_druck_bis, NULL = Standard nach Rolle):
///   admin                        → alle (inkl. Geschäftsführer-Zeugnisse)
///   HR-Team / superuser / buchh. → schicht
///   user (GF)                    → ct
///   lowuser                      → keine
/// Liegt die Zeugnis-Funktion ÜBER der Stufe, kann der Benutzer die Maske
/// zwar ausfüllen, aber nur als Entwurf an HR senden.
/// </summary>
public static class ZeugnisBerechtigung
{
    public const string Keine = "keine", Crew = "crew", Ct = "ct", Schicht = "schicht", Alle = "alle";
    public static readonly string[] Codes = { Keine, Crew, Ct, Schicht, Alle };

    public static int Stufe(string? code) => (code ?? "").Trim().ToLowerInvariant() switch
    {
        Crew    => 1,
        Ct      => 2,
        Schicht => 3,
        Alle    => 4,
        _       => 0,
    };

    public static string StandardCode(string? role, bool isHrTeam)
    {
        var r = (role ?? "").Trim().ToLowerInvariant();
        if (r == "admin") return Alle;
        if (isHrTeam || r == "superuser" || r == "buchhaltung") return Schicht;
        if (r == "user") return Ct;
        return Keine;
    }

    /// <summary>Effektive Stufe: Admin immer alle, sonst Benutzerwert oder Rollen-Standard.</summary>
    public static string Effektiv(string? role, bool isHrTeam, string? zeugnisDruckBis)
    {
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)) return Alle;
        var v = (zeugnisDruckBis ?? "").Trim().ToLowerInvariant();
        return Codes.Contains(v) ? v : StandardCode(role, isHrTeam);
    }

    public static string Effektiv(AppUser u) => Effektiv(u.Role, u.IsHrTeam, u.ZeugnisDruckBis);

    /// <summary>Stufe der Zeugnis-Funktion (Text aus der Maske).</summary>
    public static int FunktionStufe(string? funktion)
    {
        var f = (funktion ?? "").Trim();
        if (f.StartsWith("Geschäftsführer", StringComparison.OrdinalIgnoreCase)
            || f.StartsWith("Geschaeftsfuehrer", StringComparison.OrdinalIgnoreCase)
            || f.StartsWith("Restaurant-Manager", StringComparison.OrdinalIgnoreCase)) return 4;
        if (f.StartsWith("Schichtkoordinator", StringComparison.OrdinalIgnoreCase)) return 3;
        if (f.StartsWith("Crew-Trainer", StringComparison.OrdinalIgnoreCase)) return 2;
        return 1;
    }

    public static bool DarfDrucken(AppUser u, string? funktion)
        => Stufe(Effektiv(u)) >= FunktionStufe(funktion);

    public static string Label(string? code) => (code ?? "").ToLowerInvariant() switch
    {
        Crew    => "Crew",
        Ct      => "Crew-Trainer/in",
        Schicht => "Schichtkoordinator/in",
        Alle    => "alle (inkl. Geschäftsführer/in)",
        _       => "keine",
    };
}
