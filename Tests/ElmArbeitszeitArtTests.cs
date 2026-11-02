using HrSystem.Models;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Monatsmeldung: Stunden / Lektionen / beides nach dem Arbeitszeitmodell der Person
/// (XSD TotalHoursAndLessonsOfWork). Quality Tool Jan 2025 TF01 Herz: Modell «Standard»
/// nur Stunden → TotalHoursOfWork, obwohl der Vertrag einen Lektionenansatz hat.
/// </summary>
public class ElmArbeitszeitArtTests
{
    private static Arbeitszeitmodell M(decimal? std, decimal? lek) => new() { Id = 1, Wochenstunden = std, Wochenlektionen = lek };

    [Fact]
    public void NurStunden_Herz()
        => Assert.Equal(ElmMonthlyDeclarationBuilder.ArbeitszeitArt.Stunden,
                        ElmMonthlyDeclarationBuilder.ArtDesModells(M(42m, null), null));

    [Fact]
    public void NurLektionen_Degelo()
        => Assert.Equal(ElmMonthlyDeclarationBuilder.ArbeitszeitArt.Lektionen,
                        ElmMonthlyDeclarationBuilder.ArtDesModells(M(null, 21m), null));

    [Fact]
    public void StundenUndLektionen_Paganini()
        => Assert.Equal(ElmMonthlyDeclarationBuilder.ArbeitszeitArt.StundenUndLektionen,
                        ElmMonthlyDeclarationBuilder.ArtDesModells(M(20m, 10m), null));

    [Fact]
    public void AbweichendeLektionenDerPerson_GehenVor()
        => Assert.Equal(ElmMonthlyDeclarationBuilder.ArbeitszeitArt.StundenUndLektionen,
                        ElmMonthlyDeclarationBuilder.ArtDesModells(M(42m, null),
                            new EmployeeArbeitszeitmodell { AbweichendeWochenlektionen = 20m }));

    [Fact]
    public void OhneModell_Null()
        => Assert.Null(ElmMonthlyDeclarationBuilder.ArtDesModells(null, null));

    [Fact]
    public void LektionenBeiNurStunden_StopptMitHinweis()
        => Assert.Contains("nur Stunden", ElmMonthlyDeclarationBuilder.ZeitPasstNichtZumModell(
            ElmMonthlyDeclarationBuilder.ArbeitszeitArt.Stunden, 120m, 8m));

    [Fact]
    public void StundenBeiNurLektionen_StopptMitHinweis()
        => Assert.Contains("nur Lektionen", ElmMonthlyDeclarationBuilder.ZeitPasstNichtZumModell(
            ElmMonthlyDeclarationBuilder.ArbeitszeitArt.Lektionen, 5m, 40m));

    [Theory]
    [InlineData(ElmMonthlyDeclarationBuilder.ArbeitszeitArt.Stunden, 160, 0)]                 // Herz
    [InlineData(ElmMonthlyDeclarationBuilder.ArbeitszeitArt.Lektionen, 0, 42)]                // Degelo
    [InlineData(ElmMonthlyDeclarationBuilder.ArbeitszeitArt.StundenUndLektionen, 100, 40)]    // Paganini
    [InlineData(ElmMonthlyDeclarationBuilder.ArbeitszeitArt.StundenUndLektionen, 150, 0)]     // Paganini Feb
    public void PassendeZeit_KeinHinweis(ElmMonthlyDeclarationBuilder.ArbeitszeitArt art, decimal std, decimal lek)
        => Assert.Null(ElmMonthlyDeclarationBuilder.ZeitPasstNichtZumModell(art, std, lek));
}
