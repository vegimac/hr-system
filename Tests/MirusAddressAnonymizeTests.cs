using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

public class MirusAddressAnonymizeTests
{
    [Theory]
    [InlineData("Teresa Maria", "Aiello", "T. M. A.")]
    [InlineData("Zehra", "Akbas", "Z. A.")]
    [InlineData("Jean-Pierre", "Müller", "J. P. M.")]
    [InlineData("", "Test", "T.")]
    [InlineData(null, null, "—")]
    public void AnonymizePerson_Initialen(string? first, string? last, string expected)
        => Assert.Equal(expected, MirusAddressCompareController.AnonymizePerson(first, last));
}
