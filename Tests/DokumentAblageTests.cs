using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.DokumentAblage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Ablage nach Angabe (Walter 25.09.2026): Zielliste pro Mitarbeiter,
/// Kategorie aus dem Feld-Code, Verknüpfen beim Hochladen.
/// </summary>
public class DokumentAblageTests
{
    private static AppDbContext NeueDb(string t)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("Ablage_" + t + "_" + Guid.NewGuid()).Options);

    private static (AppDbContext Db, Employee Ma) MitMa(string t, string nation = "XK")
    {
        var db = NeueDb(t);
        db.Nationalities.Add(new Nationality { Id = 1, Code = "CH" });
        db.Nationalities.Add(new Nationality { Id = 2, Code = nation == "CH" ? "DE" : nation });
        var ma = new Employee { Id = 10, FirstName = "Anna", LastName = "Muster", EmployeeNumber = "1220001",
                                NationalityId = nation == "CH" ? 1 : 2 };
        db.Employees.Add(ma);
        db.Employees.Add(new Employee { Id = 11, FirstName = "Fremd", LastName = "Person", EmployeeNumber = "1220002" });
        db.SaveChanges();
        return (db, ma);
    }

    [Fact]
    public async Task Optionen_zeigen_Historie_Einträge_und_lassen_Ferien_weg()
    {
        var (db, _) = MitMa(nameof(Optionen_zeigen_Historie_Einträge_und_lassen_Ferien_weg));
        var heute = DateOnly.FromDateTime(DateTime.Now);
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = 1, EmployeeId = 10, Iban = "CH93 0076 2011 6238 5295 7", ValidFrom = heute.AddYears(-1) });
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = 2, EmployeeId = 10, Iban = "CH00", ValidFrom = heute.AddYears(-3), ValidTo = heute.AddYears(-2) });
        db.Employments.Add(new Employment { Id = 5, EmployeeId = 10, EmploymentModel = "FLEX", ContractStartDate = new DateTime(2026, 1, 1) });
        db.Absences.Add(new Absence { Id = 7, EmployeeId = 10, AbsenceType = "KRANK", DateFrom = heute.AddDays(-3), DateTo = heute });
        db.Absences.Add(new Absence { Id = 8, EmployeeId = 10, AbsenceType = "FERIEN", DateFrom = heute.AddDays(-20), DateTo = heute.AddDays(-10) });
        db.EmployeeFamilyMembers.Add(new EmployeeFamilyMember { Id = 3, EmployeeId = 10, MemberType = "Kind", FirstName = "Lea" });
        db.PermitTypes.Add(new PermitType { Id = 1, Code = "B" });
        db.EmployeePermitHistories.Add(new EmployeePermitHistory { Id = 4, EmployeeId = 10, PermitTypeId = 1, ValidFrom = new DateOnly(2025, 1, 1), DokumentId = 99 });
        await db.SaveChangesAsync();

        var keys = (await new DokumentAblageService(db).OptionenAsync(10))!.Select(o => o.Key).ToList();

        Assert.Contains("bank:1", keys);
        Assert.DoesNotContain("bank:2", keys);          // abgelaufenes Konto
        Assert.Contains("vertrag:5", keys);
        Assert.Contains("absenz:7", keys);
        Assert.DoesNotContain("absenz:8", keys);        // Ferien brauchen kein Zeugnis
        Assert.Contains("ausweis_kind:3", keys);
        Assert.Contains("geburtsurkunde_kind:3", keys);
        Assert.Contains("bewilligung_neu", keys);
        Assert.Contains("bewilligung:4", keys);
        Assert.Equal(5, keys.Count(k => k.StartsWith("anderes:")));
    }

    [Fact]
    public async Task Schweizer_bekommen_keine_Bewilligung()
    {
        var (db, _) = MitMa(nameof(Schweizer_bekommen_keine_Bewilligung), nation: "CH");
        var keys = (await new DokumentAblageService(db).OptionenAsync(10))!.Select(o => o.Key).ToList();
        Assert.DoesNotContain(keys, k => k.StartsWith("bewilligung"));
    }

    [Fact]
    public async Task Verknüpfen_setzt_alle_gewählten_Felder()
    {
        var (db, _) = MitMa(nameof(Verknüpfen_setzt_alle_gewählten_Felder));
        db.Employments.Add(new Employment { Id = 5, EmployeeId = 10, ContractStartDate = new DateTime(2026, 1, 1) });
        db.Absences.Add(new Absence { Id = 7, EmployeeId = 10, AbsenceType = "KRANK" });
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = 1, EmployeeId = 10, Iban = "CH93" });
        db.EmployeePermitHistories.Add(new EmployeePermitHistory { Id = 4, EmployeeId = 10, PermitTypeId = 1 });
        db.EmployeeFamilyMembers.Add(new EmployeeFamilyMember { Id = 3, EmployeeId = 10, MemberType = "Kind" });
        await db.SaveChangesAsync();

        var (fehler, verknuepft) = await new DokumentAblageService(db).VerknuepfeAsync(10, 500, new[]
        {
            "ausweis", "ahv_karte", "vertrag:5", "absenz:7", "bank:1", "bewilligung:4", "geburtsurkunde_kind:3",
        });
        Assert.Null(fehler);
        await db.SaveChangesAsync();

        Assert.Equal(7, verknuepft.Count);
        var ma = await db.Employees.SingleAsync(e => e.Id == 10);
        Assert.Equal(500, ma.IdPassDokumentId);
        Assert.Equal(500, ma.AhvKarteDokumentId);
        Assert.Equal(500, (await db.Employments.SingleAsync()).VertragDokumentId);
        Assert.Equal(500, (await db.Absences.SingleAsync()).DokumentId);
        Assert.Equal(500, (await db.EmployeeBankAccounts.SingleAsync()).DokumentId);
        Assert.Equal(500, (await db.EmployeePermitHistories.SingleAsync()).DokumentId);
        var kind = await db.EmployeeFamilyMembers.SingleAsync();
        Assert.Equal(500, kind.GeburtsurkundeDokumentId);
        Assert.Null(kind.DokumentId);
    }

    [Fact]
    public async Task Kündigung_ist_Ablageziel_und_wird_verknüpft()
    {
        var (db, ma) = MitMa(nameof(Kündigung_ist_Ablageziel_und_wird_verknüpft));
        ma.KuendigungPer = new DateTime(2026, 10, 31);
        await db.SaveChangesAsync();
        var service = new DokumentAblageService(db);

        var opt = (await service.OptionenAsync(10))!.Single(o => o.Key == "kuendigung");
        Assert.Equal("per 31.10.2026", opt.Sub);
        Assert.False(opt.Historie);                  // ersetzt still

        var (fehler, _) = await service.VerknuepfeAsync(10, 600, new[] { "kuendigung" });
        Assert.Null(fehler);
        await db.SaveChangesAsync();
        Assert.Equal(600, (await db.Employees.SingleAsync(e => e.Id == 10)).KuendigungDokumentId);
    }

    [Fact]
    public async Task Fremder_Eintrag_wird_abgewiesen()
    {
        var (db, _) = MitMa(nameof(Fremder_Eintrag_wird_abgewiesen));
        db.Employments.Add(new Employment { Id = 6, EmployeeId = 11, ContractStartDate = new DateTime(2026, 1, 1) });
        await db.SaveChangesAsync();
        var (fehler, _) = await new DokumentAblageService(db).VerknuepfeAsync(10, 500, new[] { "vertrag:6" });
        Assert.NotNull(fehler);
        Assert.Null((await db.Employments.SingleAsync()).VertragDokumentId);
    }

    [Theory]
    [InlineData("bewilligung_neu")]
    [InlineData("foto")]
    [InlineData("anderes:lohn")]
    [InlineData("gibtsnicht")]
    public async Task Ziele_ohne_Feld_werden_beim_Verknüpfen_abgewiesen(string ziel)
    {
        var (db, _) = MitMa(nameof(Ziele_ohne_Feld_werden_beim_Verknüpfen_abgewiesen) + ziel);
        var (fehler, _) = await new DokumentAblageService(db).VerknuepfeAsync(10, 500, new[] { ziel });
        Assert.NotNull(fehler);
    }

    [Fact]
    public async Task Kategorie_kommt_aus_dem_Feld_Code_mit_Rückfall_auf_ID()
    {
        var db = NeueDb(nameof(Kategorie_kommt_aus_dem_Feld_Code_mit_Rückfall_auf_ID));
        db.DokumentKategorien.Add(new DokumentKategorie { Id = 1, Name = "Persönliche Angaben", SortOrder = 10 });
        db.DokumentKategorien.Add(new DokumentKategorie { Id = 2, Name = "Alt", SortOrder = 5, Aktiv = false });
        db.DokumentTypen.Add(new DokumentTyp { Id = 20, KategorieId = 1, Name = "Identitätskarte", LinkedFieldCode = "id_card" });
        db.DokumentTypen.Add(new DokumentTyp { Id = 21, KategorieId = 2, Name = "Pass (alt)", LinkedFieldCode = "passport" });
        db.DokumentTypen.Add(new DokumentTyp { Id = 22, KategorieId = 1, Name = "AHV", LinkedFieldCode = "ahv_card", Aktiv = false });
        await db.SaveChangesAsync();

        var typen = await new DokumentAblageService(db).TypenFuerCodesAsync();
        // «passport» liegt nur in einer inaktiven Kategorie → Ausweis fällt auf die ID-Karte zurück
        var ausweis = DokumentAblageService.TypFuerArt(DokumentAblageService.FindeArt("ausweis")!, typen);
        Assert.Equal(20, ausweis!.TypId);
        Assert.Equal("Persönliche Angaben", ausweis.KategorieName);
        // inaktiver Typ zählt nicht → Kategorie am Schluss selbst wählen
        Assert.Null(DokumentAblageService.TypFuerArt(DokumentAblageService.FindeArt("ahv_karte")!, typen));
    }

    [Fact]
    public void Ziel_Schlüssel_werden_streng_gelesen()
    {
        Assert.Equal("vertrag", DokumentAblageService.FindeArt("vertrag:12")!.Schluessel);
        Assert.Null(DokumentAblageService.FindeArt("vertrag:x"));
        Assert.True(DokumentAblageService.FindeArt("anderes:lohn")!.Anderes);
        Assert.Null(DokumentAblageService.FindeArt("anderes:5"));
        Assert.Equal(new List<string> { "ausweis", "vertrag:3" },
                     DokumentAblageService.ParseZiele(" ausweis ;vertrag:3;ausweis;"));
    }
}
