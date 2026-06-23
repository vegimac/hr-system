using System.Net.Http;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Regression für den echten Commit-Pfad des easy@work-MA-Syncs:
/// Eine leere Checkbox-Auswahl im Frontend kommt als selectedNumbers=[] an und
/// muss trotzdem ALLE gematchten MA verarbeiten. Sonst bleiben UNCHANGED-MA wie
/// Amire ohne Timeline-Sync, alte UTP-Verträge werden nicht korrigiert und
/// easyatwork_contract_id/pay_rate_id bleiben NULL.
/// </summary>
public class EasyAtWorkEmployeeCommitHistoryTests
{
    private sealed class FakeEawClient : EasyAtWorkClient
    {
        public List<EawEmployee> Employees { get; } = new();
        public Dictionary<int, List<EawContract>> ContractsByEmployee { get; } = new();
        public Dictionary<int, List<EawPayRate>> PayRatesByEmployee { get; } = new();
        public Dictionary<int, List<EawPosition>> PositionsByEmployee { get; } = new();

        public FakeEawClient() : base(
            new HttpClient(),
            new EasyAtWorkSettings { BaseUrl = "x", ClientId = "x", ClientSecret = "x" },
            NullLogger<EasyAtWorkClient>.Instance) { }

        public override Task<List<EawEmployee>> GetAllEmployeesIncludingInactiveAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult(Employees);

        public override Task<EawPaginated<EawContract>> GetContractsAsync(int customerId, int employeeId, CancellationToken ct = default)
            => Task.FromResult(new EawPaginated<EawContract>
            {
                Data = ContractsByEmployee.TryGetValue(employeeId, out var rows) ? rows : new()
            });

        public override Task<EawPaginated<EawPayRate>> GetPayRatesAsync(int customerId, int employeeId, CancellationToken ct = default)
            => Task.FromResult(new EawPaginated<EawPayRate>
            {
                Data = PayRatesByEmployee.TryGetValue(employeeId, out var rows) ? rows : new()
            });

        public override Task<EawPaginated<EawPosition>> GetPositionsAsync(int customerId, int employeeId, CancellationToken ct = default)
            => Task.FromResult(new EawPaginated<EawPosition>
            {
                Data = PositionsByEmployee.TryGetValue(employeeId, out var rows) ? rows : new()
            });
    }

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("EawCommitHistory_" + testName + "_" + Guid.NewGuid()).Options);

    [Fact]
    public async Task EmptySelectedNumbers_ProcessesUnchangedEmployeeTimeline()
    {
        using var db = NewDb();
        db.CompanyProfiles.Add(new CompanyProfile { Id = 230, BranchName = "Test", CompanyName = "Test" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping
        {
            CompanyProfileId = 230,
            EasyAtWorkCustomerId = 1936
        });
        db.JobGroups.Add(new JobGroup { Id = 7, Code = "SHIFT_LEADER_7_PLUS", IsKader = true });
        var emp = new Employee
        {
            Id = 3040,
            EmployeeNumber = "2300004",
            EasyAtWorkEmployeeId = 28396,
            FirstName = "Amire",
            LastName = "Mehmeti",
            IsActive = true
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        db.Employments.AddRange(
            new Employment
            {
                EmployeeId = emp.Id,
                CompanyProfileId = 230,
                ContractStartDate = new DateTime(2021, 8, 10),
                ContractEndDate = new DateTime(2024, 12, 8),
                IsActive = false,
                EmploymentModel = "UTP",
                SalaryType = "hourly"
            },
            new Employment
            {
                EmployeeId = emp.Id,
                CompanyProfileId = 230,
                ContractStartDate = new DateTime(2026, 1, 1),
                ContractEndDate = null,
                IsActive = true,
                EmploymentModel = "FIX-M",
                SalaryType = "monthly",
                EmploymentPercentage = 60m,
                MonthlySalary = 2760m,
                MonthlySalaryFte = 4600m
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
            From = new DateOnly(2024, 11, 7)
        });
        client.ContractsByEmployee[31171] = new()
        {
            new EawContract
            {
                Id = 45583, AmountType = "percent", Amount = 60m, Percentage = 60m,
                FromRaw = "2025-12-31 23:00:00", ToRaw = null, UpdatedAtRaw = "2026-04-20 08:58:48"
            },
            new EawContract
            {
                Id = 30712, AmountType = "percent", Amount = 80m, Percentage = 80m,
                FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59", UpdatedAtRaw = "2024-11-07 09:33:59"
            }
        };
        client.PayRatesByEmployee[31171] = new()
        {
            new EawPayRate
            {
                Id = 72192, Type = "month", Rate = 2760m,
                FromRaw = "2025-12-31 23:00:00", ToRaw = null, UpdatedAtRaw = "2026-04-20 09:01:34"
            },
            new EawPayRate
            {
                Id = 43326, Type = "month", Rate = 3436m,
                FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59", UpdatedAtRaw = "2024-11-07 07:45:31"
            }
        };
        client.PositionsByEmployee[31171] = new()
        {
            new EawPosition { Id = 4379, Name = "SHIFT_LEADER_7_PLUS" }
        };

        var svc = new EasyAtWorkEmployeeSyncService(
            db, client, NullLogger<EasyAtWorkEmployeeSyncService>.Instance);

        var res = await svc.CommitAsync(new EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = 230,
            OnlyActive = false,
            SkipDetailCalls = true,
            SelectedNumbers = new List<string>() // Frontend: keine Checkboxen ausgewählt
        });

        Assert.Equal(1, res.CountUpdated);
        var rows = await db.Employments
            .Where(e => e.EmployeeId == emp.Id)
            .OrderBy(e => e.ContractStartDate)
            .ToListAsync();

        Assert.Contains(rows, r => r.EasyAtWorkContractId == 30712 && r.EasyAtWorkPayRateId == 43326);
        Assert.Contains(rows, r => r.EasyAtWorkContractId == 45583 && r.EasyAtWorkPayRateId == 72192);

        var oldWrong = rows.Single(r => r.ContractStartDate == new DateTime(2021, 8, 10));
        Assert.True(oldWrong.ContractEndDate <= new DateTime(2024, 11, 6));
        Assert.False(oldWrong.IsActive);

        var oldImported = rows.Single(r => r.EasyAtWorkContractId == 30712);
        Assert.Equal(new DateTime(2024, 11, 7), oldImported.ContractStartDate);
        Assert.Equal(new DateTime(2024, 12, 31), oldImported.ContractEndDate);
        Assert.Equal("FIX-M", oldImported.EmploymentModel);
        Assert.Equal(80m, oldImported.EmploymentPercentage);
        Assert.Equal(3436m, oldImported.MonthlySalary);
        Assert.Equal(4295m, oldImported.MonthlySalaryFte);

        var current = rows.Single(r => r.EasyAtWorkContractId == 45583);
        Assert.Equal(new DateTime(2026, 1, 1), current.ContractStartDate);
        Assert.Null(current.ContractEndDate);
        Assert.Equal(60m, current.EmploymentPercentage);
        Assert.Equal(2760m, current.MonthlySalary);
        Assert.Equal(4600m, current.MonthlySalaryFte);
    }

    [Fact]
    public async Task NonEmptySelectedNumbers_StillProcessesUnchangedEmployeeTimeline()
    {
        using var db = NewDb();
        db.CompanyProfiles.Add(new CompanyProfile { Id = 230, BranchName = "Test", CompanyName = "Test" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping
        {
            CompanyProfileId = 230,
            EasyAtWorkCustomerId = 1936
        });
        db.JobGroups.Add(new JobGroup { Id = 7, Code = "SHIFT_LEADER_7_PLUS", IsKader = true });

        var amire = new Employee
        {
            Id = 3040,
            EmployeeNumber = "2300004",
            EasyAtWorkEmployeeId = 28396,
            FirstName = "Amire",
            LastName = "Mehmeti",
            IsActive = true
        };
        var selectedUpdate = new Employee
        {
            Id = 4000,
            EmployeeNumber = "2309999",
            EasyAtWorkEmployeeId = 99901,
            FirstName = "Selected",
            LastName = "Update",
            IsActive = true
        };
        db.Employees.AddRange(amire, selectedUpdate);
        await db.SaveChangesAsync();

        db.Employments.AddRange(
            new Employment
            {
                EmployeeId = amire.Id,
                CompanyProfileId = 230,
                ContractStartDate = new DateTime(2021, 8, 10),
                ContractEndDate = new DateTime(2024, 12, 8),
                IsActive = false,
                EmploymentModel = "UTP",
                SalaryType = "hourly"
            },
            new Employment
            {
                EmployeeId = amire.Id,
                CompanyProfileId = 230,
                ContractStartDate = new DateTime(2026, 1, 1),
                ContractEndDate = null,
                IsActive = true,
                EmploymentModel = "FIX-M",
                SalaryType = "monthly",
                EmploymentPercentage = 60m,
                MonthlySalary = 2760m,
                MonthlySalaryFte = 4600m
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
            From = new DateOnly(2024, 11, 7)
        });
        client.Employees.Add(new EawEmployee
        {
            Id = 99901,
            UserId = 99901,
            Number = "2309999",
            FirstName = "Selected",
            LastName = "Changed",
            From = new DateOnly(2025, 1, 1)
        });
        client.ContractsByEmployee[31171] = new()
        {
            new EawContract
            {
                Id = 45583, AmountType = "percent", Amount = 60m, Percentage = 60m,
                FromRaw = "2025-12-31 23:00:00", ToRaw = null, UpdatedAtRaw = "2026-04-20 08:58:48"
            },
            new EawContract
            {
                Id = 30712, AmountType = "percent", Amount = 80m, Percentage = 80m,
                FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59", UpdatedAtRaw = "2024-11-07 09:33:59"
            }
        };
        client.PayRatesByEmployee[31171] = new()
        {
            new EawPayRate
            {
                Id = 72192, Type = "month", Rate = 2760m,
                FromRaw = "2025-12-31 23:00:00", ToRaw = null, UpdatedAtRaw = "2026-04-20 09:01:34"
            },
            new EawPayRate
            {
                Id = 43326, Type = "month", Rate = 3436m,
                FromRaw = "2024-11-06 23:00:00", ToRaw = "2024-12-31 22:59:59", UpdatedAtRaw = "2024-11-07 07:45:31"
            }
        };
        client.PositionsByEmployee[31171] = new()
        {
            new EawPosition { Id = 4379, Name = "SHIFT_LEADER_7_PLUS" }
        };

        var svc = new EasyAtWorkEmployeeSyncService(
            db, client, NullLogger<EasyAtWorkEmployeeSyncService>.Instance);

        var res = await svc.CommitAsync(new EasyAtWorkEmployeeSyncService.SyncRequest
        {
            CompanyProfileId = 230,
            OnlyActive = false,
            SkipDetailCalls = true,
            SelectedNumbers = new List<string> { "2309999" } // andere UPDATE-Zeile ausgewählt
        });

        Assert.Equal(1, res.CountUpdated);
        var rows = await db.Employments
            .Where(e => e.EmployeeId == amire.Id)
            .OrderBy(e => e.ContractStartDate)
            .ToListAsync();

        Assert.Contains(rows, r => r.EasyAtWorkContractId == 30712 && r.EasyAtWorkPayRateId == 43326);
        Assert.Contains(rows, r => r.EasyAtWorkContractId == 45583 && r.EasyAtWorkPayRateId == 72192);
    }
}
