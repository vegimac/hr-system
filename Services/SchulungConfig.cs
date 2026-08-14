namespace HrSystem.Services;

/// <summary>
/// Manager-Schulungen (Walter-Vorgabe 14.08.2026): Nothelfer /
/// Peak-Verifizierung / Seco. Gültigkeitsdauer in Monaten, gepflegt als
/// app_setting (editierbar auf der Schulungs-Seite) — hier die Keys +
/// Defaults, geteilt von DashboardService und ManagerSchulungenController.
/// </summary>
public static class SchulungConfig
{
    public const string KeyNothelfer = "Schulung.NothelferMonate";
    public const string KeyPeak      = "Schulung.PeakMonate";
    public const string KeySeco      = "Schulung.SecoMonate";

    // Defaults (von Walter in der UI anpassbar): Nothelfer-Refresh alle
    // 2 Jahre, Peak-Verifizierung + Seco jährlich.
    public const int DefaultNothelfer = 24;
    public const int DefaultPeak      = 12;
    public const int DefaultSeco      = 12;

    public static int ParseMonate(string? value, int fallback)
        => int.TryParse(value, out var n) && n > 0 ? n : fallback;
}
