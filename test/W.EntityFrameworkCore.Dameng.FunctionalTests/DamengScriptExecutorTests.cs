using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// Pure script-splitting coverage for <see cref="DamengScriptExecutor"/>; no database needed.
/// </summary>
public sealed class DamengScriptExecutorTests
{
    [Fact]
    public void IdempotentSplitKeepsNestedGuardBlocksAsSingleBatches()
    {
        var script = """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" ("MigrationId" NVARCHAR2(150) NOT NULL);
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '1'
                ) THEN
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1
                            FROM SYS.SYSOBJECTS
                            WHERE TYPE$ = 'SCH' AND NAME = 'app'
                        ) THEN
                            EXECUTE IMMEDIATE 'CREATE SCHEMA "app"';
                        END IF;
                    END;
                    EXECUTE IMMEDIATE 'CREATE TABLE "T" ("ID" INT)';
            END IF;
            END;
            /
            """;

        var batches = DamengScriptExecutor.SplitIdempotent(script);

        Assert.Equal(2, batches.Count);
        Assert.StartsWith("CREATE TABLE IF NOT EXISTS", batches[0], StringComparison.Ordinal);
        Assert.StartsWith("BEGIN", batches[1], StringComparison.Ordinal);
        Assert.EndsWith("END;", batches[1], StringComparison.Ordinal);
        Assert.Equal(2, batches[1].Split('\n').Count(line => line.Trim() == "BEGIN"));
    }

    [Fact]
    public void IdempotentSplitIgnoresBlockKeywordsInsideLiteralsAndComments()
    {
        // A multi-line comment text lands inside an EXECUTE IMMEDIATE literal; its END;/BEGIN
        // lines must not terminate the surrounding history guard block.
        var script = """
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '1'
                ) THEN
                    EXECUTE IMMEDIATE 'COMMENT ON TABLE "T" IS ''hello
            END;
            world''';
                    EXECUTE IMMEDIATE 'COMMENT ON COLUMN "T"."C" IS ''/* note
            BEGIN
            */''';
            END IF;
            END;
            /
            """;

        var batch = Assert.Single(DamengScriptExecutor.SplitIdempotent(script));
        Assert.StartsWith("BEGIN", batch, StringComparison.Ordinal);
        Assert.EndsWith("END;", batch, StringComparison.Ordinal);
        Assert.Contains("world", batch, StringComparison.Ordinal);
        Assert.Contains("*/", batch, StringComparison.Ordinal);
    }

    [Fact]
    public void StatementSplitKeepsBlocksWholeWithoutIdempotentMarkers()
    {
        var script = """
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM SYS.SYSOBJECTS
                    WHERE TYPE$ = 'SCH' AND NAME = 'app'
                ) THEN
                    EXECUTE IMMEDIATE 'CREATE SCHEMA "app"';
                END IF;
            END;
            CREATE TABLE "T" ("ID" INT);
            """;

        var batches = DamengScriptExecutor.SplitStatements(script);

        Assert.Equal(2, batches.Count);
        Assert.StartsWith("BEGIN", batches[0], StringComparison.Ordinal);
        Assert.StartsWith("CREATE TABLE", batches[1], StringComparison.Ordinal);
    }
}
