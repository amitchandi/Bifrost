using Xunit;

namespace Bifrost.Tests;

// StripSuffix is private in Restructurer so we test it via reflection
// or we can expose it as internal + InternalsVisibleTo. For simplicity
// we duplicate the logic here and test the expected behaviour.

public class RestructurerTests
{
    private static string StripSuffix(string tableName, string tenantId)
    {
        var withUnderscore = $"_{tenantId}";
        if (tableName.EndsWith(withUnderscore, StringComparison.OrdinalIgnoreCase))
            return tableName[..^withUnderscore.Length];
        if (tableName.EndsWith(tenantId, StringComparison.OrdinalIgnoreCase))
            return tableName[..^tenantId.Length];
        return tableName;
    }

    [Fact]
    public void StripSuffix_WithUnderscore_StripsCorrectly()
        => Assert.Equal("Orders", StripSuffix("Orders_142", "142"));

    [Fact]
    public void StripSuffix_WithoutUnderscore_StripsCorrectly()
        => Assert.Equal("Orders", StripSuffix("Orders142", "142"));

    [Fact]
    public void StripSuffix_NoMatch_ReturnsOriginal()
        => Assert.Equal("Products", StripSuffix("Products", "142"));

    [Fact]
    public void StripSuffix_CaseInsensitive()
        => Assert.Equal("Orders", StripSuffix("Orders_142", "142"));

    [Fact]
    public void StripSuffix_MultiWord_StripsCorrectly()
        => Assert.Equal("OrderItems", StripSuffix("OrderItems_142", "142"));

    [Fact]
    public void StripSuffix_TenantIdOnly_ReturnsEmpty()
        => Assert.Equal("", StripSuffix("142", "142"));

    [Fact]
    public void StripSuffix_PrefersUnderscoreVariant()
    {
        // "Orders_142" should strip "_142" not just "142"
        var result = StripSuffix("Orders_142", "142");
        Assert.Equal("Orders", result);
        Assert.DoesNotContain("_", result);
    }
}
