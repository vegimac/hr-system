using HrSystem.Data;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Zivilstand-Wechsel aus der MA-Maske gilt ab «Zivilstand seit», ohne «Erfahren am»
/// (Walter 02.11.2026, TF12 Casanova: Februar 2025 meldete noch den alten Stand, weil
/// «Erfahren am» = heute gesetzt wurde). Der easy@work-Sync setzt weiterhin heute.
/// </summary>
public class ZivilstandErfahrenAmTests
{
    private static AppDbContext Db(string n)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("ZivErf_" + n + "_" + Guid.NewGuid()).Options);

    [Fact]
    public async Task Maske_OhneErfahrenAm_GiltRueckwirkend()
    {
        using var db = Db(nameof(Maske_OhneErfahrenAm_GiltRueckwirkend));
        await new ZivilstandHistorieService(db).NachfuehrenAsync(12, "aufgeloeste_partnerschaft",
            "aufgeloeste_partnerschaft_verschollen", new DateOnly(2023, 6, 15), "Korrektur Testdaten (MA-Maske)",
            erfahrenAm: null, erfahrenAmExplizit: true);
        await db.SaveChangesAsync();
        var (z, seit, _) = ZivilstandHistorieService.Am(db.EmployeeZivilstandHistories.ToList(),
            new DateOnly(2025, 2, 28), null, null);
        Assert.Equal("aufgeloeste_partnerschaft_verschollen", z);
        Assert.Equal(new DateOnly(2023, 6, 15), seit);
    }

    [Fact]
    public async Task Sync_SetztErfahrenAmHeute()
    {
        using var db = Db(nameof(Sync_SetztErfahrenAmHeute));
        await new ZivilstandHistorieService(db).NachfuehrenAsync(5, "ledig", "verheiratet",
            new DateOnly(2025, 6, 1), "aus easy@work übernommen");
        await db.SaveChangesAsync();
        var neu = db.EmployeeZivilstandHistories.Single(h => h.Zivilstand == "verheiratet");
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), neu.ErfahrenAm);
    }
}
