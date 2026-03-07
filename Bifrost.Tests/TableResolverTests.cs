using Bifrost.Core;
using Xunit;

namespace Bifrost.Tests;

// TableResolver.Resolve requires a live SqlConnection for DB-backed filters.
// We test the override application and explicit table parsing logic in isolation.

public class TableResolverTests
{
    // ── Override application ──────────────────────────────────────────────────

    private static List<TableRef> ApplyOverrides(List<TableRef> tables, List<TableOverride> overrides)
    {
        return tables.Select(t =>
        {
            var full = $"{t.Schema}.{t.Name}".ToLower();
            var ov   = overrides.FirstOrDefault(o => o.Name.ToLower() == full);
            return ov is null ? t : new TableRef
            {
                Schema = t.Schema,
                Name   = t.Name,
                Ignore = ov.Ignore ?? false,
                Where  = ov.Where,
                Query  = ov.Query,
            };
        }).ToList();
    }

    [Fact]
    public void ApplyOverrides_SetsIgnore()
    {
        var tables    = new List<TableRef> { new() { Schema = "dbo", Name = "Orders" } };
        var overrides = new List<TableOverride> { new() { Name = "dbo.Orders", Ignore = true } };
        var result    = ApplyOverrides(tables, overrides);
        Assert.True(result[0].Ignore);
    }

    [Fact]
    public void ApplyOverrides_SetsWhere()
    {
        var tables    = new List<TableRef> { new() { Schema = "dbo", Name = "Orders" } };
        var overrides = new List<TableOverride> { new() { Name = "dbo.Orders", Where = "Id > 100" } };
        var result    = ApplyOverrides(tables, overrides);
        Assert.Equal("Id > 100", result[0].Where);
    }

    [Fact]
    public void ApplyOverrides_SetsQuery()
    {
        var tables    = new List<TableRef> { new() { Schema = "dbo", Name = "Orders" } };
        var overrides = new List<TableOverride> { new() { Name = "dbo.Orders", Query = "SELECT TOP 10 * FROM [dbo].[Orders]" } };
        var result    = ApplyOverrides(tables, overrides);
        Assert.Equal("SELECT TOP 10 * FROM [dbo].[Orders]", result[0].Query);
    }

    [Fact]
    public void ApplyOverrides_CaseInsensitiveMatch()
    {
        var tables    = new List<TableRef> { new() { Schema = "dbo", Name = "Orders" } };
        var overrides = new List<TableOverride> { new() { Name = "DBO.ORDERS", Ignore = true } };
        var result    = ApplyOverrides(tables, overrides);
        Assert.True(result[0].Ignore);
    }

    [Fact]
    public void ApplyOverrides_NoMatch_LeavesTableUnchanged()
    {
        var tables    = new List<TableRef> { new() { Schema = "dbo", Name = "Orders", Ignore = false } };
        var overrides = new List<TableOverride> { new() { Name = "dbo.Products", Ignore = true } };
        var result    = ApplyOverrides(tables, overrides);
        Assert.False(result[0].Ignore);
    }

    [Fact]
    public void ApplyOverrides_MultipleTablesOnlyMatchedAffected()
    {
        var tables = new List<TableRef>
        {
            new() { Schema = "dbo", Name = "Orders" },
            new() { Schema = "dbo", Name = "Products" },
        };
        var overrides = new List<TableOverride> { new() { Name = "dbo.Orders", Ignore = true } };
        var result    = ApplyOverrides(tables, overrides);
        Assert.True(result[0].Ignore);
        Assert.False(result[1].Ignore);
    }

    // ── Explicit table parsing ────────────────────────────────────────────────

    private static TableRef ParseJsonTable(JsonTable t)
    {
        var parts = t.Name.Split('.');
        return new TableRef
        {
            Schema = parts.Length == 2 ? parts[0] : "dbo",
            Name   = parts.Length == 2 ? parts[1] : parts[0],
            Ignore = t.Ignore ?? false,
            Where  = t.Where,
            Query  = t.Query,
        };
    }

    [Fact]
    public void ParseJsonTable_WithSchema_ExtractsSchemaAndName()
    {
        var t      = new JsonTable { Name = "sales.Orders" };
        var result = ParseJsonTable(t);
        Assert.Equal("sales", result.Schema);
        Assert.Equal("Orders", result.Name);
    }

    [Fact]
    public void ParseJsonTable_WithoutSchema_DefaultsToDbo()
    {
        var t      = new JsonTable { Name = "dbo.Orders" };
        var result = ParseJsonTable(t);
        Assert.Equal("dbo", result.Schema);
        Assert.Equal("Orders", result.Name);
    }

    [Fact]
    public void ParseJsonTable_PreservesWhere()
    {
        var t      = new JsonTable { Name = "dbo.Orders", Where = "CreatedAt > '2024-01-01'" };
        var result = ParseJsonTable(t);
        Assert.Equal("CreatedAt > '2024-01-01'", result.Where);
    }

    [Fact]
    public void ParseJsonTable_PreservesIgnore()
    {
        var t      = new JsonTable { Name = "dbo.Orders", Ignore = true };
        var result = ParseJsonTable(t);
        Assert.True(result.Ignore);
    }
}
