using HrSystem.Services;
using Xunit;
using R = HrSystem.Services.GastroSocialMeldungRegel;

namespace HrSystem.Tests;

/// <summary>
/// An-/Abmeldung GastroSocial merken (Walter 13.10.2026): «Angemeldet müsste grundsätzlich jeder
/// Neueintritt sein und abgemeldet alle Austritte.» Jede Filiale = eigenes Konto.
/// </summary>
public class GastroSocialMeldungTests
{
    private static DateTime D(int j, int m, int t) => new(j, m, t);
    private static DateOnly O(int j, int m, int t) => new(j, m, t);

    [Fact]
    public void NahtloseVertragsanpassung_ist_keine_neue_Anmeldung()
    {
        var v = new[]
        {
            new R.Vertrag(1, 10, D(2026, 1, 1), D(2026, 3, 31)),
            new R.Vertrag(1, 10, D(2026, 4, 1), null),
        };
        var an = R.Anmeldungen(v);
        Assert.Single(an);
        Assert.Equal(O(2026, 1, 1), an[0].Datum);
    }

    [Fact]
    public void Wiedereintritt_nach_Luecke_und_Filialwechsel_sind_je_eine_Anmeldung()
    {
        var v = new[]
        {
            new R.Vertrag(1, 10, D(2026, 1, 1), D(2026, 2, 28)),
            new R.Vertrag(1, 10, D(2026, 6, 1), D(2026, 8, 31)),   // Wiedereintritt Filiale 10
            new R.Vertrag(1, 20, D(2026, 9, 1), null),            // Übertritt → Filiale 20
        };
        var an = R.Anmeldungen(v).OrderBy(e => e.Datum).ToList();
        Assert.Equal(3, an.Count);
        Assert.Equal((10, O(2026, 6, 1)), (an[1].CompanyProfileId, an[1].Datum));
        Assert.Equal((20, O(2026, 9, 1)), (an[2].CompanyProfileId, an[2].Datum));
    }

    [Fact]
    public void Abmeldung_gehoert_zur_Filiale_des_letzten_Vertrags()
    {
        var v = new[]
        {
            new R.Vertrag(1, 10, D(2025, 1, 1), D(2026, 4, 30)),
            new R.Vertrag(1, 20, D(2026, 5, 1), D(2026, 9, 30)),
        };
        var ab = R.AbmeldungVon(1, D(2026, 9, 30), v);
        Assert.NotNull(ab);
        Assert.Equal(20, ab!.CompanyProfileId);
        Assert.Null(R.AbmeldungVon(1, null, v));
    }

    [Fact]
    public void Meldung_zaehlt_mit_kleiner_Datumskorrektur_aber_nicht_fuer_andere_Filiale()
    {
        var e = new R.Ereignis(1, 10, R.Anmeldung, O(2026, 3, 1));
        Assert.NotNull(R.Passende(e, new[] { new R.Meldung(1, 10, R.Anmeldung, O(2026, 3, 15)) }));
        Assert.Null(R.Passende(e, new[] { new R.Meldung(1, 10, R.Anmeldung, O(2026, 5, 1)) }));
        Assert.Null(R.Passende(e, new[] { new R.Meldung(1, 20, R.Anmeldung, O(2026, 3, 1)) }));
        Assert.Null(R.Passende(e, new[] { new R.Meldung(1, 10, R.Abmeldung, O(2026, 3, 1)) }));
    }

    [Fact]
    public void ToDo_erst_ab_Datum_und_nur_Altbestand_ab_2026()
    {
        var heute = O(2026, 10, 13);
        var ereignisse = new[]
        {
            new R.Ereignis(1, 10, R.Anmeldung, O(2025, 6, 1)),   // Altbestand → still
            new R.Ereignis(2, 10, R.Anmeldung, O(2026, 10, 1)),  // fehlt
            new R.Ereignis(3, 10, R.Anmeldung, O(2026, 11, 1)),  // Eintritt kommt erst
            new R.Ereignis(4, 10, R.Abmeldung, O(2026, 9, 30)),  // gemeldet
        };
        var meldungen = new[] { new R.Meldung(4, 10, R.Abmeldung, O(2026, 9, 30)) };
        var offen = R.Offen(ereignisse, meldungen, heute);
        Assert.Single(offen);
        Assert.Equal(2, offen[0].EmployeeId);
    }

    [Fact]
    public void Abmeldung_erst_wenn_der_Austritt_vorbei_ist()
    {
        var ab = new R.Ereignis(1, 10, R.Abmeldung, O(2026, 10, 31));
        var keine = Array.Empty<R.Meldung>();
        Assert.False(R.Faellig(ab, O(2026, 10, 20), 1));
        Assert.False(R.Faellig(ab, O(2026, 10, 31), 1));
        Assert.True(R.Faellig(ab, O(2026, 11, 1), 1));
        Assert.Empty(R.Offen(new[] { ab }, keine, O(2026, 10, 31), R.AbmeldungNachTagen(null)));
        Assert.Single(R.Offen(new[] { ab }, keine, O(2026, 11, 1), R.AbmeldungNachTagen(-1)));
    }

    [Fact]
    public void Abmeldung_minus_drei_Tage_warnt_ab_dem_dritten_Tag_danach()
    {
        var ab = new R.Ereignis(1, 10, R.Abmeldung, O(2026, 10, 31));
        var keine = Array.Empty<R.Meldung>();
        Assert.Equal(3, R.AbmeldungNachTagen(-3));
        Assert.False(R.Faellig(ab, O(2026, 11, 2), 3));
        Assert.True(R.Faellig(ab, O(2026, 11, 3), 3));
        Assert.Empty(R.Offen(new[] { ab }, keine, O(2026, 11, 2), 3));
        Assert.Equal(O(2026, 10, 31), R.Offen(new[] { ab }, keine, O(2026, 11, 3), 3)[0].Datum);
    }

    [Fact]
    public void Liste_Beschaeftigt_bestaetigt_Anmeldungen_bis_heute()
    {
        var heute = O(2026, 10, 13);
        var ev = new List<R.Ereignis>
        {
            new(1, 10, R.Anmeldung, O(2024, 1, 1)),
            new(1, 10, R.Anmeldung, O(2026, 6, 1)),
        };
        var b = R.BestaetigtDurchListe(true, null, ev, heute);
        Assert.Equal(2, b.Count);

        // Eintritt erst nächsten Monat, aber schon gemeldet → die erste Anmeldung zählt.
        var kuenftig = new List<R.Ereignis> { new(2, 10, R.Anmeldung, O(2026, 11, 1)) };
        Assert.Single(R.BestaetigtDurchListe(true, null, kuenftig, heute));
    }

    [Fact]
    public void Liste_Ausgetreten_bestaetigt_Abmeldung_aber_nicht_den_Wiedereintritt()
    {
        var heute = O(2026, 10, 13);
        var ev = new List<R.Ereignis>
        {
            new(1, 10, R.Anmeldung, O(2025, 1, 1)),
            new(1, 10, R.Abmeldung, O(2026, 3, 31)),
            new(1, 10, R.Anmeldung, O(2026, 8, 1)),   // Wiedereintritt — bei GastroSocial noch «ausgetreten»
        };
        var b = R.BestaetigtDurchListe(false, D(2026, 3, 31), ev, heute);
        Assert.Contains(b, e => e.Art == R.Abmeldung);
        Assert.Contains(b, e => e.Datum == O(2025, 1, 1));
        Assert.DoesNotContain(b, e => e.Datum == O(2026, 8, 1));
    }
}
