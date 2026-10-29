using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace W.EntityFrameworkCore.Dameng.Tests;

public sealed class DamengHistoryScriptTests
{
    [Theory]
    [InlineData(null)] [InlineData("app")] [InlineData("CREATE TABLE schema")]
    [InlineData("schema\"'quoted")] [InlineData("点.模式")] [InlineData("create Table schema")]
    [InlineData("/* CREATE TABLE */ -- name")]
    public void CreateIfMissingPreservesSchemaLiteralAndIdentifier(string? schema)
        => AssertExactInsertion(schema, "__History");

    [Theory]
    [InlineData("CREATE TABLE history")][InlineData("h\"'istory")][InlineData("点.历史")]
    public void CreateIfMissingPreservesHistoryTableIdentifier(string table)
        => AssertExactInsertion("CREATE TABLE 模式", table);

    [Fact]
    public void CreateIfMissingContainsExactlyOneRealHistoryCreate()
        => AssertExactInsertion("CREATE TABLE '/* CREATE TABLE */\"", "CREATE TABLE \".;--");

    private static void AssertExactInsertion(string? schema, string table)
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseDameng(
            "Server=localhost;User=test;Password=test", d => d.MigrationsHistoryTable(table, schema)).Options);
        var repository = context.GetService<IHistoryRepository>();
        var before = repository.GetCreateScript(); var after = repository.GetCreateIfNotExistsScript();
        var qualified = schema is null ? Quote(table) : Quote(schema) + "." + Quote(table);
        var create = "CREATE TABLE " + qualified + " (";
        var guarded = "CREATE TABLE IF NOT EXISTS " + qualified + " (";
        Assert.Contains(create, before);
        Assert.Contains(guarded, after);
        Assert.Equal(before.Replace(create, guarded, StringComparison.Ordinal), after);
        Assert.Equal(before.Length + "IF NOT EXISTS ".Length, after.Length);
        if (schema is not null)
        {
            Assert.Contains("NAME = '" + schema.Replace("'", "''", StringComparison.Ordinal) + "'", after);
            Assert.Contains("'CREATE SCHEMA " + Quote(schema).Replace("'", "''", StringComparison.Ordinal) + "'", after);
        }
    }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
