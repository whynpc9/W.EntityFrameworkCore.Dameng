using Xunit;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// Pure script-splitting coverage for <see cref="DamengScriptExecutor"/>; no database needed.
/// </summary>
public sealed class DamengScriptExecutorTests
{
    [Theory]
    [InlineData("BEGIN NULL; END;")]
    [InlineData("-- lead\nBEGIN -- body\n NULL; END; -- tail")]
    [InlineData("/* lead ' BEGIN */ DECLARE v INT; BEGIN v := 1; END;")]
    [InlineData("BEGIN BEGIN NULL; END; IF 1=1 THEN NULL; END IF; END;")]
    [InlineData("BEGIN DECLARE v INT; BEGIN v := CASE WHEN 1=1 THEN 2 ELSE 3 END; END; END;")]
    [InlineData("BEGIN CASE WHEN 1=1 THEN NULL; END CASE; END;")]
    [InlineData("BEGIN /* block\n/\nEND; ' */ NULL; END;")]
    [InlineData("BEGIN -- quote ' in comment\n NULL; END;")]
    [InlineData("DECLARE PROCEDURE p IS BEGIN NULL; END; BEGIN NULL; END;")]
    [InlineData("DECLARE PROCEDURE p; PROCEDURE p IS BEGIN NULL; END p; BEGIN p; END;")]
    [InlineData("DECLARE PROCEDURE p IS PROCEDURE q IS BEGIN NULL; END; BEGIN q; END; BEGIN p; END;")]
    [InlineData("DECLARE FUNCTION f RETURN INT IS BEGIN RETURN CASE WHEN 1=1 THEN 1 ELSE 2 END; END; BEGIN NULL; END;")]
    public void SplitRecognizesTokensAndPreservesBlockComments(string block)
    {
        foreach (var idempotent in new[] { false, true })
        {
            var script = "SELECT 1 FROM dual;\n" + block + (idempotent ? "\n/\n" : "\n") + "SELECT 2 FROM dual;";
            var batches = idempotent
                ? DamengScriptExecutor.SplitIdempotent(script)
                : DamengScriptExecutor.SplitStatements(script);
            Assert.Equal(3, batches.Count);
            Assert.Equal("SELECT 1 FROM dual", batches[0]);
            Assert.Contains("END;", batches[1]);
            Assert.StartsWith(block.Split("END;")[0], batches[1], StringComparison.Ordinal);
            Assert.Contains("SELECT 2 FROM dual", batches[2]);
        }
    }

    [Theory]
    [InlineData("BEGIN NULL;")]
    [InlineData("DECLARE v INT;")]
    [InlineData("BEGIN NULL; END")]
    [InlineData("/* unterminated")]
    [InlineData("SELECT 'unterminated")]
    public void SplitRejectsIncompleteCommands(string script)
        => Assert.Throws<InvalidOperationException>(() => DamengScriptExecutor.SplitStatements(script));

    [Theory]
    [InlineData("CREATE PROCEDURE p AS BEGIN NULL; END;")]
    [InlineData("CREATE OR REPLACE FUNCTION f RETURN INT AS BEGIN RETURN 1; END;")]
    [InlineData("CREATE TRIGGER t AFTER INSERT ON x BEGIN NULL; END;")]
    [InlineData("CREATE PACKAGE p AS PROCEDURE q; END;")]
    public void SplitRejectsStoredDefinitionsBeforeReturningPartialCommands(string definition)
        => Assert.Throws<NotSupportedException>(() => DamengScriptExecutor.SplitStatements("SELECT 1 FROM dual;" + definition));

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

    [Fact]
    public void SplitKeepsLowercaseAndIndentedBlocksWhole()
    {
        var script = """

              begin
                null;
              end;
            CREATE TABLE "T" ("ID" INT);
            """;

        var batches = DamengScriptExecutor.SplitStatements(script);

        Assert.Equal(2, batches.Count);
        Assert.StartsWith("begin", batches[0], StringComparison.Ordinal);
        Assert.EndsWith("end;", batches[0], StringComparison.Ordinal);
        Assert.StartsWith("CREATE TABLE", batches[1], StringComparison.Ordinal);
    }

    [Fact]
    public void SplitKeepsDeclareOpenedBlocksWhole()
    {
        var script = """
            DECLARE
                v INT;
            BEGIN
                v := 1;
            END;
            CREATE TABLE "T" ("ID" INT);
            """;

        var batches = DamengScriptExecutor.SplitStatements(script);

        Assert.Equal(2, batches.Count);
        Assert.StartsWith("DECLARE", batches[0], StringComparison.Ordinal);
        Assert.EndsWith("END;", batches[0], StringComparison.Ordinal);
        Assert.StartsWith("CREATE TABLE", batches[1], StringComparison.Ordinal);
    }
}
