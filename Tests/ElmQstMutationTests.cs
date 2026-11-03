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
