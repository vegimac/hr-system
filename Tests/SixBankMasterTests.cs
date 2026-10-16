using HrSystem.Services.Six;
using Xunit;

namespace HrSystem.Tests;

public class SixBankMasterTests
{
    [Fact]
    public void MapEntries_takes_BankMaster_skips_Concatenated()
    {
        var entries = new[]
        {
            new SixBankMasterEntry
            {
                EntryType = "BankMaster",
                Iid = 9000,
                BankOrInstitutionName = "PostFinance AG",
                Bic = "POFICHBEXXX",
                StreetName = "Mingerstrasse",
                BuildingNumber = "20",
                PostCode = "3030",
                TownName = "Bern"
            },
            new SixBankMasterEntry
            {
                EntryType = "BankMasterConcatenated",
                Iid = 9999,
                NewIid = 9000,
                BankOrInstitutionName = "Alt"
            },
            new SixBankMasterEntry
            {
                EntryType = "BankMaster",
                Iid = 100,
                BankOrInstitutionName = "Schweizerische Nationalbank",
                Bic = "SNBZCHZZXXX",
                TownName = "Zürich",
                PostCode = "8022"
            }
        };

        var rows = SixBankMasterSyncService.MapEntries(entries);
        Assert.Equal(2, rows.Count);
        var pf = Assert.Single(rows, r => r.Iid == "9000");
        Assert.Equal("PostFinance AG", pf.Name);
        Assert.Equal("POFICHBEXXX", pf.Bic);
        Assert.Equal("Mingerstrasse 20", pf.Strasse);
        Assert.Equal("3030", pf.Plz);
        Assert.Equal("Bern", pf.Ort);
        Assert.Contains(rows, r => r.Iid == "100");
    }
}
