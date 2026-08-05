using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Spec-Audit (Walter-Bug 06.08.2026): user_branch_access wird von ZWEI UIs
/// beschrieben — Benutzerverwaltung (Filial-Zugänge) UND Filiale→Unterzeichner
/// (Role/FunctionTitle/IsDefault auf DENSELBEN Zeilen). Das frühere
/// «RemoveRange(alle) + nackt neu anlegen» in UsersController.Update löschte
/// bei jedem Benutzer-Speichern die Unterzeichner-Attribute aller Filialen
/// (Anita verlor «Geschäftsführerin» + «Allgemeiner Unterzeichner»).
/// Pflicht: DIFF — bestehende Zeilen unangetastet lassen.
/// </summary>
public class UserBranchAccessPreserveSpecTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
                dir = Directory.GetParent(dir)?.FullName;
            return dir ?? throw new InvalidOperationException("Repo-Root nicht gefunden");
        }
    }

    [Fact]
    public void Benutzer_Update_darf_BranchAccess_nicht_wholesale_neu_anlegen()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "Controllers/UsersController.cs"));

        // Das Wholesale-Muster darf nicht zurückkommen …
        Assert.False(
            Regex.IsMatch(src, @"RemoveRange\(user\.BranchAccess\)"),
            "UsersController.Update darf user.BranchAccess nicht komplett löschen — " +
            "Unterzeichner-Attribute (Role/FunctionTitle/IsDefault) gehen sonst bei jedem Speichern verloren. DIFF verwenden.");

        // … und der Diff-Ansatz muss vorhanden sein (wegfallende entfernen, neue ergänzen).
        Assert.Contains("wegfallend", src);
        Assert.Contains("vorhandene", src);
    }
}
