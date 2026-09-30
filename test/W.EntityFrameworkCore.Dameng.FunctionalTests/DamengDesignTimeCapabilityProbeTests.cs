using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Dm;
using Xunit;
using Xunit.Abstractions;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

/// <summary>
/// 设计时（反向工程与注释迁移）候选目录与语义的服务器测量。测试通过只表示所有候选均已尝试，
/// 不表示提供程序已实现对应能力；每个 case 的独立 JSON 行包含成功值或 SQL 错误。
/// </summary>
public sealed class DamengDesignTimeCapabilityProbeTests(ITestOutputHelper output)
{
    private const int CommandTimeoutSeconds = 15;

    private static readonly string[] CatalogCandidates =
    [
        "SYS.SYSOBJECTS",
        "SYS.SYSCOLUMNS",
        "SYS.SYSCONS",
        "SYS.SYSINDEXES",
        "SYS.SYSCOMMENTS",
        "SYS.SYSTABLECOMMENTS",
        "SYS.SYSCOLUMNCOMMENTS",
        "USER_TABLES",
        "USER_TAB_COLUMNS",
        "USER_TAB_COMMENTS",
        "USER_COL_COMMENTS",
        "USER_CONSTRAINTS",
        "USER_CONS_COLUMNS",
        "USER_INDEXES",
        "USER_IND_COLUMNS",
        "USER_VIEWS",
        "USER_SEQUENCES",
        "ALL_TABLES",
        "ALL_TAB_COLUMNS",
        "ALL_TAB_COMMENTS",
        "ALL_COL_COMMENTS",
        "ALL_CONSTRAINTS",
        "ALL_CONS_COLUMNS",
        "ALL_INDEXES",
        "ALL_IND_COLUMNS",
        "ALL_VIEWS",
        "ALL_SEQUENCES"
    ];

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task CatalogShapeAndCurrentSchema()
    {
        // 连接问题要使整个测试失败；目录缺失只留在各 case 的证据中。
        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();

        var cases = new List<ProbeCase>
        {
            new("ENV.current_schema", "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual"),
            new("ENV.current_schid", "SELECT CURRENT_SCHID() FROM dual"),
            new("ENV.user", "SELECT USER FROM dual"),
            new("ENV.session_user", "SELECT SESSION_USER FROM dual")
        };
        foreach (var probe in cases)
        {
            await ExecuteCaseAsync(connection, probe);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in CatalogCandidates)
        {
            var status = await DumpShapeAsync(connection, candidate);
            counts[status] = counts.GetValueOrDefault(status) + 1;
        }

        Write(new { Status = "summary", Cases = CatalogCandidates.Length, Counts = counts });
        Assert.Equal(CatalogCandidates.Length, counts.Values.Sum());
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task FixtureCatalogRows()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_DT_{suffix}";
        var sequenceName = $"EF10_DTSEQ_{suffix}";
        var primaryKeyName = $"PK_DT_{suffix}";
        var uniqueName = $"UQ_DT_{suffix}";
        var indexName = $"IDX_DT_{suffix}";

        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var sequenceCreated = false;
        var tableCreated = false;
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE SEQUENCE \"{sequenceName}\" START WITH 41 INCREMENT BY 3");
            sequenceCreated = true;
            await ExecuteNonQueryAsync(
                connection,
                $"""
                CREATE TABLE "{tableName}" (
                    "ID" BIGINT IDENTITY(3, 2) NOT NULL,
                    "CODE" VARCHAR(30) NOT NULL,
                    "NAME" NVARCHAR2(50) NOT NULL,
                    "PLAIN_VARCHAR" VARCHAR(30),
                    "AMOUNT" DECIMAL(18, 3),
                    "DURATION" INTERVAL DAY(4) TO SECOND(3),
                    "DURATION_WIDE" INTERVAL DAY(9) TO SECOND(6),
                    "DURATION_DEFAULT" INTERVAL DAY TO SECOND,
                    "MONTHS" INTERVAL YEAR(2) TO MONTH,
                    "SEQ_NUM" INT DEFAULT "{sequenceName}".NEXTVAL,
                    "STATUS" VARCHAR(8) DEFAULT 'NEW',
                    "CREATED_AT" DATETIME(7) DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT "{primaryKeyName}" PRIMARY KEY ("ID"),
                    CONSTRAINT "{uniqueName}" UNIQUE ("CODE", "NAME")
                )
                """);
            tableCreated = true;
            await ProbeDdlStepAsync(
                connection,
                "FIX.create_index",
                $"CREATE INDEX \"{indexName}\" ON \"{tableName}\" (\"STATUS\" ASC, \"CREATED_AT\" DESC)");
            await ProbeDdlStepAsync(connection, "FIX.comment_table", $"COMMENT ON TABLE \"{tableName}\" IS 'fixture 表注释'");
            await ProbeDdlStepAsync(connection, "FIX.comment_column_name", $"COMMENT ON COLUMN \"{tableName}\".\"NAME\" IS '名称列注释'");
            await ProbeDdlStepAsync(connection, "FIX.comment_column_status", $"COMMENT ON COLUMN \"{tableName}\".\"STATUS\" IS '状态''引号'");

            await DumpRowsAsync(connection, "FIX.user_tables", "SELECT * FROM USER_TABLES WHERE TABLE_NAME = :name", tableName);
            await DumpRowsAsync(connection, "FIX.user_tab_columns", "SELECT * FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :name ORDER BY COLUMN_ID", tableName);
            await DumpRowsAsync(connection, "FIX.user_tab_comments", "SELECT * FROM USER_TAB_COMMENTS WHERE TABLE_NAME = :name", tableName);
            await DumpRowsAsync(connection, "FIX.user_col_comments", "SELECT * FROM USER_COL_COMMENTS WHERE TABLE_NAME = :name ORDER BY COLUMN_NAME", tableName);
            await DumpRowsAsync(connection, "FIX.user_constraints", "SELECT * FROM USER_CONSTRAINTS WHERE TABLE_NAME = :name", tableName);
            await DumpRowsAsync(connection, "FIX.user_cons_columns", "SELECT * FROM USER_CONS_COLUMNS WHERE TABLE_NAME = :name ORDER BY CONSTRAINT_NAME, POSITION", tableName);
            await DumpRowsAsync(connection, "FIX.user_indexes", "SELECT * FROM USER_INDEXES WHERE TABLE_NAME = :name", tableName);
            await DumpRowsAsync(connection, "FIX.user_ind_columns", "SELECT * FROM USER_IND_COLUMNS WHERE TABLE_NAME = :name ORDER BY INDEX_NAME, COLUMN_POSITION", tableName);
            await DumpRowsAsync(connection, "FIX.user_sequences", "SELECT * FROM USER_SEQUENCES WHERE SEQUENCE_NAME = :name", sequenceName);
            await DumpRowsAsync(
                connection,
                "FIX.all_tables",
                "SELECT * FROM ALL_TABLES WHERE TABLE_NAME = :name AND OWNER = SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())",
                tableName);
            await DumpRowsAsync(
                connection,
                "FIX.all_tab_columns",
                "SELECT * FROM ALL_TAB_COLUMNS WHERE TABLE_NAME = :name AND OWNER = SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) ORDER BY COLUMN_ID",
                tableName);
            await DumpRowsAsync(
                connection,
                "FIX.sysobjects",
                "SELECT * FROM SYS.SYSOBJECTS WHERE NAME IN (:t, :pk, :uq, :idx, :seq)",
                tableName,
                ("pk", primaryKeyName),
                ("uq", uniqueName),
                ("idx", indexName),
                ("seq", sequenceName));
            await DumpRowsAsync(
                connection,
                "FIX.syscolumns",
                "SELECT C.* FROM SYS.SYSCOLUMNS C INNER JOIN SYS.SYSOBJECTS O ON C.ID = O.ID WHERE O.NAME = :name ORDER BY C.COLID",
                tableName);
        }
        finally
        {
            if (tableCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{tableName}\"");
            }

            if (sequenceCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP SEQUENCE \"{sequenceName}\"");
            }
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task StringLengthSemantics()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_DTS_{suffix}";

        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = false;
        var hasCharQualifiedColumn = true;
        try
        {
            try
            {
                await ExecuteNonQueryAsync(
                    connection,
                    $"""
                    CREATE TABLE "{tableName}" (
                        "ID" INT NOT NULL,
                        "NV3" NVARCHAR2(3),
                        "V3" VARCHAR(3),
                        "V3C" VARCHAR(3 CHAR),
                        "V9" VARCHAR(9),
                        "C3" CHAR(3),
                        CONSTRAINT "PK_DTS_{suffix}" PRIMARY KEY ("ID")
                    )
                    """);
                Write(new { Id = "STR.create_table", Status = "with_char_qualifier" });
            }
            catch (DbException error)
            {
                Write(new
                {
                    Id = "STR.create_table",
                    Status = "char_qualifier_rejected",
                    error.ErrorCode,
                    Summary = SafeSummary(error.Message)
                });
                hasCharQualifiedColumn = false;
                await ExecuteNonQueryAsync(
                    connection,
                    $"""
                    CREATE TABLE "{tableName}" (
                        "ID" INT NOT NULL,
                        "NV3" NVARCHAR2(3),
                        "V3" VARCHAR(3),
                        "V9" VARCHAR(9),
                        "C3" CHAR(3),
                        CONSTRAINT "PK_DTS_{suffix}" PRIMARY KEY ("ID")
                    )
                    """);
            }

            created = true;

            await ProbeInsertTextAsync(connection, tableName, "STR.nv3_3chinese", "NV3", "中文字");
            await ProbeInsertTextAsync(connection, tableName, "STR.nv3_4chinese", "NV3", "中文字符");
            await ProbeInsertTextAsync(connection, tableName, "STR.v3_1chinese", "V3", "中");
            await ProbeInsertTextAsync(connection, tableName, "STR.v3_2chinese", "V3", "中文");
            await ProbeInsertTextAsync(connection, tableName, "STR.v3_3ascii", "V3", "abc");
            await ProbeInsertTextAsync(connection, tableName, "STR.v3_4ascii", "V3", "abcd");
            if (hasCharQualifiedColumn)
            {
                await ProbeInsertTextAsync(connection, tableName, "STR.v3c_3chinese", "V3C", "中文字");
            }

            await ProbeInsertTextAsync(connection, tableName, "STR.v9_3chinese", "V9", "中文字");
            await ProbeInsertTextAsync(connection, tableName, "STR.c3_1chinese", "C3", "中");
            await ProbeInsertTextAsync(connection, tableName, "STR.c3_3ascii", "C3", "abc");

            await DumpRowsAsync(connection, "STR.user_tab_columns", "SELECT * FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :name ORDER BY COLUMN_ID", tableName);
        }
        finally
        {
            if (created)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task IdentityFlagsAndTypeCoverage()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var identityTable = $"EF10_DTI_{suffix}";
        var identityDefaultTable = $"EF10_DTJ_{suffix}";
        var typesTable = $"EF10_DTT_{suffix}";

        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var identityCreated = false;
        var identityDefaultCreated = false;
        var typesCreated = false;
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                $"""
                CREATE TABLE "{identityTable}" (
                    "A" INT,
                    "B" BIGINT IDENTITY(5,10) NOT NULL,
                    "C" INT,
                    CONSTRAINT "PK_DTI_{suffix}" PRIMARY KEY ("A")
                )
                """);
            identityCreated = true;
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE \"{identityDefaultTable}\" (\"X\" INT IDENTITY)");
            identityDefaultCreated = true;
            await ExecuteNonQueryAsync(
                connection,
                $"""
                CREATE TABLE "{typesTable}" (
                    "C_BIT" BIT,
                    "C_TINYINT" TINYINT,
                    "C_SMALLINT" SMALLINT,
                    "C_INT" INT,
                    "C_BIGINT" BIGINT,
                    "C_DECIMAL" DECIMAL,
                    "C_DECIMAL_PS" DECIMAL(10,2),
                    "C_REAL" REAL,
                    "C_FLOAT" FLOAT,
                    "C_DOUBLE" DOUBLE,
                    "C_CHAR" CHAR(5),
                    "C_VARCHAR" VARCHAR(20),
                    "C_VARCHAR_CHAR" VARCHAR(20 CHAR),
                    "C_NVARCHAR2" NVARCHAR2(20),
                    "C_CLOB" CLOB,
                    "C_NCLOB" NCLOB,
                    "C_TEXT" TEXT,
                    "C_BINARY" BINARY(4),
                    "C_VARBINARY" VARBINARY(16),
                    "C_BLOB" BLOB,
                    "C_DATE" DATE,
                    "C_TIME" TIME,
                    "C_TIME_P" TIME(3),
                    "C_DATETIME" DATETIME,
                    "C_DATETIME_P" DATETIME(3),
                    "C_TIMESTAMP" TIMESTAMP,
                    "C_DTO" DATETIME(7) WITH TIME ZONE,
                    "C_TS_TZ" TIMESTAMP WITH TIME ZONE,
                    "C_INTERVAL_DS" INTERVAL DAY(4) TO SECOND(3),
                    "C_INTERVAL_YM" INTERVAL YEAR(3) TO MONTH
                )
                """);
            typesCreated = true;

            foreach (var table in new[] { identityTable, identityDefaultTable, typesTable })
            {
                await DumpRowsAsync(
                    connection,
                    $"IDN.tab_columns.{table}",
                    "SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, DATA_SCALE, NULLABLE, CHAR_LENGTH, CHAR_USED, DATA_DEFAULT, COLUMN_ID FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :name ORDER BY COLUMN_ID",
                    table);
                await DumpRowsAsync(
                    connection,
                    $"IDN.syscolumns.{table}",
                    "SELECT C.NAME, C.COLID, C.TYPE$, C.LENGTH$, C.SCALE, C.NULLABLE$, C.DEFVAL, C.INFO1, C.INFO2 FROM SYS.SYSCOLUMNS C INNER JOIN SYS.SYSOBJECTS O ON C.ID = O.ID WHERE O.NAME = :name ORDER BY C.COLID",
                    table);
            }

            await ExecuteCaseAsync(connection, new ProbeCase("IDN.ident_seed", $"SELECT IDENT_SEED('{identityTable}') FROM dual"));
            await ExecuteCaseAsync(connection, new ProbeCase("IDN.ident_incr", $"SELECT IDENT_INCR('{identityTable}') FROM dual"));
            await ExecuteCaseAsync(connection, new ProbeCase("IDN.ident_seed_default", $"SELECT IDENT_SEED('{identityDefaultTable}') FROM dual"));
            await ExecuteCaseAsync(connection, new ProbeCase("IDN.ident_incr_default", $"SELECT IDENT_INCR('{identityDefaultTable}') FROM dual"));
            await ExecuteCaseAsync(connection, new ProbeCase("IDN.ident_current", $"SELECT IDENT_CURRENT('{identityTable}') FROM dual"));
        }
        finally
        {
            if (typesCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{typesTable}\"");
            }

            if (identityDefaultCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{identityDefaultTable}\"");
            }

            if (identityCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{identityTable}\"");
            }
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task SchemaCreationGuard()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var schemaName = $"EF10_DTSCH_{suffix}";

        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = false;
        try
        {
            await ProbeDdlStepAsync(connection, "SCH.if_not_exists_first", $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"");
            await ProbeDdlStepAsync(connection, "SCH.if_not_exists_second", $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"");
            created = true;

            var guardedBlock =
                "BEGIN\n"
                + "    IF NOT EXISTS (\n"
                + "        SELECT 1\n"
                + "        FROM SYS.SYSOBJECTS\n"
                + $"        WHERE TYPE$ = 'SCH' AND NAME = '{schemaName}'\n"
                + "    ) THEN\n"
                + $"        EXECUTE IMMEDIATE 'CREATE SCHEMA \"{schemaName}\"';\n"
                + "    END IF;\n"
                + "END;";
            await ProbeDdlStepAsync(connection, "SCH.guarded_block_repeat", guardedBlock);

            var idempotentWrapped = "EXECUTE IMMEDIATE '"
                + guardedBlock.Replace("'", "''", StringComparison.Ordinal)
                + "'";
            await ProbeDdlStepAsync(connection, "SCH.guarded_block_nested_dynamic", idempotentWrapped);

            var historyGuardWrapped =
                "BEGIN\n"
                + "    IF NOT EXISTS (\n"
                + "        SELECT 1\n"
                + "        FROM SYS.SYSOBJECTS\n"
                + $"        WHERE TYPE$ = 'SCH' AND NAME = '{schemaName}'\n"
                + "    ) THEN\n"
                + "        " + guardedBlock.Replace("\n", "\n        ", StringComparison.Ordinal) + "\n"
                + "    END IF;\n"
                + "END;";
            await ProbeDdlStepAsync(connection, "SCH.guarded_block_nested_in_history_if", historyGuardWrapped);

            await DumpRowsAsync(
                connection,
                "SCH.sysobjects_lookup",
                "SELECT NAME, TYPE$ FROM SYS.SYSOBJECTS WHERE TYPE$ = 'SCH' AND NAME = :name",
                schemaName);
        }
        finally
        {
            if (created)
            {
                await ProbeDdlStepAsync(connection, "SCH.drop", $"DROP SCHEMA \"{schemaName}\"");
            }
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task IdentityLookupQuoting()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var weirdTable = $"EF10_WEIRD.{suffix}";  // quoted name containing a dot
        var normalTable = $"EF10_NORM_{suffix}";

        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var weirdCreated = false;
        var normalCreated = false;
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE \"{weirdTable}\" (\"ID\" INT IDENTITY(9, 7) NOT NULL PRIMARY KEY)");
            weirdCreated = true;
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE \"{normalTable}\" (\"ID\" INT IDENTITY(4, 2) NOT NULL PRIMARY KEY)");
            normalCreated = true;

            var schema = string.Empty;
            await using (var schemaCommand = connection.CreateCommand())
            {
                schemaCommand.CommandText = "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual";
                schemaCommand.CommandTimeout = CommandTimeoutSeconds;
                schema = Convert.ToString(await schemaCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
            }

            await ExecuteCaseAsync(connection, new ProbeCase("IDLQ.normal_plain", $"SELECT IDENT_SEED('{schema}.{normalTable}') FROM dual"));
            await ExecuteCaseAsync(connection, new ProbeCase("IDLQ.normal_delimited", $"SELECT IDENT_SEED('\"{schema}\".\"{normalTable}\"') FROM dual"));
            await ExecuteCaseAsync(connection, new ProbeCase("IDLQ.weird_plain", $"SELECT IDENT_SEED('{schema}.{weirdTable}') FROM dual"));
            await ExecuteCaseAsync(connection, new ProbeCase("IDLQ.weird_delimited", $"SELECT IDENT_SEED('\"{schema}\".\"{weirdTable}\"') FROM dual"));
            await ExecuteCaseAsync(connection, new ProbeCase("IDLQ.weird_delimited_incr", $"SELECT IDENT_INCR('\"{schema}\".\"{weirdTable}\"') FROM dual"));
        }
        finally
        {
            if (weirdCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{weirdTable}\"");
            }

            if (normalCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{normalTable}\"");
            }
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task ForeignKeyAndViewCatalog()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var parentTable = $"EF10_DTP_{suffix}";
        var childTable = $"EF10_DTCH_{suffix}";
        var viewName = $"EF10_DTV_{suffix}";

        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var parentCreated = false;
        var childCreated = false;
        var viewCreated = false;
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE \"{parentTable}\" (\"ID\" INT PRIMARY KEY, \"CODE\" INT UNIQUE, \"NAME\" NVARCHAR2(20))");
            parentCreated = true;
            await ExecuteNonQueryAsync(
                connection,
                $"""
                CREATE TABLE "{childTable}" (
                    "ID" INT PRIMARY KEY,
                    "PID_CASCADE" INT REFERENCES "{parentTable}" ("ID") ON DELETE CASCADE,
                    "PID_SETNULL" INT REFERENCES "{parentTable}" ("ID") ON DELETE SET NULL,
                    "PID_DEFAULT" INT REFERENCES "{parentTable}" ("ID"),
                    "PCODE" INT REFERENCES "{parentTable}" ("CODE") ON DELETE CASCADE
                )
                """);
            childCreated = true;
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE VIEW \"{viewName}\" AS SELECT \"ID\", \"NAME\" FROM \"{parentTable}\"");
            viewCreated = true;
            await ProbeDdlStepAsync(connection, "FKV.comment_on_view", $"COMMENT ON TABLE \"{viewName}\" IS '视图注释'");

            await DumpRowsAsync(
                connection,
                "FKV.child_constraints",
                "SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE, DELETE_RULE, R_OWNER, R_CONSTRAINT_NAME FROM ALL_CONSTRAINTS WHERE OWNER = SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AND TABLE_NAME = :name ORDER BY CONSTRAINT_NAME",
                childTable);
            await DumpRowsAsync(
                connection,
                "FKV.view_tab_columns",
                "SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, NULLABLE FROM ALL_TAB_COLUMNS WHERE OWNER = SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AND TABLE_NAME = :name ORDER BY COLUMN_ID",
                viewName);
            await DumpRowsAsync(
                connection,
                "FKV.view_in_all_tables",
                "SELECT TABLE_NAME FROM ALL_TABLES WHERE OWNER = SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AND TABLE_NAME = :name",
                viewName);
            await DumpRowsAsync(
                connection,
                "FKV.view_comment",
                "SELECT TABLE_NAME, TABLE_TYPE, COMMENTS FROM ALL_TAB_COMMENTS WHERE OWNER = SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AND TABLE_NAME = :name",
                viewName);
            await DumpRowsAsync(
                connection,
                "FKV.sysobjects_view",
                "SELECT NAME, TYPE$, SUBTYPE$ FROM SYS.SYSOBJECTS WHERE NAME = :name",
                viewName);
        }
        finally
        {
            if (viewCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP VIEW \"{viewName}\"");
            }

            if (childCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{childTable}\"");
            }

            if (parentCreated)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{parentTable}\"");
            }
        }
    }

    [DamengFact]
    [Trait("Category", "CapabilityProbe")]
    public async Task CommentLifecycle()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12].ToUpperInvariant();
        var tableName = $"EF10_DTC_{suffix}";

        await using var connection = new DmConnection(DamengTestEnvironment.GetRequiredConnectionString());
        await connection.OpenAsync();
        var created = false;
        try
        {
            await ExecuteNonQueryAsync(
                connection,
                $"CREATE TABLE \"{tableName}\" (\"ID\" INT NOT NULL, \"NOTE\" NVARCHAR2(20), CONSTRAINT \"PK_DTC_{suffix}\" PRIMARY KEY (\"ID\"))");
            created = true;

            await ProbeCommentStepAsync(connection, tableName, "CMT.table_write", $"COMMENT ON TABLE \"{tableName}\" IS '首版注释'");
            await ProbeCommentStepAsync(connection, tableName, "CMT.table_overwrite", $"COMMENT ON TABLE \"{tableName}\" IS '覆盖注释'");
            await ProbeCommentStepAsync(connection, tableName, "CMT.table_clear_empty", $"COMMENT ON TABLE \"{tableName}\" IS ''");
            await ProbeCommentStepAsync(connection, tableName, "CMT.table_clear_null", $"COMMENT ON TABLE \"{tableName}\" IS NULL");
            await ProbeCommentStepAsync(connection, tableName, "CMT.column_write", $"COMMENT ON COLUMN \"{tableName}\".\"NOTE\" IS '列注释'");
            await ProbeCommentStepAsync(connection, tableName, "CMT.column_clear_empty", $"COMMENT ON COLUMN \"{tableName}\".\"NOTE\" IS ''");
            await ProbeCommentStepAsync(connection, tableName, "CMT.column_clear_null", $"COMMENT ON COLUMN \"{tableName}\".\"NOTE\" IS NULL");
        }
        finally
        {
            if (created)
            {
                await ExecuteNonQueryAsync(connection, $"DROP TABLE \"{tableName}\"");
            }
        }
    }

    private async Task ProbeCommentStepAsync(DbConnection connection, string tableName, string id, string sql)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await ExecuteNonQueryAsync(connection, sql);
            Write(new { Id = id, Status = "executed", ElapsedMs = watch.ElapsedMilliseconds });
        }
        catch (DbException error)
        {
            Write(new
            {
                Id = id,
                Status = "sql_error",
                error.ErrorCode,
                Summary = SafeSummary(error.Message),
                ElapsedMs = watch.ElapsedMilliseconds
            });
            return;
        }

        var isTable = id.StartsWith("CMT.table", StringComparison.Ordinal);
        var readback = isTable
            ? "SELECT COMMENTS FROM USER_TAB_COMMENTS WHERE TABLE_NAME = :name"
            : "SELECT COMMENTS FROM USER_COL_COMMENTS WHERE TABLE_NAME = :name AND COLUMN_NAME = 'NOTE'";
        await DumpRowsAsync(connection, $"{id}.readback", readback, tableName);
    }

    private async Task ProbeDdlStepAsync(DbConnection connection, string id, string sql)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await ExecuteNonQueryAsync(connection, sql);
            Write(new { Id = id, Status = "executed", ElapsedMs = watch.ElapsedMilliseconds });
        }
        catch (DbException error)
        {
            Write(new
            {
                Id = id,
                Status = "sql_error",
                error.ErrorCode,
                Summary = SafeSummary(error.Message),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
    }

    private async Task ProbeInsertTextAsync(DbConnection connection, string tableName, string id, string column, string value)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO \"{tableName}\" (\"ID\", \"{column}\") VALUES (1, :value)";
            command.CommandTimeout = CommandTimeoutSeconds;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "value";
            parameter.Value = value;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
            await ExecuteNonQueryAsync(connection, $"DELETE FROM \"{tableName}\" WHERE \"ID\" = 1");
            Write(new
            {
                Id = id,
                Status = "inserted",
                CharLength = value.Length,
                Utf8Length = System.Text.Encoding.UTF8.GetByteCount(value),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
        catch (DbException error)
        {
            Write(new
            {
                Id = id,
                Status = "sql_error",
                error.ErrorCode,
                Summary = SafeSummary(error.Message),
                CharLength = value.Length,
                Utf8Length = System.Text.Encoding.UTF8.GetByteCount(value),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
    }

    private async Task<string> DumpShapeAsync(DbConnection connection, string catalogObject)
    {
        var id = $"SHAPE.{catalogObject}";
        var watch = Stopwatch.StartNew();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {catalogObject} WHERE 1 = 0";
            command.CommandTimeout = CommandTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new List<string>();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                columns.Add($"{reader.GetName(index)}:{reader.GetDataTypeName(index)}");
            }

            Write(new { Id = id, Status = "shape", Columns = columns, ElapsedMs = watch.ElapsedMilliseconds });
            return "shape";
        }
        catch (DbException error)
        {
            Write(new
            {
                Id = id,
                Status = "sql_error",
                error.ErrorCode,
                Summary = SafeSummary(error.Message),
                ElapsedMs = watch.ElapsedMilliseconds
            });
            return "sql_error";
        }
    }

    private async Task DumpRowsAsync(
        DbConnection connection,
        string id,
        string sql,
        string nameValue,
        params (string Name, string Value)[] extraParameters)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = CommandTimeoutSeconds;
            var parameter = command.CreateParameter();
            parameter.ParameterName = sql.Contains(":t", StringComparison.Ordinal) && !sql.Contains(":name", StringComparison.Ordinal) ? "t" : "name";
            parameter.Value = nameValue;
            command.Parameters.Add(parameter);
            foreach (var (name, value) in extraParameters)
            {
                var extra = command.CreateParameter();
                extra.ParameterName = name;
                extra.Value = value;
                command.Parameters.Add(extra);
            }

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    row[reader.GetName(index)] = FormatValue(reader.GetValue(index));
                }

                rows.Add(row);
            }

            Write(new { Id = id, Status = "rows", RowCount = rows.Count, Rows = rows, ElapsedMs = watch.ElapsedMilliseconds });
        }
        catch (DbException error)
        {
            Write(new
            {
                Id = id,
                Status = "sql_error",
                error.ErrorCode,
                Summary = SafeSummary(error.Message),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
    }

    private async Task ExecuteCaseAsync(DbConnection connection, ProbeCase probe)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = probe.Sql;
            command.CommandTimeout = CommandTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                Write(new { probe.Id, Status = "no_row", ElapsedMs = watch.ElapsedMilliseconds });
                return;
            }

            var value = reader.GetValue(0);
            Write(new
            {
                probe.Id,
                Status = "value",
                Value = FormatValue(value),
                ClrType = value is DBNull ? null : value.GetType().FullName,
                DataType = reader.GetDataTypeName(0),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
        catch (DbException error)
        {
            Write(new
            {
                probe.Id,
                Status = "sql_error",
                error.ErrorCode,
                Summary = SafeSummary(error.Message),
                ElapsedMs = watch.ElapsedMilliseconds
            });
        }
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync();
    }

    private void Write<T>(T result)
        => output.WriteLine(JsonSerializer.Serialize(result));

    private static string? FormatValue(object value)
        => value switch
        {
            DBNull => null,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => bytes.Length > 64 ? $"[hex:{bytes.Length} bytes]" : Convert.ToHexString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

    private static string SafeSummary(string message)
    {
        var firstLine = message.Split(['\r', '\n'], 2)[0];
        var connectionString = DamengTestEnvironment.ConnectionString;
        if (!string.IsNullOrEmpty(connectionString))
        {
            firstLine = firstLine.Replace(connectionString, "[redacted]", StringComparison.OrdinalIgnoreCase);
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (string key in builder.Keys)
            {
                if (!IsSensitiveConnectionField(key))
                {
                    continue;
                }

                var value = Convert.ToString(builder[key], CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(value))
                {
                    firstLine = firstLine.Replace(value, "[redacted]", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return firstLine.Length > 160 ? firstLine[..160] : firstLine;
    }

    private static bool IsSensitiveConnectionField(string key)
        => key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Equals("pwd", StringComparison.OrdinalIgnoreCase)
            || key.Contains("user", StringComparison.OrdinalIgnoreCase)
            || key.Equals("uid", StringComparison.OrdinalIgnoreCase)
            || key.Contains("server", StringComparison.OrdinalIgnoreCase)
            || key.Contains("host", StringComparison.OrdinalIgnoreCase)
            || key.Contains("data source", StringComparison.OrdinalIgnoreCase);

    private sealed record ProbeCase(string Id, string Sql);
}
