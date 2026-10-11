using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Anmeldung Familienzulagen → «In OneCrew speichern» (Walter 11.10.2026):
/// nur leere OneCrew-Felder füllen, nie überschreiben; ungültige Werte ⇒ nichts gespeichert.
/// </summary>
public class FamzRueckschreibenTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FamzRueck_" + t + "_" + Guid.NewGuid()).Options);

    private static async Task<(Employee Ma, EmployeeFamilyMember Saud, EmployeeFamilyMember Alen)> Anita(AppDbContext db)
    {
        var ma = new Employee { EmployeeNumber = "750009", FirstName = "Anita", LastName = "Djonlagic", IsActive = true, MaritalStatus = "verheiratet" };
        db.Employees.Add(ma);
        await db.SaveChangesAsync();
        var saud = new EmployeeFamilyMember { EmployeeId = ma.Id, MemberType = "Ehepartner", FirstName = "Saud", LastName = "Djonlagic", Erwerbstaetig = true };
        var alen = new EmployeeFamilyMember { EmployeeId = ma.Id, MemberType = "Kind", FirstName = "Alen", LastName = "Djonlagic", Gender = "Männlich" };
        db.EmployeeFamilyMembers.AddRange(saud, alen);
        await db.SaveChangesAsync();
        return (ma, saud, alen);
    }

    private static FamzAnmeldungDto Formular(int saudId, int alenId) => new()
    {
        Antragsteller = new FamzPersonDto { ZivilstandSeit = "14.06.2008" },
        AndererElternteil = new FamzElternteilDto
        {
            FamilyMemberId = saudId, AhvNummer = "7561234567897", EinkommenHoeher = true,
            EinkommenUeberMinimum = true, Arbeitspensum = "100",
        },
        Kinder = new() { new FamzKindDto { FamilyMemberId = alenId, Beziehung = "l", Geschlecht = "F" } },
    };

    [Fact]
    public async Task Leere_Felder_werden_nachgetragen()
    {
        using var db = NewDb();
        var (ma, saud, alen) = await Anita(db);
        var r = await new FamzRueckschreiben(db).SpeichernAsync(ma.Id, Formular(saud.Id, alen.Id), new[]
        {
            "antragsteller.zivilstandSeit", "andererElternteil.ahvNummer", "andererElternteil.einkommenHoeher",
            "andererElternteil.einkommenUeberMinimum", "andererElternteil.arbeitspensum", "kinder.0.beziehung",
        });

        Assert.Empty(r.Fehler);
        Assert.Equal(6, r.Gespeichert.Count);
        Assert.Equal(new DateOnly(2008, 6, 14), ma.MaritalStatusSince);
        Assert.Equal("756.1234.5678.97", saud.SocialSecurityNumber);
        Assert.False(saud.MaHatHoeheresEinkommen);     // Saud verdient mehr ⇒ MA nicht
        Assert.True(saud.EinkommenUeberMinimum);
        Assert.Equal(100m, saud.Arbeitspensum);
        Assert.Equal("L", alen.BeziehungZumMa);
    }

    [Fact]
    public async Task Vorhandene_Werte_werden_nie_ueberschrieben()
    {
        using var db = NewDb();
        var (ma, saud, alen) = await Anita(db);
        saud.SocialSecurityNumber = "756.9999.9999.99";
        await db.SaveChangesAsync();

        var r = await new FamzRueckschreiben(db).SpeichernAsync(ma.Id, Formular(saud.Id, alen.Id),
            new[] { "andererElternteil.ahvNummer", "kinder.0.geschlecht" });

        Assert.Empty(r.Gespeichert);
        Assert.Equal("756.9999.9999.99", saud.SocialSecurityNumber);
        Assert.Equal("Männlich", alen.Gender);
    }

    [Fact]
    public async Task Ungueltige_AHV_speichert_gar_nichts()
    {
        var name = "FamzRueck_Ungueltig_" + Guid.NewGuid();
        DbContextOptions<AppDbContext> opt = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
        int maId, saudId;
        using (var db = new AppDbContext(opt))
        {
            var (ma, saud, alen) = await Anita(db);
            maId = ma.Id; saudId = saud.Id;
            var dto = Formular(saud.Id, alen.Id);
            dto.AndererElternteil!.AhvNummer = "756.1234.5678.90";

            var r = await new FamzRueckschreiben(db).SpeichernAsync(ma.Id, dto,
                new[] { "andererElternteil.ahvNummer", "andererElternteil.einkommenHoeher" });

            Assert.Single(r.Fehler);
            Assert.Empty(r.Gespeichert);
        }
        using var frisch = new AppDbContext(opt);
        var s = await frisch.EmployeeFamilyMembers.SingleAsync(m => m.Id == saudId);
        Assert.Null(s.MaHatHoeheresEinkommen);
        Assert.Null(s.SocialSecurityNumber);
    }

    [Fact]
    public async Task Beide_Eltern_verknuepft_Kind_ohne_Elternteil_und_fuellt_Sorge()
    {
        using var db = NewDb();
        var (ma, saud, alen) = await Anita(db);
        var arman = new EmployeeFamilyMember { EmployeeId = ma.Id, MemberType = "Kind", FirstName = "Arman", AndererElternteilId = saud.Id, ElterlicheSorge = "ELTERN" };
        db.EmployeeFamilyMembers.Add(arman);
        await db.SaveChangesAsync();
        var dto = Formular(saud.Id, alen.Id);
        dto.Kinder!.Add(new FamzKindDto { FamilyMemberId = arman.Id });
        dto.Sorge = "eltern";
        dto.LebenBei = "ELTERN";

        var r = await new FamzRueckschreiben(db).SpeichernAsync(ma.Id, dto, new[] { "sorge", "lebenBei" });

        Assert.Empty(r.Fehler);
        Assert.Equal(new[] { "Alen: elterliche Sorge", "Alen: anderer Elternteil Saud" }, r.Gespeichert);
        Assert.Equal("ELTERN", alen.ElterlicheSorge);
        Assert.Equal(saud.Id, alen.AndererElternteilId);
        Assert.False(alen.GemeinsamesKindMitPartner);
    }

    [Fact]
    public async Task Beide_Eltern_ohne_Elternteil_im_Haushalt_verknuepft_nichts()
    {
        using var db = NewDb();
        var (ma, saud, alen) = await Anita(db);
        saud.LebtImHaushalt = false;
        await db.SaveChangesAsync();
        var dto = Formular(saud.Id, alen.Id);
        dto.LebenBei = "ELTERN";

        var r = await new FamzRueckschreiben(db).SpeichernAsync(ma.Id, dto, new[] { "lebenBei" });

        Assert.Empty(r.Gespeichert);
        Assert.Null(alen.AndererElternteilId);
    }

    [Fact]
    public async Task Nicht_speicherbare_und_fremde_Felder_werden_ignoriert()
    {
        using var db = NewDb();
        var (ma, saud, alen) = await Anita(db);
        var fremd = new EmployeeFamilyMember { EmployeeId = ma.Id + 1, MemberType = "Ehepartner", FirstName = "Fremd" };
        db.EmployeeFamilyMembers.Add(fremd);
        await db.SaveChangesAsync();
        var dto = Formular(fremd.Id, alen.Id);
        dto.AndererElternteil!.NameVorname = "Neu Name";

        var r = await new FamzRueckschreiben(db).SpeichernAsync(ma.Id, dto,
            new[] { "andererElternteil.nameVorname", "andererElternteil.ahvNummer", "agName" });

        Assert.Empty(r.Gespeichert);
        Assert.Null(fremd.SocialSecurityNumber);
        Assert.False(FamzRueckschreiben.Speicherbar("andererElternteil.nameVorname"));
        Assert.True(FamzRueckschreiben.Speicherbar("kinder.2.beziehung"));
    }
}
