using Bifrost.Core;
using Xunit;

namespace Bifrost.Tests;

public class SqlBuilderTests
{
    // ── EscapeValue ───────────────────────────────────────────────────────────

    [Fact]
    public void EscapeValue_Null_ReturnsNullLiteral()
        => Assert.Equal("NULL", SqlBuilder.EscapeValue(null, "varchar"));

    [Fact]
    public void EscapeValue_DBNull_ReturnsNullLiteral()
        => Assert.Equal("NULL", SqlBuilder.EscapeValue(DBNull.Value, "varchar"));

    [Fact]
    public void EscapeValue_Int_ReturnsRawNumber()
        => Assert.Equal("42", SqlBuilder.EscapeValue(42, "int"));

    [Fact]
    public void EscapeValue_Decimal_ReturnsInvariantCulture()
        => Assert.Equal("3.14", SqlBuilder.EscapeValue(3.14m, "decimal"));

    [Fact]
    public void EscapeValue_Bool_True_Returns1()
        => Assert.Equal("1", SqlBuilder.EscapeValue(true, "bit"));

    [Fact]
    public void EscapeValue_Bool_False_Returns0()
        => Assert.Equal("0", SqlBuilder.EscapeValue(false, "bit"));

    [Fact]
    public void EscapeValue_String_WrapsInSingleQuotes()
        => Assert.Equal("'hello'", SqlBuilder.EscapeValue("hello", "varchar"));

    [Fact]
    public void EscapeValue_String_EscapesSingleQuotes()
        => Assert.Equal("'it''s'", SqlBuilder.EscapeValue("it's", "varchar"));

    [Fact]
    public void EscapeValue_NVarChar_AddsNPrefix()
        => Assert.Equal("N'hello'", SqlBuilder.EscapeValue("hello", "nvarchar"));

    [Fact]
    public void EscapeValue_NChar_AddsNPrefix()
        => Assert.Equal("N'hello'", SqlBuilder.EscapeValue("hello", "nchar"));

    [Fact]
    public void EscapeValue_DateTime_FormatsCorrectly()
    {
        var dt = new DateTime(2024, 3, 15, 10, 30, 0, 500);
        Assert.Equal("'2024-03-15T10:30:00.500'", SqlBuilder.EscapeValue(dt, "datetime"));
    }

    [Fact]
    public void EscapeValue_ByteArray_ReturnsHex()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        Assert.Equal("0xDEADBEEF", SqlBuilder.EscapeValue(bytes, "varbinary"));
    }

    // ── BuildCreateTable ──────────────────────────────────────────────────────

    [Fact]
    public void BuildCreateTable_ContainsIfNotExists()
    {
        var cols = new List<ColumnInfo>
        {
            new() { ColName = "Id", DataType = "int", IsNullable = false, IsIdentity = true, IsPrimaryKey = true }
        };
        var sql = SqlBuilder.BuildCreateTable("dbo", "Orders", cols);
        Assert.Contains("IF OBJECT_ID", sql);
        Assert.Contains("IS NULL", sql);
    }

    [Fact]
    public void BuildCreateTable_IncludesIdentity()
    {
        var cols = new List<ColumnInfo>
        {
            new() { ColName = "Id", DataType = "int", IsNullable = false, IsIdentity = true, IsPrimaryKey = true }
        };
        var sql = SqlBuilder.BuildCreateTable("dbo", "Orders", cols);
        Assert.Contains("IDENTITY(1,1)", sql);
    }

    [Fact]
    public void BuildCreateTable_VarcharWithLength()
    {
        var cols = new List<ColumnInfo>
        {
            new() { ColName = "Name", DataType = "varchar", MaxLength = 100, IsNullable = true }
        };
        var sql = SqlBuilder.BuildCreateTable("dbo", "Test", cols);
        Assert.Contains("varchar(100)", sql);
    }

    [Fact]
    public void BuildCreateTable_NVarcharMax()
    {
        var cols = new List<ColumnInfo>
        {
            new() { ColName = "Body", DataType = "nvarchar", MaxLength = -1, IsNullable = true }
        };
        var sql = SqlBuilder.BuildCreateTable("dbo", "Test", cols);
        Assert.Contains("nvarchar(MAX)", sql);
    }

    [Fact]
    public void BuildCreateTable_DecimalWithPrecisionScale()
    {
        var cols = new List<ColumnInfo>
        {
            new() { ColName = "Price", DataType = "decimal", Precision = 18, Scale = 4, IsNullable = false }
        };
        var sql = SqlBuilder.BuildCreateTable("dbo", "Test", cols);
        Assert.Contains("decimal(18,4)", sql);
    }

    [Fact]
    public void BuildCreateTable_EndsWithGo()
    {
        var cols = new List<ColumnInfo>
        {
            new() { ColName = "Id", DataType = "int", IsNullable = false }
        };
        var sql = SqlBuilder.BuildCreateTable("dbo", "Test", cols);
        Assert.EndsWith("GO\r\n", sql);
    }
}
