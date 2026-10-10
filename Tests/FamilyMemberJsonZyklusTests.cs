using System;
using System.Text.Json;
using HrSystem.Models;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter-Bug 10.10.2026 (Ehemann von Dila Tetaj, C-Ausweis): PUT family/{id} gab das
/// Mitglied mit geladener Bewilligungs-Historie zurück — Historie → FamilyMember → Historie …
/// endlos, HTTP 500 «Fehler beim Speichern», obwohl die Daten gespeichert waren.
/// </summary>
public class FamilyMemberJsonZyklusTests
{
    [Fact]
    public void Mitglied_MitHistorie_LaesstSichSerialisieren()
    {
        var m = new EmployeeFamilyMember { Id = 1, MemberType = "Ehepartner" };
        m.PermitHistories.Add(new FamilyMemberPermitHistory
            { Id = 1, FamilyMemberId = 1, FamilyMember = m, ValidFrom = new DateOnly(2025, 3, 1) });
        m.ErwerbHistories.Add(new FamilyMemberErwerbHistory
            { Id = 1, FamilyMemberId = 1, FamilyMember = m, ValidFrom = new DateOnly(2025, 1, 1) });

        var json = JsonSerializer.Serialize(m);

        Assert.Contains("PermitHistories", json);
        Assert.DoesNotContain("\"FamilyMember\"", json);
    }
}
