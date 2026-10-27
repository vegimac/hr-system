using HrSystem.Models;
using Xunit;

namespace HrSystem.Tests;

public class VertragsartKatalogTests
{
    [Fact]
    public void Leer_heisst_die_vier_Modelle()
    {
        var codes = VertragsartKatalog.Aufgeloest(null);
        Assert.Equal(new[] { "FLEX", "MTP", "FIX", "FIX-M" }, codes);
        Assert.False(VertragsartKatalog.Erlaubt(null, "apprentice"));
        Assert.True(VertragsartKatalog.Erlaubt(null, "FIX-M"));
    }

    [Fact]
    public void Gespeichert_ersetzt_den_Standard()
    {
        var codes = VertragsartKatalog.Aufgeloest("apprentice,indefiniteSalaryMth");
        Assert.Equal(new[] { "indefiniteSalaryMth", "apprentice" }, codes);
        Assert.False(VertragsartKatalog.Erlaubt("apprentice", "FLEX"));
    }

    [Fact]
    public void Swissdec_Art_gewinnt_vor_dem_Modell()
    {
        Assert.Equal("apprentice", VertragsartKatalog.CodeVon("FIX", "apprentice"));
        Assert.Equal("FLEX", VertragsartKatalog.CodeVon("UTP", null));
        Assert.Equal("administrativeBoard", VertragsartKatalog.CodeVon("FIX", "administrativeBoard"));
    }

    [Fact]
    public void Speichern_behaelt_nur_bekannte_Codes_in_Katalogreihenfolge()
    {
        var (text, unbekannt) = VertragsartKatalog.Speichern(new[] { "FIX-M", "unbekannt", "FLEX" });
        Assert.Equal("FLEX,FIX-M", text);
        Assert.Equal(new[] { "unbekannt" }, unbekannt);
    }
}
