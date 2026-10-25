using HrSystem.Services.Personaldaten;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Personaldaten-Plausibilität (Walter 15.10.2026, Swissdec F06_01_1: «PlausiRules schon
/// im HR-System abfangen»). Alter unter 16 / über 70 nur mit Bestätigung, Telefon und
/// E-Mail gültig (Walters Beispiele «+41 79 409 43 33» und «walter.schaub@gmail.com»),
/// PLZ im Ausland sinnvoll, Datumsfolge, Pflichtfelder, Bewilligung, Namen, Familie.
/// </summary>
public class PersonaldatenPruefungTests
{
    private static readonly DateOnly Heute = new(2026, 10, 15);

    private static PdPerson Gut() => new()
    {
        Vorname = "Walter",
        Nachname = "Schaub",
        Geburt = new DateOnly(1970, 5, 3),
        Geschlecht = "male",
        NationalitaetCode = "CH",
        Ahv = "756.9217.0769.85",
        Telefon = "+41 79 409 43 33",
        Email = "walter.schaub@gmail.com",
        Land = "CH",
        Plz = "6210",
        Ort = "Sursee",
        Strasse = "Bahnhofstrasse 1",
        Eintritt = new DateOnly(2020, 1, 1),
    };

    private static List<string> Codes(PdPerson p, ISet<string>? chPlz = null)
        => PersonaldatenPruefung.Pruefe(p, Heute, null, chPlz).Select(b => b.Code).ToList();

    [Fact]
    public void Vollstaendige_Person_ohne_Befund()
        => Assert.Empty(Codes(Gut()));

    [Theory]
    [InlineData(2011, 1, 1, "ALTER_UNTER_16")]   // 15
    [InlineData(2010, 10, 16, "ALTER_UNTER_16")] // wird morgen 16
    [InlineData(1955, 10, 15, "ALTER_UEBER_70")] // heute 71
    [InlineData(1926, 1, 1, "ALTER_UNMOEGLICH")]
    public void Alter_ausserhalb_16_bis_70(int j, int m, int t, string code)
    {
        var p = Gut();
        p.Geburt = new DateOnly(j, m, t);
        p.Eintritt = null;
        var b = Assert.Single(PersonaldatenPruefung.Pruefe(p, Heute), x => x.Code.StartsWith("ALTER_"));
        Assert.Equal(code, b.Code);
        Assert.Equal(code == "ALTER_UNMOEGLICH" ? PdStufe.Fehler : PdStufe.Bestaetigen, b.Stufe);
    }

    [Theory]
    [InlineData(2010, 10, 15)] // genau 16
    [InlineData(1956, 10, 15)] // genau 70
    [InlineData(1955, 10, 16)] // wird morgen 71
    public void Alter_16_und_70_ohne_Rueckfrage(int j, int m, int t)
    {
        var p = Gut();
        p.Geburt = new DateOnly(j, m, t);
        p.Eintritt = null;
        Assert.DoesNotContain(Codes(p), c => c.StartsWith("ALTER_"));
    }

    [Fact]
    public void Alter_bei_Ausgetretenen_am_Austritt()
    {
        var p = Gut();
        p.Geburt = new DateOnly(1950, 1, 1);
        p.Austritt = new DateOnly(2019, 12, 31);   // damals 69
        p.Eintritt = new DateOnly(2010, 1, 1);
        Assert.DoesNotContain(Codes(p), c => c.StartsWith("ALTER_"));
    }

    [Theory]
    [InlineData("+41 79 409 43 33")]
    [InlineData("079 409 43 33")]
    [InlineData("0794094333")]
    [InlineData("0041 79 409 43 33")]
    [InlineData("41794094333")]
    [InlineData("+49 151 23456789")]
    [InlineData("+39 333 123 4567")]
    public void Telefon_gueltig(string tel)
    {
        var p = Gut();
        p.Telefon = tel;
        Assert.DoesNotContain("TELEFON_UNGUELTIG", Codes(p));
    }

    [Theory]
    [InlineData("079 409 43")]
    [InlineData("12345")]
    [InlineData("+41 12 345 67 89")]
    [InlineData("keine")]
    public void Telefon_ungueltig(string tel)
    {
        var p = Gut();
        p.Telefon = tel;
        Assert.Contains("TELEFON_UNGUELTIG", Codes(p));
    }

    [Fact]
    public void Telefon_wird_international_formatiert()
    {
        Assert.Equal("+41 79 409 43 33", PersonaldatenPruefung.TelefonFormatiert("0794094333"));
        Assert.Equal("+41 79 409 43 33", PersonaldatenPruefung.TelefonFormatiert("+41 79 409 43 33"));
        Assert.Null(PersonaldatenPruefung.TelefonFormatiert("079 409 43"));
        // Swissdec: national (079… / 041…), Speichern bleibt +41…
        Assert.Equal("079 409 43 33", PersonaldatenPruefung.TelefonFuerSwissdec("+41 79 409 43 33"));
        Assert.Equal("079 409 43 33", PersonaldatenPruefung.TelefonFuerSwissdec("0794094333"));
        Assert.Equal("041 218 65 32", PersonaldatenPruefung.TelefonFuerSwissdec("+41 41 218 65 32"));
        Assert.Null(PersonaldatenPruefung.TelefonFuerSwissdec("079 409 43"));
    }

    [Theory]
    [InlineData("walter.schaub@gmail.com", null)]
    [InlineData("hr@srgmbh.ch", null)]
    [InlineData("walter.schaub@gmial.com", "EMAIL_TIPPFEHLER")]
    [InlineData("walter.schaub@bluewn.ch", "EMAIL_TIPPFEHLER")]
    [InlineData("walter.schaub@gmail.con", "EMAIL_UNGUELTIG")]
    [InlineData("walter.schaub@gmail", "EMAIL_UNGUELTIG")]
    [InlineData("walter schaub@gmail.com", "EMAIL_UNGUELTIG")]
    [InlineData("walter..schaub@gmail.com", "EMAIL_UNGUELTIG")]
    public void Email(string mail, string? code)
    {
        var p = Gut();
        p.Email = mail;
        var c = Codes(p).Where(x => x.StartsWith("EMAIL_")).ToList();
        if (code == null) Assert.Empty(c);
        else Assert.Equal(code, Assert.Single(c));
    }

    [Theory]
    [InlineData("CH", "6210", null)]
    [InlineData("CH", "621", "PLZ_UNGUELTIG")]
    [InlineData("CH", "0621", "PLZ_UNGUELTIG")]
    [InlineData("DE", "79539", null)]
    [InlineData("DE", "7953", "PLZ_UNGUELTIG")]
    [InlineData("AT", "6800", null)]
    [InlineData("LI", "9490", null)]
    [InlineData("LI", "9400", "PLZ_UNGUELTIG")]
    [InlineData("PT", "4000-123", null)]
    [InlineData("XX", "1234", "LAND_UNGUELTIG")]
    public void Plz_nach_Land(string land, string plz, string? code)
    {
        var p = Gut();
        p.Land = land;
        p.Plz = plz;
        if (land != "CH") p.NationalitaetCode = land == "XX" ? "CH" : land;
        if (land is "DE" or "AT" or "LI" or "PT") p.BewilligungCode = "G";
        var c = Codes(p).Where(x => x.StartsWith("PLZ_") || x == "LAND_UNGUELTIG").ToList();
        if (code == null) Assert.Empty(c);
        else Assert.Equal(code, Assert.Single(c));
    }

    [Fact]
    public void Schweizer_Plz_muss_im_Verzeichnis_stehen()
    {
        var verzeichnis = new HashSet<string> { "6210", "8000" };
        Assert.DoesNotContain("PLZ_UNBEKANNT", Codes(Gut(), verzeichnis));
        var p = Gut();
        p.Plz = "6299";
        Assert.Contains("PLZ_UNBEKANNT", Codes(p, verzeichnis));
        Assert.DoesNotContain("PLZ_UNBEKANNT", Codes(p));   // ohne Verzeichnis nur Format
    }

    [Fact]
    public void Datumsfolge()
    {
        var p = Gut();
        p.Eintritt = new DateOnly(1980, 1, 1);   // 9 Jahre alt
        Assert.Contains("EINTRITT_ZU_FRUEH", Codes(p));
        p = Gut();
        p.Austritt = new DateOnly(2019, 12, 31);
        Assert.Contains("AUSTRITT_VOR_EINTRITT", Codes(p));
        p = Gut();
        p.Geburt = new DateOnly(2027, 1, 1);
        Assert.Contains("GEBURT_ZUKUNFT", Codes(p));
    }

    [Fact]
    public void Pflichtfelder_Geschlecht_Nationalitaet_Geburt()
    {
        var p = Gut();
        p.Geschlecht = null;
        p.NationalitaetCode = null;
        p.Geburt = null;
        var c = Codes(p);
        Assert.Contains("GESCHLECHT_FEHLT", c);
        Assert.Contains("NATIONALITAET_FEHLT", c);
        Assert.Contains("GEBURT_FEHLT", c);

        p = Gut();
        p.NationalitaetCode = null;
        p.NationalitaetUnbekannt = "Kosovarisch";
        Assert.Contains("NATIONALITAET_UNBEKANNT", Codes(p));
    }

    [Fact]
    public void Ahv_Pruefziffer()
    {
        var p = Gut();
        p.Ahv = "756.1234.5678.90";
        Assert.Contains("AHV_UNGUELTIG", Codes(p));
        p.Ahv = "756.1234.5678.97";
        Assert.DoesNotContain("AHV_UNGUELTIG", Codes(p));
        p.Ahv = "756.7865.5342.46";
        Assert.DoesNotContain("AHV_UNGUELTIG", Codes(p));
        p.Ahv = "756.7865.4342.46";
        Assert.Contains("AHV_UNGUELTIG", Codes(p));
        p.Ahv = "unbekannt";
        Assert.DoesNotContain("AHV_UNGUELTIG", Codes(p));
    }

    [Fact]
    public void Wohnsitz_und_Bewilligung()
    {
        var p = Gut();
        p.BewilligungCode = "B";
        Assert.Contains("BEWILLIGUNG_BEI_CH", Codes(p));

        p = Gut();
        p.NationalitaetCode = "DE";
        p.BewilligungCode = "G";
        Assert.Contains("GRENZGAENGER_WOHNT_CH", Codes(p));

        p = Gut();
        p.NationalitaetCode = "DE";
        p.Land = "DE";
        p.Plz = "79539";
        p.Ort = "Lörrach";
        p.BewilligungCode = "B";
        var b = Assert.Single(PersonaldatenPruefung.Pruefe(p, Heute), x => x.Code == "WOHNSITZ_AUSLAND");
        Assert.Equal(PdStufe.Bestaetigen, b.Stufe);
    }

    [Theory]
    [InlineData("Anna-Lena", null)]
    [InlineData("Zoë", null)]
    [InlineData("D'Angelo", null)]
    [InlineData("Walter2", "NAME_ZEICHEN")]
    [InlineData(" Walter", "NAME_LEERZEICHEN")]
    [InlineData("Walter  Hans", "NAME_LEERZEICHEN")]
    public void Namen(string vorname, string? code)
    {
        var p = Gut();
        p.Vorname = vorname;
        var c = Codes(p).Where(x => x.StartsWith("NAME_")).ToList();
        if (code == null) Assert.Empty(c);
        else Assert.Contains(code, c);
    }

    [Fact]
    public void Gleicher_Vor_und_Nachname_nur_Rueckfrage()
    {
        var p = Gut();
        p.Vorname = "Schaub";
        var b = Assert.Single(PersonaldatenPruefung.Pruefe(p, Heute), x => x.Code == "NAME_GLEICH");
        Assert.Equal(PdStufe.Bestaetigen, b.Stufe);
    }

    [Fact]
    public void NurNeue_haelt_bestehende_Maengel_nicht_an()
    {
        var alt = Gut();
        alt.Telefon = "079 409 43";               // war schon falsch
        var neu = Gut();
        neu.Telefon = "079 409 43";
        neu.Geburt = new DateOnly(2012, 1, 1);    // neu: jünger als 16
        neu.Eintritt = null;
        alt.Eintritt = null;
        var neue = PersonaldatenPruefung.NurNeue(
            PersonaldatenPruefung.Pruefe(neu, Heute), PersonaldatenPruefung.Pruefe(alt, Heute));
        Assert.Equal("ALTER_UNTER_16", Assert.Single(neue).Code);
    }

    [Fact]
    public void Bestaetigung_gilt_nur_fuer_den_Wert()
    {
        var p = Gut();
        p.Geburt = new DateOnly(2012, 1, 1);
        p.Eintritt = null;
        var befund = Assert.Single(PersonaldatenPruefung.Pruefe(p, Heute));
        var bestaetigt = new HashSet<string> { befund.Schluessel };
        Assert.Empty(PersonaldatenPruefung.OhneBestaetigte(PersonaldatenPruefung.Pruefe(p, Heute), bestaetigt));

        p.Geburt = new DateOnly(2012, 2, 1);      // anderer Wert → fragt neu
        Assert.Single(PersonaldatenPruefung.OhneBestaetigte(PersonaldatenPruefung.Pruefe(p, Heute), bestaetigt));
    }

    [Fact]
    public void Fehler_lassen_sich_nicht_bestaetigen()
    {
        var p = Gut();
        p.Telefon = "12345";
        var b = Assert.Single(PersonaldatenPruefung.Pruefe(p, Heute));
        Assert.Single(PersonaldatenPruefung.OhneBestaetigte(new[] { b }, new HashSet<string> { b.Schluessel }));
        Assert.DoesNotContain(b.Code, PersonaldatenPruefung.BestaetigbareCodes);
    }

    [Fact]
    public void Duplikate_AHV_und_Person()
    {
        var d = new PersonaldatenDuplikate();
        d.Hinzufuegen(1, "1220001", "Walter", "Schaub", new DateOnly(1970, 5, 3), "756.9217.0769.85");
        Assert.Empty(d.Pruefe(1, "756.9217.0769.85", "Walter", "Schaub", new DateOnly(1970, 5, 3)));
        var c = d.Pruefe(null, "7569217076985", "walter", "SCHAUB", new DateOnly(1970, 5, 3)).Select(b => b.Code).ToList();
        Assert.Contains("AHV_DOPPELT", c);
        Assert.Contains("PERSON_DOPPELT", c);
        d.Hinzufuegen(2, "580108", "Gazele", "Jemmo", new DateOnly(1999, 1, 1), null);
        Assert.Contains("PERSON_DOPPELT", d.Pruefe(3, null, "Gazele", "Jemmo", new DateOnly(1999, 1, 1)).Select(b => b.Code));
        Assert.Contains("PERSON_DOPPELT", d.Pruefe(3, null, "Gazele", "Jem-mo", new DateOnly(1999, 1, 1)).Select(b => b.Code));
        Assert.Empty(d.Pruefe(2, null, "Gazele", "Jemmo", new DateOnly(1999, 1, 1)));
        Assert.True(PersonaldatenDuplikate.IstArchiv("991220001"));
        Assert.True(PersonaldatenDuplikate.IstArchiv("1220001alt"));
    }

    [Fact]
    public void Familie()
    {
        var maGeb = new DateOnly(1990, 1, 1);
        var c = PersonaldatenPruefung.PruefeFamilie(
            new PdFamilie { Art = "Kind", Vorname = "Lia", Geburt = new DateOnly(2027, 1, 1), Ahv = "756.1234.5678.90" },
            maGeb, Heute).Select(b => b.Code).ToList();
        Assert.Contains("FAM_AHV_UNGUELTIG", c);
        Assert.Contains("FAM_GEBURT_ZUKUNFT", c);

        var k = Assert.Single(PersonaldatenPruefung.PruefeFamilie(
            new PdFamilie { Art = "Kind", Vorname = "Mia", Geburt = new DateOnly(1995, 1, 1) }, maGeb, Heute));
        Assert.Equal("KIND_ZU_ALT", k.Code);
        Assert.Equal(PdStufe.Bestaetigen, k.Stufe);

        Assert.Empty(PersonaldatenPruefung.PruefeFamilie(
            new PdFamilie { Art = "Kind", Vorname = "Noa", Geburt = new DateOnly(2018, 3, 1), Ahv = "756.9217.0769.85" },
            maGeb, Heute));
    }

    [Theory]
    [InlineData("CH9300762011623852957", true)]
    [InlineData("CH93 0076 2011 6238 5295 7", true)]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("CH9300762011623852958", false)]
    [InlineData("CH93", false)]
    public void Iban(string iban, bool gueltig)
        => Assert.Equal(gueltig, PersonaldatenPruefung.IbanGueltig(iban));
}

/// <summary>To-do «personaldaten_pruefen» für den Bestand: ein Eintrag pro MA, bestätigte Rückfragen fallen weg.</summary>
public class PersonaldatenTodoTests
{
    private static HrSystem.Data.AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<HrSystem.Data.AppDbContext>()
            .UseInMemoryDatabase("Personaldaten_" + t + "_" + Guid.NewGuid()).Options);

    private static HrSystem.Models.Employee Ma(int id, bool phantom = false) => new()
    {
        Id = id,
        FirstName = "Walter", LastName = id == 1 ? "Muster" : "Beispiel",
        EmployeeNumber = "122000" + id,
        IsActive = true,
        IsPayrollExcluded = phantom,
        Gender = "male",
        NationalityId = 1,
        DateOfBirth = new DateTime(1990, 1, 1),
        SocialSecurityNumber = id == 1 ? "756.9217.0769.85" : null,
        PhoneMobile = "+41 79 409 43 33",
        Email = "walter.schaub@gmail.com",
        Country = "CH", ZipCode = "6210", City = "Sursee", Street = "Bahnhofstrasse 1",
        EntryDate = new DateTime(2020, 1, 1),
    };

    private static async Task<List<HrSystem.Services.DashboardService.DashboardAlert>> AlertsAsync(HrSystem.Data.AppDbContext db)
    {
        var svc = new HrSystem.Services.DashboardService(db, new HrSystem.Services.QstPflichtCheckService(db),
            new HrSystem.Services.SperrfristService(db));
        return (await svc.BuildAsync(null)).Alerts.Where(a => a.Category == "personaldaten_pruefen").ToList();
    }

    private static async Task SeedAsync(HrSystem.Data.AppDbContext db, params HrSystem.Models.Employee[] ma)
    {
        db.Nationalities.Add(new HrSystem.Models.Nationality { Id = 1, Code = "CH" });
        db.Employees.AddRange(ma);
        foreach (var e in ma)
            db.Employments.Add(new HrSystem.Models.Employment
            {
                Id = e.Id, EmployeeId = e.Id, CompanyProfileId = 58,
                ContractStartDate = new DateTime(2020, 1, 1), EmploymentModel = "FLEX", IsActive = true,
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Gueltige_Daten_kein_ToDo()
    {
        using var db = NewDb();
        await SeedAsync(db, Ma(1));
        Assert.Empty(await AlertsAsync(db));
    }

    [Fact]
    public async Task Ungueltiges_Telefon_meldet_einmal_pro_MA()
    {
        using var db = NewDb();
        var ma = Ma(1);
        ma.PhoneMobile = "079 409 43";
        ma.Email = "walter@gmail.con";
        await SeedAsync(db, ma);
        var a = Assert.Single(await AlertsAsync(db));
        Assert.Contains("Natel", a.Subtitle);
        Assert.Contains("E-Mail", a.Subtitle);
    }

    [Fact]
    public async Task MA_ohne_Lohn_meldet_nicht()
    {
        using var db = NewDb();
        var ma = Ma(1, phantom: true);
        ma.PhoneMobile = "12345";
        await SeedAsync(db, ma);
        Assert.Empty(await AlertsAsync(db));
    }

    [Fact]
    public async Task Bestaetigte_Rueckfrage_verschwindet()
    {
        using var db = NewDb();
        var ma = Ma(1);
        ma.DateOfBirth = new DateTime(2012, 1, 1);
        ma.EntryDate = new DateTime(2026, 1, 1);
        await SeedAsync(db, ma);
        Assert.Single(await AlertsAsync(db));

        await new PersonaldatenAuswertung(db).BestaetigenAsync(1, null, "ALTER_UNTER_16", "2012-01-01", "Walter", default);
        Assert.Empty(await AlertsAsync(db));
    }

    [Fact]
    public async Task Doppelte_AHV_meldet_beide()
    {
        using var db = NewDb();
        var b = Ma(2);
        b.SocialSecurityNumber = "756.9217.0769.85";
        await SeedAsync(db, Ma(1), b);
        Assert.Equal(2, (await AlertsAsync(db)).Count);
    }
}
