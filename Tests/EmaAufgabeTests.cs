using HrSystem.Services;
using Xunit;
using R = HrSystem.Services.EmaAufgabeRegel;

namespace HrSystem.Tests;

public class EmaAufgabeTests
{
    private static DateOnly D(int j, int m, int t) => new(j, m, t);

    [Fact]
    public void Bvg_Eintritt_und_Austritt_haengen_am_Vorsorgeplan()
    {
        var spannen = new[] { new R.BvgSpanne(1, D(2026, 3, 1), D(2026, 8, 31)) };
        var codes = new[] { new R.BvgCode(1, D(2026, 3, 1), D(2026, 8, 31), "11", null, true) };
        var personen = new[] { new R.Person(1, D(2026, 3, 1), D(2026, 8, 31), null, null) };
        var a = R.BvgAufgaben(spannen, codes, Array.Empty<R.Abwesenheit>(), Array.Empty<R.Wechsel>(), Array.Empty<R.Wechsel>(), Array.Empty<R.Wechsel>(), personen);
        Assert.Contains(a, x => x.Grund == "entryCompany" && x.Stichtag == D(2026, 3, 1) && x.Zusatz == "100 % arbeitsfähig");
        Assert.Contains(a, x => x.Grund == "withdrawalPensionPlan" && x.Stichtag == D(2026, 8, 31));
    }

    [Fact]
    public void Bvg_Unterbruch_und_Lohn_und_Tod()
    {
        var spannen = new[]
        {
            new R.BvgSpanne(1, D(2026, 1, 1), D(2026, 2, 28)),
            new R.BvgSpanne(1, D(2026, 4, 1), null),
        };
        var krank = new[]
        {
            new R.Abwesenheit(1, D(2026, 3, 1), D(2026, 3, 31), "KRANK"),
            new R.Abwesenheit(1, D(2026, 6, 1), D(2026, 6, 10), "UNFALL"),
        };
        var lohn = new[]
        {
            new R.Wechsel(1, D(2026, 1, 1), "20"),
            new R.Wechsel(1, D(2026, 5, 1), "22"),
        };
        var a = R.BvgAufgaben(spannen, Array.Empty<R.BvgCode>(), krank, lohn, Array.Empty<R.Wechsel>(), Array.Empty<R.Wechsel>(),
            new[] { new R.Person(1, D(2026, 1, 1), null, null, null) });
        Assert.Contains(a, x => x.Grund == "interruptionOfEmployment" && x.Art == "EINTRITT" && x.Stichtag == D(2026, 4, 1));
        Assert.Contains(a, x => x.Grund == "interruptionOfEmployment" && x.Art == "AUSTRITT" && x.Text == "Beginn Arbeitsunterbruch" && x.Stichtag == D(2026, 2, 28));
        Assert.Contains(a, x => x.Grund == "salary" && x.Stichtag == D(2026, 5, 1) && x.Text == "Lohnänderung");
        Assert.Contains(a, x => x.Grund == "incapacity" && x.Stichtag == D(2026, 6, 1));

        var tod = R.BvgAufgaben(
            new[] { new R.BvgSpanne(2, D(2026, 1, 1), D(2026, 6, 30)) },
            Array.Empty<R.BvgCode>(), Array.Empty<R.Abwesenheit>(), Array.Empty<R.Wechsel>(), Array.Empty<R.Wechsel>(), Array.Empty<R.Wechsel>(),
            new[] { new R.Person(2, D(2026, 1, 1), D(2026, 6, 30), "TODESFALL", "CH") });
        Assert.Contains(tod, x => x.Grund == "deceased" && x.Text == "Verstorben");
    }

    [Fact]
    public void Qst_Mutation_gilt_am_Folgemonat_Eintritt_am_Eintrittsdatum()
    {
        Assert.Equal(D(2026, 4, 1), R.MutationsDatum(D(2026, 3, 15)));
        Assert.Equal(D(2026, 3, 1), R.MutationsDatum(D(2026, 3, 1)));

        var v = new[]
        {
            new R.QstVersion(1, D(2026, 1, 10), D(2026, 3, 31), "A0N", "BE", 0, false, "Bern", null, "B"),
            new R.QstVersion(1, D(2026, 4, 1), null, "A0Y", "BE", 0, true, "Bern", null, "B"),
        };
        var a = R.QstAufgaben(v, Array.Empty<R.Wechsel>(), new[] { new R.Person(1, D(2026, 1, 10), null, null, "DE") });
        Assert.Contains(a, x => x.Grund == "entryCompany" && x.Stichtag == D(2026, 1, 10));
        Assert.Contains(a, x => x.Grund == "churchTax" && x.Art == "MUTATION" && x.Stichtag == D(2026, 4, 1));
    }

    [Fact]
    public void Qst_Gruende_Zivilstand_Partner_Kinder_Kanton_und_Austritte()
    {
        var basis = new R.QstVersion(1, D(2026, 1, 1), null, "B0N", "BE", 0, false, "Bern", "BE", "B");
        var zivil = R.QstAufgaben(
            new[] { basis, basis with { ValidFrom = D(2026, 6, 1), ValidTo = null, Code = "A0N" } },
            new[] { new R.Wechsel(1, D(2026, 5, 20), "geschieden") },
            new[] { new R.Person(1, D(2026, 1, 1), null, null, "DE") });
        Assert.Contains(zivil, x => x.Grund == "civilstate" && x.Stichtag == D(2026, 6, 1));

        var partner = R.QstAufgaben(
            new[] { basis, basis with { ValidFrom = D(2026, 6, 15), ValidTo = null, Code = "C0N" } },
            Array.Empty<R.Wechsel>(),
            new[] { new R.Person(1, D(2026, 1, 1), null, null, "DE") });
        Assert.Contains(partner, x => x.Grund == "partnerWork" && x.Text == "Arbeit des Partners" && x.Stichtag == D(2026, 7, 1));

        var kind = R.QstAufgaben(
            new[] { basis, basis with { ValidFrom = D(2026, 3, 1), ValidTo = null, Code = "B1N", Kinder = 1 } },
            Array.Empty<R.Wechsel>(),
            new[] { new R.Person(1, D(2026, 1, 1), null, null, "DE") });
        Assert.Contains(kind, x => x.Grund == "childrenDeduction");

        var kanton = R.QstAufgaben(
            new[] { basis, basis with { ValidFrom = D(2026, 2, 1), ValidTo = null, Kanton = "LU" } },
            Array.Empty<R.Wechsel>(),
            new[] { new R.Person(1, D(2026, 1, 1), null, null, "DE") });
        Assert.Contains(kanton, x => x.Grund == "cantonChange" && x.Art == "MUTATION");

        var ex = R.QstAufgaben(
            new[] { basis, basis with { ValidFrom = D(2026, 4, 1), ValidTo = null, PartnerArbeitsort = "EX" } },
            Array.Empty<R.Wechsel>(),
            new[] { new R.Person(1, D(2026, 1, 1), null, null, "DE") });
        Assert.Contains(ex, x => x.Grund == "partnerIncome");

        var c = R.QstAufgaben(
            new[] { basis with { ValidTo = D(2026, 7, 31) }, basis with { ValidFrom = D(2026, 8, 1), ValidTo = null, Code = "NON", Bewilligung = "C" } },
            Array.Empty<R.Wechsel>(),
            new[] { new R.Person(1, D(2026, 1, 1), null, null, "DE") });
        Assert.Contains(c, x => x.Grund == "settled-C" && x.Text == "Niederlassung C");

        var firma = R.QstAufgaben(
            new[] { new R.QstVersion(2, D(2026, 1, 1), D(2026, 9, 30), "A0N", "BE", 0, false, "Bern", null, "B") },
            Array.Empty<R.Wechsel>(),
            new[] { new R.Person(2, D(2026, 1, 1), D(2026, 9, 30), null, "DE") });
        Assert.Contains(firma, x => x.Grund == "withdrawalCompany" && x.Stichtag == D(2026, 9, 30));
    }

    [Fact]
    public void Nachzahlung_im_Vorjahr_jaehrlich_ist_Leistungsanspruch_des_Austritts()
    {
        Assert.True(NachzahlungQstZeit.LeistungsanspruchMitAustritt(true, D(2025, 12, 31), 2026, true));
        Assert.False(NachzahlungQstZeit.LeistungsanspruchMitAustritt(true, D(2026, 3, 31), 2026, true));
        Assert.False(NachzahlungQstZeit.LeistungsanspruchMitAustritt(true, D(2025, 12, 31), 2026, false));
        Assert.False(NachzahlungQstZeit.LeistungsanspruchMitAustritt(false, D(2025, 12, 31), 2026, true));
    }
}
