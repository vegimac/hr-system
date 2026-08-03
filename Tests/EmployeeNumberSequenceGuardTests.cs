using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class EmployeeNumberSequenceGuardTests
{
    [Fact]
    public void Single_Must_Be_MaxPlusOne()
    {
        var ok = EmployeeNumberSequenceGuard.TryValidate(
            new[] { "750105" }, 750104, out var msg, out var exp, out _);
        Assert.True(ok, msg);
        Assert.Equal(new long[] { 750105 }, exp);
    }

    [Fact]
    public void Single_Wrong_Blocks()
    {
        var ok = EmployeeNumberSequenceGuard.TryValidate(
            new[] { "750107" }, 750104, out var msg, out var exp, out _);
        Assert.False(ok);
        Assert.Equal(new long[] { 750105 }, exp);
        Assert.Contains("750105", msg);
        Assert.Contains("750107", msg);
    }

    [Fact]
    public void Batch_Must_Be_Exact_Consecutive_Block()
    {
        var ok = EmployeeNumberSequenceGuard.TryValidate(
            new[] { "750107", "750105", "750106" }, 750104, out var msg, out var exp, out _);
        Assert.True(ok, msg);
        Assert.Equal(new long[] { 750105, 750106, 750107 }, exp);
    }

    [Fact]
    public void Batch_Gap_Or_Jump_Blocks_Entire_Set()
    {
        var ok = EmployeeNumberSequenceGuard.TryValidate(
            new[] { "750105", "750106", "759934" }, 750104, out var msg, out var exp, out _);
        Assert.False(ok);
        Assert.Equal(new long[] { 750105, 750106, 750107 }, exp);
        Assert.Contains("759934", msg);
    }

    [Fact]
    public void Batch_Starting_Too_High_Blocks()
    {
        var ok = EmployeeNumberSequenceGuard.TryValidate(
            new[] { "750106", "750107" }, 750104, out _, out var exp, out _);
        Assert.False(ok);
        Assert.Equal(new long[] { 750105, 750106 }, exp);
    }

    [Fact]
    public void No_Max_Only_Consecutive_Among_Themselves()
    {
        var ok = EmployeeNumberSequenceGuard.TryValidate(
            new[] { "580002", "580003" }, null, out var msg, out _, out _);
        Assert.True(ok, msg);

        ok = EmployeeNumberSequenceGuard.TryValidate(
            new[] { "580002", "580004" }, null, out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Empty_New_List_Passes()
    {
        Assert.True(EmployeeNumberSequenceGuard.TryValidate(
            Array.Empty<string>(), 750104, out _, out _, out _));
    }

    [Fact]
    public void FindMaxExisting_Ignores_Alt_And_Wrong_Prefix()
    {
        var max = EmployeeNumberSequenceGuard.FindMaxExisting(
            new[] { "750104", "750103alt", "580999", "9999001", "750099" }, "75");
        Assert.Equal(750104, max);
    }
}
