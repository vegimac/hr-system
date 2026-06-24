using System.Net.Http;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

public class EasyAtWorkEmployeePreviewTests
{
    private sealed class FakeEawClient : EasyAtWorkClient
    {
        public List<EawEmployee> Employees { get; } = new();

        public FakeEawClient() : base(
            new HttpClient(),
            new EasyAtWorkSettings { BaseUrl = "x", ClientId = "x", ClientSecret = "x" },
            NullLogger<EasyAtWorkClient>.Instance) { }

        public override Task<List<EawEmployee>> GetAllEmployeesIncludingInactiveAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult(Employees);
    }

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("EawPreview_" + testName + "_" + Guid.NewGuid()).Options);

    [Fact]
    public async Task MissingOptionalBackfillData_DoesNotCreatePreviewUpdate()
    {
        using var db = NewDb();
        db.CompanyProfiles.Add(new CompanyProfile { Id = 230, BranchName = "Langenthal", CompanyName = "Langenthal" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping
        {
            CompanyProfileId = 230,
            EasyAtWorkCustomerId = 1936
        });
        db.Employees.Add(new Employee
        {
            Id = 1,
            EmployeeNumber = "2300004",
            EasyAtWorkEmployeeId = 28396,
            FirstName = "Amire",
            LastName = "Mehmeti",
            DateOfBirth = new DateTime(1990, 11, 24),
            Email = "amiremehmeti@bluewin.ch",
            EntryDate = new DateTime(2024, 11, 7),
            IsActive = true
            // bewusst keine AHV, kein Zivilstand, kein Bankkonto, keine JobGroup
        });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee
        {
            Id = 31171,
            UserId = 28396,
            Number = "2300004",
            FirstName = "Amire",
            LastName = "Mehmeti",
            BirthDate = new DateOnly(1990, 11, 24),
            Email = "amiremehmeti@bluewin.ch",
            From = new DateOnly(2024, 11, 7)
        });

        var svc = new EasyAtWorkEmployeeSyncService(
            db, client, NullLogger<EasyAtWorkEmployeeSyncService>.Instance);

        var res = await svc.PreviewAsync(new EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = 230,
            OnlyActive = false,
            SkipDetailCalls = false
        });

        Assert.Equal(0, res.CountUpdate);
        Assert.Equal(1, res.CountUnchanged);
        var row = Assert.Single(res.Rows);
        Assert.Equal("UNCHANGED", row.Status);
    }
}
