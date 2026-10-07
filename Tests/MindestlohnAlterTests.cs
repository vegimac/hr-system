using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Jugend-Mindestlohn (age_max = 17) gilt bis zum vollendeten 18. Lebensjahr;
/// massgebend ist das Alter am Ende des Stichtag-Monats (Walter 07.10.2026).
/// </summary>
public class MindestlohnAlterTests
{
    [Fact]
    public void Geburtstag_am_30_Juli_ab_1_Juli_erwachsen()
    {
        var geburt = new DateTime(2008, 7, 30);
        Assert.Equal(17, MindestlohnAlter.AlterFuerRegel(geburt, new DateTime(2026, 6, 30)));
        Assert.Equal(18, MindestlohnAlter.AlterFuerRegel(geburt, new DateTime(2026, 7, 1)));
        Assert.Equal(18, MindestlohnAlter.AlterFuerRegel(geburt, new DateOnly(2026, 7, 15)));
    }

    [Fact]
    public void Geburtstag_am_1_des_Monats_ab_diesem_Monat_erwachsen()
    {
        var geburt = new DateTime(2008, 8, 1);
        Assert.Equal(17, MindestlohnAlter.AlterFuerRegel(geburt, new DateTime(2026, 7, 31)));
        Assert.Equal(18, MindestlohnAlter.AlterFuerRegel(geburt, new DateTime(2026, 8, 1)));
    }

    [Fact]
    public void Ohne_Geburtsdatum_kein_Alter()
        => Assert.Null(MindestlohnAlter.AlterFuerRegel(null, new DateTime(2026, 7, 1)));

    [Fact]
    public void Neunundzwanzigster_Februar_stuerzt_nicht_ab()
    {
        var geburt = new DateTime(2008, 2, 29);
        Assert.Equal(17, MindestlohnAlter.AlterFuerRegel(geburt, new DateTime(2026, 1, 15)));
        Assert.Equal(18, MindestlohnAlter.AlterFuerRegel(geburt, new DateTime(2026, 2, 1)));
        Assert.Equal(17, MindestlohnAlter.Alter(geburt, new DateTime(2026, 2, 27)));
        Assert.Equal(18, MindestlohnAlter.Alter(geburt, new DateTime(2026, 2, 28)));
    }
}
