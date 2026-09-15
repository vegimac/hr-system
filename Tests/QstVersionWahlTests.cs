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
}
