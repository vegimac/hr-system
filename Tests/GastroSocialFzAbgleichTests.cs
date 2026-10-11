using HrSystem.Services;
using Xunit;
using static HrSystem.Services.GastroSocialFzAbgleich;

namespace HrSystem.Tests;

public class GastroSocialFzAbgleichTests
{
    private const string AhvMa = "756.1234.5678.97";
    private const string AhvKind = "756.9876.5432.10";

    private static Zeile Gs(DateTime von, DateTime bis, decimal ansatz, string? kindAhv = AhvKind, string bezAhv = AhvMa,
        string kind = "Lea", string leistung = "Normale Leistungen")
    {
        int monate = (bis.Year - von.Year) * 12 + bis.Month - von.Month + 1;
        return new Zeile
        {
            Nr = 2, Leistung = leistung, BezuegerAhv = bezAhv, BezuegerName = "Muster", BezuegerVorname = "Anna",
            KindAhv = kindAhv, KindVorname = kind, KindGeburt = new DateTime(2015, 3, 1),
            Von = von, Bis = bis, Monate = monate, Ansatz = ansatz, Total = monate * ansatz,
        };
    }

    private static Zulage Zl(DateOnly von, DateOnly? bis, decimal betrag, string typ = "KZ")
    {
        var z = new Zulage { Id = 1, Typ = typ, Satz = 1, Von = von, Bis = bis };
        foreach (var m in Monate(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)))
            if (von <= m.AddMonths(1).AddDays(-1) && (bis == null || bis >= m)) z.Betraege[m] = betrag;
        return z;
    }

    private static Ma MaMit(params Zulage[] zulagen) => new()
    {
        Id = 1, Vorname = "Anna", Nachname = "Muster", Ahv = AhvMa, InFiliale = true,
        VertraegeHier = { (new DateTime(2024, 1, 1), null) },
        Kinder = { new Kind { Id = 10, Vorname = "Lea", Nachname = "Muster", Ahv = AhvKind, Geburt = new DateTime(2015, 3, 1), Zulagen = zulagen.ToList() } },
    };

    private static readonly DateTime Jan = new(2026, 1, 1), Jul = new(2026, 7, 31), Dez = new(2026, 12, 31);

    [Fact]
    public void Gleiche_Monate_und_Ansatz_passt()
    {
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225) }, new[] { MaMit(Zl(new(2026, 1, 1), null, 225)) });
        var t = Assert.Single(r.Passt);
        Assert.Equal(2700m, t.OcTotal);
        Assert.Empty(r.Fehlt);
        Assert.Empty(r.Abweichung);
    }

    [Fact]
    public void Abgelaufene_Zulage_mit_gleichem_Ende_passt()
    {
        var r = Vergleiche(new[] { Gs(Jan, Jul, 278) }, new[] { MaMit(Zl(new(2026, 1, 1), new(2026, 7, 31), 278)) });
        Assert.Single(r.Passt);
    }

    [Fact]
    public void Kind_ohne_Zulage_fehlt()
    {
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225) }, new[] { MaMit() });
        var t = Assert.Single(r.Fehlt);
        Assert.Contains("01.2026–12.2026", t.Hinweis);
        Assert.Equal(10, t.FamilyMemberId);
    }

    [Fact]
    public void Kind_nicht_erfasst_fehlt()
    {
        var ma = MaMit();
        ma.Kinder.Clear();
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225) }, new[] { ma });
        var t = Assert.Single(r.Fehlt);
        Assert.Contains("nicht erfasst", t.Hinweis);
        Assert.Equal(1, t.EmployeeId);
    }

    [Fact]
    public void Spaeterer_Beginn_und_anderer_Ansatz_ist_Abweichung()
    {
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225) }, new[] { MaMit(Zl(new(2026, 3, 1), null, 215)) });
        var t = Assert.Single(r.Abweichung);
        Assert.Contains("fehlt 01.2026–02.2026", t.Hinweis);
        Assert.Contains("GastroSocial 225.00, OneCrew 215.00", t.Hinweis);
    }

    [Fact]
    public void OneCrew_zahlt_ueber_GastroSocial_Ende_hinaus()
    {
        var r = Vergleiche(new[] { Gs(Jan, Jul, 278), Gs(Jan, Dez, 225, kindAhv: null, kind: "Tim") },
            new[] { MaMit(Zl(new(2026, 1, 1), null, 278)) });
        var t = Assert.Single(r.Abweichung);
        Assert.Contains("zusätzlich 08.2026–12.2026", t.Hinweis);
    }

    [Fact]
    public void Zulage_nur_in_OneCrew_wird_gemeldet()
    {
        var ma = MaMit(Zl(new(2026, 1, 1), null, 225));
        var andere = new Ma { Id = 2, Vorname = "Beat", Nachname = "Beispiel", Ahv = "756.1111.2222.33", InFiliale = true };
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225, kindAhv: "756.5555.6666.77", bezAhv: "756.1111.2222.33", kind: "Max") },
            new[] { ma, andere });
        var t = Assert.Single(r.NurOneCrew);
        Assert.Equal(1, t.EmployeeId);
        Assert.Contains("01.2026–12.2026", t.Hinweis);
    }

    [Fact]
    public void Nur_in_OneCrew_zaehlt_nur_Vertragsmonate_dieser_Filiale()
    {
        var ma = MaMit(Zl(new(2026, 1, 1), null, 225));
        ma.VertraegeHier = new() { (new DateTime(2024, 1, 1), new DateTime(2025, 12, 31)) };
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225, bezAhv: "756.0000.0000.00", kindAhv: null, kind: "X") }, new[] { ma });
        Assert.Empty(r.NurOneCrew);
    }

    [Fact]
    public void Bezueger_unbekannt_ist_nicht_zuordenbar()
    {
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225, bezAhv: "756.0000.0000.00", kindAhv: null) },
            new[] { new Ma { Id = 2, Vorname = "Beat", Nachname = "Beispiel", InFiliale = true } });
        Assert.Single(r.NichtZuordenbar);
    }

    [Fact]
    public void Match_ueber_Kind_AHV_wenn_MA_AHV_fehlt()
    {
        var ma = MaMit(Zl(new(2026, 1, 1), null, 225));
        ma.Ahv = null;
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225) }, new[] { ma });
        var t = Assert.Single(r.Passt);
        Assert.Equal("KIND_AHV", t.MatchArt);
        Assert.Contains("AHV-Nummer des MA fehlt", t.Hinweis);
    }

    [Fact]
    public void Wechsel_KZ_zu_AZ_im_Jahr_zwei_Zeilen_ein_Kind()
    {
        var kz = Zl(new(2026, 1, 1), new(2026, 2, 28), 225);
        var az = Zl(new(2026, 3, 1), null, 278, "AZ");
        az.Id = 2;
        var r = Vergleiche(new[] { Gs(Jan, new DateTime(2026, 2, 28), 225), Gs(new DateTime(2026, 3, 1), Dez, 278) },
            new[] { MaMit(kz, az) });
        Assert.Equal(1, r.Kinder);
        Assert.Single(r.Passt);
    }

    [Fact]
    public void Fehlende_Kind_AHV_kann_uebernommen_werden()
    {
        var ma = MaMit(Zl(new(2026, 1, 1), null, 225));
        ma.Kinder[0].Ahv = null;
        var r = Vergleiche(new[] { Gs(Jan, Dez, 225, kindAhv: "756.9876.5432.17") }, new[] { ma });
        var t = Assert.Single(r.Passt);
        Assert.Equal("756.9876.5432.17", t.KindAhvUebernehmen);
        Assert.Contains("AHV-Nummer des Kindes fehlt", t.Hinweis);
    }

    [Fact]
    public void Ungueltige_oder_abweichende_Kind_AHV_wird_nicht_angeboten()
    {
        var ohne = MaMit(Zl(new(2026, 1, 1), null, 225));
        ohne.Kinder[0].Ahv = null;
        var r1 = Vergleiche(new[] { Gs(Jan, Dez, 225, kindAhv: "756.9876.5432.10") }, new[] { ohne });
        Assert.Null(Assert.Single(r1.Passt).KindAhvUebernehmen);

        var anders = MaMit(Zl(new(2026, 1, 1), null, 225));
        var r2 = Vergleiche(new[] { Gs(Jan, Dez, 225, kindAhv: "756.1111.2222.39") }, new[] { anders });
        Assert.Null(Assert.Single(r2.Passt).KindAhvUebernehmen);
    }

    [Fact]
    public void Monatsbereiche_werden_zusammengefasst()
    {
        var m = new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 5, 1) };
        Assert.Equal("01.2026–02.2026, 05.2026", Bereiche(m));
    }
}
