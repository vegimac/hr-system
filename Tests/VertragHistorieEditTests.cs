using HrSystem.Controllers;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Historie-Korrektur an abgeschlossenen Vertragsabschnitten
/// (Walter-Vorgabe 22.09.2026, Fall «mittlerer Vertrag nicht bearbeitbar»).
///
/// Die Lohnlauf-Sperre (17.05.2026) galt bisher für JEDEN Vertrag, dessen
/// Beginn vor dem FirstAllowedDate liegt — also auch für längst abgelöste
/// Abschnitte. Genau die muss HR aber von Hand pflegen: easy@work liefert die
/// Historie vor 2026 unbrauchbar, und der Werdegang im Arbeitszeugnis hängt
/// daran. Die Sperre gilt darum nur noch dort, wo sie gemeint war: auf dem
/// laufenden bzw. jüngsten Vertrag.
///
/// Geprüft wird:
///   • abgelöster Alt-Abschnitt (Ende vor Sperrdatum + jüngerer Vertrag) → editierbar
///   • laufender Vertrag vor dem Sperrdatum → weiterhin 409 LOHN_EDIT_LOCKED
///   • Alt-Abschnitt OHNE jüngeren Vertrag → weiterhin gesperrt
///   • Abschnitt, der ins gesperrte Gebiet hineinreicht → weiterhin gesperrt
///   • Korrektur, die einen anderen Abschnitt überdeckt → 409 VERTRAG_UEBERLAPPUNG
/// </summary>
public class VertragHistorieEditTests
{
    private const int Filiale = 58;

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("HistEdit_" + t + "_" + Guid.NewGuid())
            .Options;
        return new AppDbContext(options);
    }

    private static EmploymentsController Ctrl(AppDbContext db)
        => new(db, new LohnEditLockService(db), new UniformDepotService(db));

    /// <summary>Definitiv abgeschlossene Periode 12/2025 → Sperrdatum 01.01.2026.</summary>
    private static async Task SperreBis2026Async(AppDbContext db)
    {
        db.PayrollPerioden.Add(new PayrollPeriode
        {
            CompanyProfileId = Filiale,
            Year = 2025, Month = 12, Label = "12/2025",
            PeriodFrom = new DateOnly(2025, 12, 1),
            PeriodTo   = new DateOnly(2025, 12, 31),
            Status = "abgeschlossen",
        });
        await db.SaveChangesAsync();
    }

    private static Employment Vertrag(int id, DateTime von, DateTime? bis, string funktion = "CREW")
        => new()
        {
            Id = id,
            EmployeeId = 1,
            CompanyProfileId = Filiale,
            ContractStartDate = von,
            ContractEndDate   = bis,
            EmploymentModel   = "FIX-M",
            SalaryType        = "monthly",
            JobTitle          = funktion,
            IsActive          = true,
        };

    /// <summary>DTO für den PUT — nur die Felder, die der Test ändert.</summary>
    private static Employment Aenderung(DateTime von, DateTime? bis, string funktion)
        => new()
        {
            ContractStartDate = von,
            ContractEndDate   = bis,
            EmploymentModel   = "FIX-M",
            SalaryType        = "monthly",
            JobTitle          = funktion,
            IsActive          = true,
        };

    [Fact]
    public async Task AbgeloesterAltAbschnitt_IstEditierbar()
    {
        using var db = NewDb();
        await SperreBis2026Async(db);
        db.Employments.Add(Vertrag(1, new DateTime(2024, 6, 1), new DateTime(2025, 5, 31), "CREW"));
        db.Employments.Add(Vertrag(2, new DateTime(2025, 6, 1), null, "SHIFT_LEADER_7_PLUS"));
        await db.SaveChangesAsync();

        var res = await Ctrl(db).Update(1,
            Aenderung(new DateTime(2024, 6, 1), new DateTime(2025, 5, 31), "REST_MANAGER"));

        Assert.IsType<OkObjectResult>(res);
        Assert.Equal("REST_MANAGER", (await db.Employments.FindAsync(1))!.JobTitle);
    }

    [Fact]
    public async Task LaufenderVertragVorSperrdatum_BleibtGesperrt()
    {
        using var db = NewDb();
        await SperreBis2026Async(db);
        db.Employments.Add(Vertrag(1, new DateTime(2024, 6, 1), null, "CREW"));
        await db.SaveChangesAsync();

        var res = await Ctrl(db).Update(1, Aenderung(new DateTime(2024, 6, 1), null, "REST_MANAGER"));

        var conflict = Assert.IsType<ConflictObjectResult>(res);
        Assert.Contains("LOHN_EDIT_LOCKED", conflict.Value!.ToString());
    }

    [Fact]
    public async Task AltAbschnittOhneNachfolger_BleibtGesperrt()
    {
        // Ausgetretener MA: der letzte Abschnitt ist zwar beendet, aber niemand
        // hat ihn abgelöst — er bleibt der jüngste und damit gesperrt.
        using var db = NewDb();
        await SperreBis2026Async(db);
        db.Employments.Add(Vertrag(1, new DateTime(2024, 6, 1), new DateTime(2025, 5, 31), "CREW"));
        await db.SaveChangesAsync();

        var res = await Ctrl(db).Update(1,
            Aenderung(new DateTime(2024, 6, 1), new DateTime(2025, 5, 31), "REST_MANAGER"));

        Assert.IsType<ConflictObjectResult>(res);
    }

    [Fact]
    public async Task AbschnittReichtInsGesperrteGebiet_BleibtGesperrt()
    {
        // Ende 31.03.2026 liegt NACH dem Sperrdatum 01.01.2026 — eine Änderung
        // könnte eine noch offene Periode betreffen. Bleibt tabu.
        using var db = NewDb();
        await SperreBis2026Async(db);
        db.Employments.Add(Vertrag(1, new DateTime(2024, 6, 1), new DateTime(2026, 3, 31), "CREW"));
        db.Employments.Add(Vertrag(2, new DateTime(2026, 4, 1), null, "SHIFT_LEADER_7_PLUS"));
        await db.SaveChangesAsync();

        var res = await Ctrl(db).Update(1,
            Aenderung(new DateTime(2024, 6, 1), new DateTime(2026, 3, 31), "REST_MANAGER"));

        Assert.IsType<ConflictObjectResult>(res);
    }

    [Fact]
    public async Task HistorieKorrektur_DarfNichtUeberlappen()
    {
        using var db = NewDb();
        await SperreBis2026Async(db);
        db.Employments.Add(Vertrag(1, new DateTime(2024, 6, 1), new DateTime(2025, 5, 31), "CREW"));
        db.Employments.Add(Vertrag(2, new DateTime(2025, 6, 1), null, "SHIFT_LEADER_7_PLUS"));
        await db.SaveChangesAsync();

        // Ende über den Beginn des Folgevertrags hinausgeschoben.
        var res = await Ctrl(db).Update(1,
            Aenderung(new DateTime(2024, 6, 1), new DateTime(2025, 8, 31), "CREW"));

        var conflict = Assert.IsType<ConflictObjectResult>(res);
        Assert.Contains("VERTRAG_UEBERLAPPUNG", conflict.Value!.ToString());
    }

    [Fact]
    public async Task HistorieKorrektur_SchliesstLueckeBisZumFolgevertrag()
    {
        // Der typische Fall: Ende war einen Tag zu früh (easy@work-Intervall).
        using var db = NewDb();
        await SperreBis2026Async(db);
        db.Employments.Add(Vertrag(1, new DateTime(2024, 6, 1), new DateTime(2025, 5, 30), "CREW"));
        db.Employments.Add(Vertrag(2, new DateTime(2025, 6, 1), null, "SHIFT_LEADER_7_PLUS"));
        await db.SaveChangesAsync();

        var res = await Ctrl(db).Update(1,
            Aenderung(new DateTime(2024, 6, 1), new DateTime(2025, 5, 31), "CREW"));

        Assert.IsType<OkObjectResult>(res);
        Assert.Equal(new DateTime(2025, 5, 31), (await db.Employments.FindAsync(1))!.ContractEndDate);
    }
}
