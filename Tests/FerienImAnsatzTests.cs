using HrSystem.Models;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Vertragsmerkmal «Ferien/Feiertag im Ansatz inbegriffen» (Walter 02.11.2026, TF15 Degelo):
/// Standard nein; Vertragskopien übernehmen es.
/// </summary>
public class FerienImAnsatzTests
{
    [Fact]
    public void Standard_Nein()
        => Assert.False(new Employment().FerienFeiertagImAnsatz);
}
