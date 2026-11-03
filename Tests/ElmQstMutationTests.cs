using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Gründe der QST-Mutation aus dem Tarifcode (RefXML Juni/Juli 2025).</summary>
public class ElmQstMutationTests
{
    [Theory]
    [InlineData("A0N", "B0N", "civilstate")]                  // TF23 Koller Heirat
    [InlineData("A0N", "C0N", "partnerWork,civilstate")]      // TF22 Bucher Heirat + Partner erwerbstätig
    [InlineData("B0Y", "C0Y", "partnerWork")]                 // TF36 Maldini August
    [InlineData("B0Y", "B1Y", "childrenDeduction")]           // TF33 Châtelain Kind
    [InlineData("A0N", "A0Y", "churchTax")]
    [InlineData("A0N", "A0N", "")]
    public void Gruende(string alt, string neu, string erwartet)
        => Assert.Equal(erwartet, string.Join(",", ElmMonthlyDeclarationBuilder.MutationsGruende(alt, neu)));
}

/// <summary>Reihenfolge der CorrectionMonth-Totale wie die Swissdec-Referenz (Java-HashMap).</summary>
public class ElmKorrekturReihenfolgeTests
{
    [Theory]
    [InlineData("2025-04,2025-05", "2025-05,2025-04")]   // RefXML Juni 2025
    [InlineData("2025-05,2025-06", "2025-05,2025-06")]   // RefXML Juli 2025
    public void Wie_Referenz(string ein, string aus)
        => Assert.Equal(aus, string.Join(",", ElmMonthlyDeclarationBuilder.JavaHashReihenfolge(ein.Split(','))));
}
