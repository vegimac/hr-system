using HrSystem.Services;
using Xunit;
using static HrSystem.Services.SchulungStatus;

namespace HrSystem.Tests;

public class SchulungStatusTests
{
    private static readonly DateOnly Heute = new(2026, 10, 1);
    private static readonly DateTime Erfasst = new(2026, 10, 1);

    private static Typ Hygiene => new(1, "HYGIENE", "Lebensmittelhygiene", null, 1, "ALLE", true, 60, Erfasst);
    private static Typ Sriw => new(2, "SRIW", "SRIW", null, 14, "ALLE", true, 60, Erfasst);
    private static Typ Sicherheit => new(3, "SICHERHEIT", "Erstunterweisung Sicherheit", 24, 1, "ALLE", true, 60, Erfasst);
    private static Typ Gastro => new(4, "GASTRO", "Gastro-Ausbildung", null, null, "LGAV", false, 60, Erfasst);
    private static Typ Peak => new(5, "PEAK", "Peak-Verifizierung", 12, null, "FIXM", false, 60, Erfasst);
    private static Typ Seco => new(6, "SECO", "SECO", 12, null, "GF", false, 60, Erfasst);

    private static Kontext Crew(DateOnly? eintritt = null, string? edu = "Ia")
        => new("FLEX", "CREW", edu, eintritt ?? new DateOnly(2020, 1, 1));

    [Fact]
    public void Gastro_nur_mit_Ausbildung_Ib_bis_IV()
    {
        Assert.False(Betrifft("LGAV", Crew(edu: "Ia")));
        Assert.True(Betrifft("LGAV", Crew(edu: "Ib")));
        Assert.True(Betrifft("LGAV", Crew(edu: "IIIa")));
        Assert.True(Betrifft("LGAV", Crew(edu: "IV")));
        Assert.False(Betrifft("LGAV", Crew(edu: null)));
    }

    [Fact]
    public void Gf_sieht_FixM_und_Gf_Schulungen()
    {
        var gf = new Kontext("FIX-M", "REST_MANAGER", "Ia", new DateOnly(2020, 1, 1));
        var asst = new Kontext("FIX-M", "ASST_1", "Ia", new DateOnly(2020, 1, 1));
        Assert.True(Betrifft("FIXM", gf));
        Assert.True(Betrifft("GF", gf));
        Assert.True(Betrifft("FIXM", asst));
        Assert.False(Betrifft("GF", asst));
        Assert.False(Betrifft("FIXM", Crew()));
    }

    [Fact]
    public void Ohne_Auffrischung_gilt_unbegrenzt()
    {
        var e = new Eintrag(1, Hygiene.Id, new DateOnly(2015, 3, 1), "FRED", null);
        var r = Berechne(Hygiene, new[] { e }, Crew(new DateOnly(2015, 3, 1)), Heute);
        Assert.Equal(Zustand.Gueltig, r.Zustand);
        Assert.Null(r.GueltigBis);
        Assert.False(r.Melden);
    }

    [Fact]
    public void Auffrischung_laeuft_bald_ab_und_abgelaufen()
    {
        var bald = new Eintrag(1, Sicherheit.Id, new DateOnly(2024, 11, 1), "FRED", null);
        var r = Berechne(Sicherheit, new[] { bald }, Crew(new DateOnly(2024, 11, 1)), Heute);
        Assert.Equal(Zustand.LaeuftAb, r.Zustand);
        Assert.Equal(new DateOnly(2026, 11, 1), r.GueltigBis);
        Assert.True(r.Melden);

        var alt = new Eintrag(2, Sicherheit.Id, new DateOnly(2024, 9, 1), "FRED", null);
        var r2 = Berechne(Sicherheit, new[] { alt }, Crew(new DateOnly(2024, 9, 1)), Heute);
        Assert.Equal(Zustand.Abgelaufen, r2.Zustand);
        Assert.True(r2.Melden);
    }

    [Fact]
    public void Juengster_Eintrag_zaehlt()
    {
        var alt = new Eintrag(1, Sicherheit.Id, new DateOnly(2022, 1, 1), "FRED", null);
        var neu = new Eintrag(2, Sicherheit.Id, new DateOnly(2026, 6, 1), "DOKUMENT", 9);
        var r = Berechne(Sicherheit, new[] { alt, neu }, Crew(new DateOnly(2021, 12, 1)), Heute);
        Assert.Equal(Zustand.Gueltig, r.Zustand);
        Assert.Equal(2, r.Letzter!.Id);
    }

    [Fact]
    public void Frist_ab_Eintritt_offen_dann_fehlt()
    {
        var eintritt = new DateOnly(2026, 9, 25);
        var r = Berechne(Sriw, Array.Empty<Eintrag>(), Crew(eintritt), Heute);
        Assert.Equal(Zustand.OffenInFrist, r.Zustand);
        Assert.Equal(new DateOnly(2026, 10, 8), r.FaelligAm);

        var spaeter = Berechne(Sriw, Array.Empty<Eintrag>(), Crew(eintritt), new DateOnly(2026, 10, 9));
        Assert.Equal(Zustand.Fehlt, spaeter.Zustand);
    }

    [Fact]
    public void Altbestand_wird_nicht_gemahnt()
    {
        var r = Berechne(Hygiene, Array.Empty<Eintrag>(), Crew(new DateOnly(2019, 5, 1)), Heute);
        Assert.Equal(Zustand.Fehlt, r.Zustand);
        Assert.False(r.Melden);
    }

    [Fact]
    public void Neuer_Eintritt_nach_Erfassung_wird_gemahnt()
    {
        var typ = Hygiene with { CreatedAt = new DateTime(2026, 9, 1) };
        var r = Berechne(typ, Array.Empty<Eintrag>(), Crew(new DateOnly(2026, 9, 20)), Heute);
        Assert.Equal(Zustand.Fehlt, r.Zustand);
        Assert.True(r.Melden);
    }

    [Fact]
    public void Wiedereintritt_macht_Frist_Schulung_wieder_offen()
    {
        var vorher = new Eintrag(1, Hygiene.Id, new DateOnly(2025, 3, 1), "FRED", null);
        var r = Berechne(Hygiene, new[] { vorher }, Crew(Heute), Heute);
        Assert.Equal(Zustand.OffenInFrist, r.Zustand);
        Assert.Null(r.Letzter);

        var spaeter = Berechne(Hygiene, new[] { vorher }, Crew(new DateOnly(2026, 9, 28)), Heute);
        Assert.Equal(Zustand.Fehlt, spaeter.Zustand);
    }

    [Fact]
    public void Onboarding_kurz_vor_Vertragsbeginn_zaehlt()
    {
        var onboarding = new Eintrag(1, Hygiene.Id, new DateOnly(2026, 9, 20), "FRED", null);
        var r = Berechne(Hygiene, new[] { onboarding }, Crew(new DateOnly(2026, 10, 1)), Heute);
        Assert.Equal(Zustand.Gueltig, r.Zustand);
    }

    [Fact]
    public void Wiedereintritt_setzt_Zertifikat_ohne_Frist_nicht_zurueck()
    {
        var ausweis = new Eintrag(1, Gastro.Id, new DateOnly(2018, 6, 30), "DOKUMENT", 4);
        var r = Berechne(Gastro, new[] { ausweis }, Crew(new DateOnly(2026, 9, 1), "IIIa"), Heute);
        Assert.Equal(Zustand.Gueltig, r.Zustand);
    }

    [Fact]
    public void Gastro_fehlt_ohne_ToDo()
    {
        var r = Berechne(Gastro, Array.Empty<Eintrag>(), Crew(edu: "II"), Heute);
        Assert.Equal(Zustand.Offen, r.Zustand);
        Assert.False(r.Melden);
    }

    [Fact]
    public void Peak_fehlt_wird_sofort_gemahnt()
    {
        var mgr = new Kontext("FIX-M", "ASST_1", "Ia", new DateOnly(2018, 1, 1));
        var r = Berechne(Peak, Array.Empty<Eintrag>(), mgr, Heute);
        Assert.Equal(Zustand.Fehlt, r.Zustand);
        Assert.True(r.Melden);
        Assert.Equal(Zustand.NichtBetroffen, Berechne(Seco, Array.Empty<Eintrag>(), mgr, Heute).Zustand);
    }

    [Fact]
    public void Warnen_ab_leer_erzeugt_keine_ToDos()
    {
        var typ = Peak with { WarnenAbTage = null };
        var mgr = new Kontext("FIX-M", "ASST_1", "Ia", new DateOnly(2018, 1, 1));
        Assert.False(Berechne(typ, Array.Empty<Eintrag>(), mgr, Heute).Melden);
        var alt = new Eintrag(1, typ.Id, new DateOnly(2020, 1, 1), "DOKUMENT", 1);
        var r = Berechne(typ, new[] { alt }, mgr, Heute);
        Assert.Equal(Zustand.Abgelaufen, r.Zustand);
        Assert.False(r.Melden);
    }
}
