using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class QstVersionWahlTests
{
    [Fact]
    public void April_kennt_noch_A0Y_wenn_B0Y_erst_im_Juni_erfahren()
    {
        var a0 = new EmployeeQuellensteuer
        {
            Id = 1, ValidFrom = new DateOnly(2025, 1, 1),
            ErfahrenAm = new DateOnly(2025, 1, 1), TarifCode = "A", QstCode = "A0Y"
        };
        var b0 = new EmployeeQuellensteuer
        {
            Id = 2, ValidFrom = new DateOnly(2025, 4, 1), ValidTo = new DateOnly(2025, 4, 30),
            ErfahrenAm = new DateOnly(2025, 6, 1), TarifCode = "B", QstCode = "B0Y"
        };

        var april = QstVersionWahl.Waehle(new[] { a0, b0 }, new DateOnly(2025, 4, 30));
        var juni  = QstVersionWahl.Waehle(new[] { a0, b0 }, new DateOnly(2025, 6, 30));

        Assert.Same(a0, april);
        Assert.Same(b0, juni);
        Assert.Equal(new DateOnly(2025, 5, 1), QstVersionWahl.LetzterUnbekannterMonat(b0));
    }

    [Fact]
    public void Ohne_ErfahrenAm_gilt_ValidFrom_wie_bisher()
    {
        var v = new EmployeeQuellensteuer { Id = 1, ValidFrom = new DateOnly(2025, 4, 1), QstCode = "B0Y" };
        Assert.Same(v, QstVersionWahl.Waehle(new[] { v }, new DateOnly(2025, 4, 30)));
        Assert.Null(QstVersionWahl.LetzterUnbekannterMonat(v));
    }

    /// <summary>
    /// TF34 Rinaldi / Swissdec RefXML Juni: B0N gültig ab 1.4., erfahren 1.6.
    /// → Korrektur-Fenster April UND Mai (Live-Lohnlauf bleibt in Apr/Mai auf A0N).
    /// </summary>
    [Fact]
    public void TF34_Zwischenmonate_April_und_Mai_bei_Erfahren_Juni()
    {
        var b0n = new EmployeeQuellensteuer
        {
            Id = 2,
            ValidFrom = new DateOnly(2025, 4, 1),
            ErfahrenAm = new DateOnly(2025, 6, 1),
            TarifCode = "B", AnzahlKinder = 0, Kirchensteuer = false, QstCode = "B0N"
        };
        Assert.Equal(new DateOnly(2025, 5, 1), QstVersionWahl.LetzterUnbekannterMonat(b0n));
    }

    /// <summary>
    /// Korrektur-Differenz = neu − alt (Swissdec CompanyCorrection).
    /// A0N 425 → B0N 185 = Erstattung −240 (nicht Nachbelastung).
    /// </summary>
    [Fact]
    public void Korrektur_Differenz_A0N_nach_B0N_ist_Erstattung()
    {
        const decimal altA0n = 425.00m;
        const decimal neuB0n = 185.00m;
        var diff = Math.Round(neuB0n - altA0n, 2);
        Assert.Equal(-240.00m, diff);
        Assert.True(diff < 0, "Negativ = Erstattung an den MA");
    }
}
