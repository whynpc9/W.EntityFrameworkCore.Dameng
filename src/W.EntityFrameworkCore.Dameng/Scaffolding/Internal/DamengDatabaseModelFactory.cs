using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Dm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;

namespace W.EntityFrameworkCore.Dameng.Scaffolding.Internal;

/// <summary>
/// Reads the current Dameng schema into a <see cref="DatabaseModel"/> for reverse engineering.
/// Only the session's current schema is scanned; the connection login and the current schema
/// may differ, so catalog queries filter by <c>SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())</c>.
/// </summary>
internal sealed class DamengDatabaseModelFactory : DatabaseModelFactory
{
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public DamengDatabaseModelFactory(IRelationalTypeMappingSource typeMappingSource)
    {
        _typeMappingSource = typeMappingSource;
    }

    private static readonly Regex NextValDefaultPattern = new(
        @"^\s*(?:(?<schema>""(?:[^""]|"""")*""|[\w$#]+)\.)?(?<seq>""(?:[^""]|"""")*""|[\w$#]+)\.NEXTVAL\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record PendingSequenceDefault(
        DatabaseColumn Column,
        string RawDefault,
        string SequenceName,
        string? SequenceSchema);

    public override DatabaseModel Create(
        string connectionString,
        DatabaseModelFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        using var connection = new DmConnection(connectionString);
        return Create(connection, options);
    }

    public override DatabaseModel Create(
        DbConnection connection,
        DatabaseModelFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        var databaseModel = new DatabaseModel();

        var connectionStartedOpen = connection.State == ConnectionState.Open;
        if (!connectionStartedOpen)
        {
            connection.Open();
        }

        try
        {
            var currentSchema = GetCurrentSchema(connection);
            databaseModel.DefaultSchema = currentSchema;

            var schemaFilter = options.Schemas.Select(NormalizeIdentifier).ToList();
            if (schemaFilter.Any(entry => !string.Equals(entry, currentSchema, StringComparison.Ordinal)))
            {
                throw new NotSupportedException(
                    "Dameng reverse engineering reads only the session's current schema "
                    + "(SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())); "
                    + "the requested schema filter includes a different schema.");
            }

            var tableFilter = BuildTableFilter(options.Tables.ToList(), currentSchema);

            var tables = GetTables(connection, currentSchema, tableFilter);
            tables.AddRange(GetViews(connection, currentSchema, tableFilter));
            if (tables.Count == 0)
            {
                return databaseModel;
            }

            var tableLookup = tables.ToDictionary(table => table.Name, StringComparer.Ordinal);
            ValidateUnsupportedTableStructures(connection, currentSchema, tableLookup);
            ValidateConstraintStates(connection, currentSchema, tableLookup);

            var pendingSequenceDefaults = new List<PendingSequenceDefault>();
            LoadColumns(connection, currentSchema, tableLookup, pendingSequenceDefaults);
            var columnLookup = tables.ToDictionary(
                table => table,
                table => table.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal));
            ResolveSequenceDefaults(connection, currentSchema, databaseModel, pendingSequenceDefaults);
            LoadIdentityAnnotations(connection, currentSchema, tableLookup, columnLookup);
            LoadConstraints(connection, currentSchema, tableLookup, columnLookup);
            LoadIndexes(connection, currentSchema, tableLookup, columnLookup);
            LoadForeignKeys(connection, currentSchema, tableLookup, columnLookup);
            LoadComments(connection, currentSchema, tableLookup, columnLookup);

            foreach (var table in tables)
            {
                table.Database = databaseModel;
                databaseModel.Tables.Add(table);
            }

            return databaseModel;
        }
        finally
        {
            if (!connectionStartedOpen)
            {
                connection.Close();
            }
        }
    }

    private static string GetCurrentSchema(DbConnection connection)
    {
        using var command = CreateCommand(
            connection,
            "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual");
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
    }

    private static HashSet<string>? BuildTableFilter(List<string> tables, string currentSchema)
    {
        if (tables.Count == 0)
        {
            return null;
        }

        var filter = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in tables)
        {
            var (entrySchema, entryName) = SplitQualifiedName(entry);
            if (entrySchema is not null)
            {
                if (!string.Equals(entrySchema, currentSchema, StringComparison.Ordinal))
                {
                    continue;
                }

                filter.Add(entryName);
            }
            else
            {
                filter.Add(entryName);
            }
        }

        if (filter.Count == 0)
        {
            throw new NotSupportedException(
                "Dameng reverse engineering reads only the session's current schema; "
                + "all requested tables were qualified with a different schema.");
        }

        return filter;
    }

    private static List<DatabaseTable> GetTables(
        DbConnection connection,
        string schema,
        HashSet<string>? tableFilter)
    {
        using var command = CreateCommand(
            connection,
            "SELECT T.TABLE_NAME, T.TEMPORARY, T.PARTITIONED, O.INFO3 FROM ALL_TABLES T "
            + "LEFT JOIN SYS.SYSOBJECTS S ON S.NAME = T.OWNER AND S.TYPE$ = 'SCH' "
            + "LEFT JOIN SYS.SYSOBJECTS O ON O.SCHID = S.ID AND O.NAME = T.TABLE_NAME "
            + "AND O.TYPE$ = 'SCHOBJ' AND O.SUBTYPE$ = 'UTAB' "
            + "WHERE T.OWNER = :schema ORDER BY T.TABLE_NAME");
        AddParameter(command, "schema", schema);

        var tables = new List<DatabaseTable>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (tableFilter is not null && !tableFilter.Contains(name))
            {
                continue;
            }

            ValidateTableKind(name, GetNullableString(reader, 1), GetNullableString(reader, 2));
            ValidateNativeTableKind(name, GetNullableInt64(reader, 3));
            tables.Add(
                new DatabaseTable
                {
                    Name = name,
                    Schema = schema
                });
        }

        return tables;
    }

    internal static void ValidateTableKind(string table, string? temporary, string? partitioned)
    {
        if (!string.Equals(temporary, "N", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Dameng table '{table}' has unsupported TEMPORARY marker '{temporary ?? "NULL"}'. "
                + "Reverse engineering cannot preserve temporary-table lifetime; exclude this table.");
        }

        if (!string.Equals(partitioned, "NO", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Dameng table '{table}' has unsupported PARTITIONED marker '{partitioned ?? "NULL"}'. "
                + "Reverse engineering cannot preserve partition definitions; exclude this table.");
        }
    }

    internal static void ValidateNativeTableKind(string table, long? info3)
    {
        // INFO3's low six bits identify the native table kind. HUGE tables are still UTAB
        // objects and can report TEMPORARY=N/PARTITIONED=NO. Only ordinary kind 0 is modeled.
        var kind = info3 & 0x3FL;
        if (kind != 0)
        {
            throw new NotSupportedException(
                $"Dameng table '{table}' has unsupported native table kind '{kind?.ToString(CultureInfo.InvariantCulture) ?? "NULL"}'. "
                + "Reverse engineering cannot preserve HUGE or other non-ordinary table definitions; exclude this table.");
        }
    }

    private static List<DatabaseTable> GetViews(
        DbConnection connection,
        string schema,
        HashSet<string>? tableFilter)
    {
        using var command = CreateCommand(
            connection,
            "SELECT VIEW_NAME FROM ALL_VIEWS WHERE OWNER = :schema ORDER BY VIEW_NAME");
        AddParameter(command, "schema", schema);

        var views = new List<DatabaseTable>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (tableFilter is not null && !tableFilter.Contains(name))
            {
                continue;
            }

            views.Add(
                new DatabaseView
                {
                    Name = name,
                    Schema = schema
                });
        }

        return views;
    }

    private void LoadColumns(
        DbConnection connection,
        string schema,
        Dictionary<string, DatabaseTable> tables,
        List<PendingSequenceDefault> pendingSequenceDefaults)
    {
        using var command = CreateCommand(
            connection,
            """
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, DATA_SCALE,
                   NULLABLE, CHAR_LENGTH, CHAR_USED, DATA_DEFAULT, COLUMN_ID
            FROM ALL_TAB_COLUMNS
            WHERE OWNER = :schema
            ORDER BY TABLE_NAME, COLUMN_ID
            """);
        AddParameter(command, "schema", schema);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            if (!tables.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var dataType = reader.GetString(2).Trim();
            var dataLength = GetNullableInt64(reader, 3);
            var dataPrecision = GetNullableInt64(reader, 4);
            var dataScale = GetNullableInt64(reader, 5);
            var nullable = string.Equals(reader.GetString(6), "Y", StringComparison.Ordinal);
            var charLength = GetNullableInt64(reader, 7);
            var charUsed = GetNullableString(reader, 8);
            var defaultValue = GetNullableString(reader, 9);

            var columnName = reader.GetString(1);
            var storeType = BuildStoreType(dataType, dataLength, dataPrecision, dataScale, charLength, charUsed);
            ValidateColumnType(tableName, columnName, storeType);
            var column = new DatabaseColumn
            {
                Table = table,
                Name = columnName,
                StoreType = storeType,
                IsNullable = nullable
            };

            if (defaultValue is not null
                && TryParseSequenceDefault(defaultValue, out var sequenceName, out var sequenceSchema))
            {
                // Facets are resolved against the catalog after all columns are read; only then
                // is the default converted into a sequence strategy.
                pendingSequenceDefaults.Add(
                    new PendingSequenceDefault(column, defaultValue, sequenceName, sequenceSchema));
            }
            else if (defaultValue is not null)
            {
                column.DefaultValueSql = defaultValue;
            }

            table.Columns.Add(column);
        }
    }

    internal void ValidateColumnType(string table, string column, string storeType)
    {
        // EF's scaffolding pipeline otherwise warns and silently drops unmapped columns.
        // Resolve by store type, just as scaffolding does, using the registered provider source.
        if (_typeMappingSource.FindMapping(storeType) is null)
        {
            throw new NotSupportedException(
                $"Dameng table or view '{table}' column '{column}' has unsupported store type '{storeType}'. "
                + "Reverse engineering cannot preserve this column; exclude the table or view.");
        }
    }

    // A NEXTVAL default becomes a sequence strategy only when the referenced sequence is read
    // from the catalog with its real facets; otherwise the model would scaffold an EF sequence
    // with invented default facets and recreate a different sequence on migration. Cross-schema
    // or unreadable references remain explicit external dependencies via their raw SQL.
    private static void ResolveSequenceDefaults(
        DbConnection connection,
        string schema,
        DatabaseModel databaseModel,
        List<PendingSequenceDefault> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var facets = LoadSequenceFacets(
            connection,
            schema,
            pending
                .Where(
                    entry => entry.SequenceSchema is null
                        || string.Equals(entry.SequenceSchema, schema, StringComparison.Ordinal))
                .Select(entry => entry.SequenceName));

        var addedToModel = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in pending)
        {
            if (entry.SequenceSchema is not null
                && !string.Equals(entry.SequenceSchema, schema, StringComparison.Ordinal))
            {
                entry.Column.DefaultValueSql = entry.RawDefault;
                continue;
            }

            if (!facets.TryGetValue(entry.SequenceName, out var sequence))
            {
                entry.Column.DefaultValueSql = entry.RawDefault;
                continue;
            }

            entry.Column[DamengAnnotationNames.ValueGenerationStrategy]
                = DamengValueGenerationStrategy.Sequence;
            entry.Column[DamengAnnotationNames.SequenceName] = entry.SequenceName;
            entry.Column[DamengAnnotationNames.SequenceSchema] = entry.SequenceSchema;
            entry.Column.ValueGenerated = ValueGenerated.OnAdd;

            if (addedToModel.Add(sequence.Name))
            {
                databaseModel.Sequences.Add(sequence);
            }
        }
    }

    private static Dictionary<string, DatabaseSequence> LoadSequenceFacets(
        DbConnection connection,
        string schema,
        IEnumerable<string> sequenceNames)
    {
        var facets = new Dictionary<string, DatabaseSequence>(StringComparer.Ordinal);
        foreach (var command in CreateSequenceCatalogCommands(connection, schema, sequenceNames))
        {
            using (command)
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var sequence = ReadSequenceRecord(reader, schema);
                    facets.Add(sequence.Name, sequence);
                }
            }
        }

        return facets;
    }

    // Keep both the IN list and the total bind count bounded (500 names plus schema).
    // A command is created lazily; the consumer owns and disposes each yielded command.
    internal static IEnumerable<DbCommand> CreateSequenceCatalogCommands(
        DbConnection connection,
        string schema,
        IEnumerable<string> sequenceNames)
    {
        foreach (var names in sequenceNames.Distinct(StringComparer.Ordinal).Chunk(500))
        {
            var placeholders = string.Join(", ", Enumerable.Range(0, names.Length).Select(index => $":seq{index}"));
            var command = CreateCommand(
                connection,
                "SELECT SEQUENCE_NAME, INCREMENT_BY, MIN_VALUE, MAX_VALUE, CYCLE_FLAG, LAST_NUMBER, CACHE_SIZE, ORDER_FLAG"
                + " FROM ALL_SEQUENCES WHERE SEQUENCE_OWNER = :schema"
                + $" AND SEQUENCE_NAME IN ({placeholders})");
            AddParameter(command, "schema", schema);
            for (var index = 0; index < names.Length; index++)
            {
                AddParameter(command, $"seq{index}", names[index]);
            }

            yield return command;
        }
    }

    internal static DatabaseSequence ReadSequenceRecord(DbDataReader reader, string schema)
    {
        var name = reader.GetString(0);
        // LAST_NUMBER is the catalog continuation point. Never replace an unrepresentable
        // local sequence with a raw NEXTVAL default: that would omit its CREATE SEQUENCE.
        if (!TryReadInt64Facet(reader, 1, out var increment)
            || increment is < int.MinValue or > int.MaxValue or 0
            || !TryReadInt64Facet(reader, 2, out var minValue)
            || !TryReadInt64Facet(reader, 3, out var maxValue)
            || !TryReadInt64Facet(reader, 5, out var startValue))
        {
            throw new NotSupportedException(
                $"Dameng local sequence '{name}' has facets that EF cannot represent exactly "
                + "(nonzero Int32 increment and Int64 bounds/start required). Exclude the referencing table.");
        }

        var cyclic = reader.GetValue(4) as string;
        if (cyclic is not ("Y" or "N"))
        {
            throw new NotSupportedException($"Dameng local sequence '{name}' has an unknown cycle flag.");
        }

        if (!TryReadInt64Facet(reader, 6, out var cacheSize) || cacheSize != 0
            || reader.GetValue(7) as string != "N")
        {
            throw new NotSupportedException(
                $"Dameng local sequence '{name}' has unsupported CACHE_SIZE or ORDER_FLAG. "
                + "Reverse engineering supports only NOCACHE NOORDER sequences; exclude the referencing table.");
        }

        return new DatabaseSequence
        {
            Name = name,
            Schema = schema,
            IncrementBy = (int)increment,
            MinValue = minValue,
            MaxValue = maxValue,
            IsCyclic = cyclic == "Y",
            StartValue = startValue
        };
    }

    private static bool TryReadInt64Facet(DbDataReader reader, int ordinal, out long value)
    {
        value = 0L;
        try
        {
            var raw = reader.GetValue(ordinal);
            if (raw is decimal decimalValue)
            {
                if (decimalValue < long.MinValue || decimalValue > long.MaxValue
                    || decimal.Truncate(decimalValue) != decimalValue)
                {
                    return false;
                }

                value = (long)decimalValue;
                return true;
            }

            // Approximate floating-point values cannot prove exact integer catalog facets.
            if (raw is null or DBNull or float or double or bool or char)
            {
                return false;
            }

            value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception error) when (error is OverflowException or FormatException or InvalidCastException)
        {
            return false;
        }
    }

    private static void LoadIdentityAnnotations(
        DbConnection connection,
        string schema,
        Dictionary<string, DatabaseTable> tables,
        Dictionary<DatabaseTable, Dictionary<string, DatabaseColumn>> columnLookup)
    {
        var identityColumns = new List<(string Table, string Column)>();
        using (var command = CreateCommand(
            connection,
            """
            SELECT O.NAME AS TABLE_NAME, C.NAME AS COLUMN_NAME, O.INFO6
            FROM SYS.SYSCOLUMNS C
            INNER JOIN SYS.SYSOBJECTS O ON C.ID = O.ID
            INNER JOIN SYS.SYSOBJECTS S ON O.SCHID = S.ID
            WHERE S.NAME = :schema
              AND O.TYPE$ = 'SCHOBJ'
              AND O.SUBTYPE$ = 'UTAB'
              AND (C.INFO2 & 1) = 1
            """))
        {
            AddParameter(command, "schema", schema);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var table = reader.GetString(0);
                if (tables.ContainsKey(table))
                {
                    var column = reader.GetString(1);
                    ValidateIdentityType(table, column, reader.GetValue(2) as byte[]);
                    identityColumns.Add((table, column));
                }
            }
        }

        var seedIncrementByTable = new Dictionary<string, (long Seed, int Increment)>(StringComparer.Ordinal);
        foreach (var (tableName, columnName) in identityColumns)
        {
            if (!tables.TryGetValue(tableName, out var table))
            {
                continue;
            }

            if (!columnLookup[table].TryGetValue(columnName, out var column))
            {
                continue;
            }

            if (!seedIncrementByTable.TryGetValue(tableName, out var seedIncrement))
            {
                seedIncrement = GetIdentitySeedIncrement(connection, schema, tableName);
                seedIncrementByTable[tableName] = seedIncrement;
            }

            column[DamengAnnotationNames.ValueGenerationStrategy]
                = DamengValueGenerationStrategy.IdentityColumn;
            column[DamengAnnotationNames.IdentitySeed] = seedIncrement.Seed;
            column[DamengAnnotationNames.IdentityIncrement] = seedIncrement.Increment;
            column.ValueGenerated = ValueGenerated.OnAdd;
        }
    }

    internal static void ValidateIdentityType(string table, string column, byte[]? info6)
    {
        // SYSOBJECTS.INFO6 bytes 25-26 distinguish IDENTITY (1) from AUTO_INCREMENT (2).
        // SYSCOLUMNS.INFO2 bit 0 marks both, so it cannot determine the generation strategy.
        // Only the complete native IDENTITY marker is supported; never guess on short data.
        if (info6 is not { Length: >= 26 } || info6[24] != 1 || info6[25] != 0)
        {
            throw new NotSupportedException(
                $"Dameng table '{table}' column '{column}' has AUTO_INCREMENT or an unknown automatic-generation type. "
                + "Reverse engineering supports only native IDENTITY; exclude this table.");
        }
    }

    private static (long Seed, int Increment) GetIdentitySeedIncrement(
        DbConnection connection,
        string schema,
        string tableName)
    {
        // IDENT_SEED/IDENT_INCR take a name string and split it themselves: delimit each
        // component independently so legal names containing dots still resolve ('a.b' alone
        // is rejected by the server as too many name prefixes), then escape the whole
        // literal. A catalog-confirmed identity column whose facets cannot be read is an
        // error, not a (1,1) default.
        var qualifiedName = "\""
            + schema.Replace("\"", "\"\"", StringComparison.Ordinal)
            + "\".\""
            + tableName.Replace("\"", "\"\"", StringComparison.Ordinal)
            + "\"";
        var literal = "'" + qualifiedName.Replace("'", "''", StringComparison.Ordinal) + "'";
        using var command = CreateCommand(
            connection,
            $"SELECT IDENT_SEED({literal}), IDENT_INCR({literal}) FROM dual");

        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            throw new InvalidOperationException(
                $"Dameng did not return identity facets for '{qualifiedName}' "
                + "even though the catalog marks one of its columns as IDENTITY.");
        }

        return (
            Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture));
    }

    private static void ValidateUnsupportedTableStructures(
        DbConnection connection,
        string schema,
        Dictionary<string, DatabaseTable> tables)
    {
        // Filter by the target object's owner, not the trigger's owner: a trigger can
        // belong to another schema. Disabled triggers still have semantics to preserve.
        using (var command = CreateCommand(connection,
            "SELECT TABLE_NAME, TRIGGER_NAME FROM ALL_TRIGGERS "
            + "WHERE TABLE_OWNER = :schema AND TABLE_NAME IS NOT NULL"))
        {
            AddParameter(command, "schema", schema);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (tables.ContainsKey(reader.GetString(0)))
                {
                    throw new NotSupportedException(
                        $"Dameng table or view '{reader.GetString(0)}' has trigger '{reader.GetString(1)}'. "
                        + "Reverse engineering cannot preserve trigger definitions or state; exclude this object.");
                }
            }
        }

        // Native SYSCONS excludes NOT NULL constraints. This avoids mistaking a user CHECK
        // (including an explicit CHECK (... IS NOT NULL)) for column nullability metadata.
        using (var command = CreateCommand(connection,
            "SELECT T.NAME, C.NAME FROM SYS.SYSCONS K "
            + "INNER JOIN SYS.SYSOBJECTS C ON C.ID = K.ID "
            + "INNER JOIN SYS.SYSOBJECTS T ON T.ID = K.TABLEID "
            + "INNER JOIN SYS.SYSOBJECTS S ON S.ID = T.SCHID "
            + "WHERE S.NAME = :schema AND K.TYPE$ = 'C'"))
        {
            AddParameter(command, "schema", schema);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (tables.ContainsKey(reader.GetString(0)))
                {
                    throw new NotSupportedException(
                        $"Dameng table '{reader.GetString(0)}' contains CHECK constraint '{reader.GetString(1)}'. "
                        + "Reverse engineering cannot preserve CHECK constraints; exclude this table.");
                }
            }
        }

        // SYSCOLINFOS.INFO1 marks virtual columns (bit 0), DEFAULT ON NULL (bit 4) and
        // ON UPDATE (bit 6). ALL_TAB_COLUMNS alone cannot preserve these generation rules.
        using var columns = CreateCommand(connection,
            "SELECT T.NAME, C.NAME, I.INFO1 FROM SYS.SYSCOLINFOS I "
            + "INNER JOIN SYS.SYSOBJECTS T ON T.ID = I.ID "
            + "INNER JOIN SYS.SYSOBJECTS S ON S.ID = T.SCHID "
            + "INNER JOIN SYS.SYSCOLUMNS C ON C.ID = I.ID AND C.COLID = I.COLID "
            + "WHERE S.NAME = :schema AND T.TYPE$ = 'SCHOBJ' AND T.SUBTYPE$ = 'UTAB'");
        AddParameter(columns, "schema", schema);
        using var columnReader = columns.ExecuteReader();
        while (columnReader.Read())
        {
            if (tables.ContainsKey(columnReader.GetString(0)))
            {
                ValidateColumnGenerationFlags(
                    columnReader.GetString(0), columnReader.GetString(1),
                    Convert.ToInt64(columnReader.GetValue(2), CultureInfo.InvariantCulture));
            }
        }
    }

    internal static bool IsVirtualColumnFlags(long flags)
        => (flags & 1L) != 0;

    internal static void ValidateColumnGenerationFlags(string table, string column, long flags)
    {
        var unsupported = IsVirtualColumnFlags(flags) ? "virtual computed column"
            : (flags & 16L) != 0 ? "DEFAULT ON NULL"
            : (flags & 64L) != 0 ? "ON UPDATE"
            : null;
        if (unsupported is not null)
        {
            throw new NotSupportedException(
                $"Dameng table '{table}' contains {unsupported} on column '{column}'. "
                + "Reverse engineering cannot preserve this generation behavior; exclude this table.");
        }
    }

    private static void ValidateConstraintStates(
        DbConnection connection,
        string schema,
        Dictionary<string, DatabaseTable> tables)
    {
        using var command = CreateCommand(
            connection,
            "SELECT TABLE_NAME, CONSTRAINT_NAME, STATUS, DEFERRABLE, DEFERRED, VALIDATED FROM ALL_CONSTRAINTS "
            + "WHERE OWNER = :schema AND CONSTRAINT_TYPE IN ('P', 'U', 'R')");
        AddParameter(command, "schema", schema);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var table = reader.GetString(0);
            if (tables.ContainsKey(table))
            {
                ValidateConstraintState(table, reader.GetString(1), GetNullableString(reader, 2),
                    GetNullableString(reader, 3), GetNullableString(reader, 4), GetNullableString(reader, 5));
            }
        }
    }

    internal static void ValidateConstraintState(
        string table, string constraint, string? status, string? deferrable, string? deferred, string? validated)
    {
        if (!string.Equals(status, "ENABLED", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Dameng constraint '{constraint}' on table '{table}' has unsupported state '{status ?? "NULL"}'. "
                + "Reverse engineering cannot preserve disabled or unknown constraint states. "
                + "Exclude this table or explicitly enable the constraint before scaffolding.");
        }

        if (deferrable != "NOT DEFERRABLE" || deferred != "IMMEDIATE" || validated != "VALIDATED")
        {
            throw new NotSupportedException(
                $"Dameng constraint '{constraint}' on table '{table}' has unsupported deferral or validation state "
                + $"('{deferrable ?? "NULL"}', '{deferred ?? "NULL"}', '{validated ?? "NULL"}'). "
                + "Reverse engineering requires NOT DEFERRABLE, IMMEDIATE and VALIDATED constraints; exclude this table.");
        }
    }

    private static void LoadConstraints(
        DbConnection connection,
        string schema,
        Dictionary<string, DatabaseTable> tables,
        Dictionary<DatabaseTable, Dictionary<string, DatabaseColumn>> columnLookup)
    {
        using var command = CreateCommand(
            connection,
            """
            SELECT C.CONSTRAINT_NAME, C.CONSTRAINT_TYPE, C.TABLE_NAME, CC.COLUMN_NAME, CC.POSITION,
                   I.INDEX_TYPE
            FROM ALL_CONSTRAINTS C
            INNER JOIN ALL_CONS_COLUMNS CC
                ON CC.OWNER = C.OWNER
                AND CC.CONSTRAINT_NAME = C.CONSTRAINT_NAME
                AND CC.TABLE_NAME = C.TABLE_NAME
            LEFT JOIN ALL_INDEXES I
                ON I.OWNER = C.OWNER AND I.TABLE_NAME = C.TABLE_NAME AND I.INDEX_NAME = C.INDEX_NAME
            WHERE C.OWNER = :schema
              AND C.CONSTRAINT_TYPE IN ('P', 'U')
            ORDER BY C.TABLE_NAME, C.CONSTRAINT_NAME, CC.POSITION
            """);
        AddParameter(command, "schema", schema);

        var constraints = new List<(
            string Name,
            string Type,
            DatabaseTable Table,
            string? IndexType,
            List<string> Columns)>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var tableName = reader.GetString(2);
            if (!tables.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var constraintName = reader.GetString(0);
            var constraintType = reader.GetString(1);
            var columnName = reader.GetString(3);

            var constraint = constraints.LastOrDefault()
                is { } last
                && string.Equals(last.Name, constraintName, StringComparison.Ordinal)
                && string.Equals(last.Type, constraintType, StringComparison.Ordinal)
                && ReferenceEquals(last.Table, table)
                    ? last
                    : default;
            if (constraint == default)
            {
                constraint = (constraintName, constraintType, table, GetNullableString(reader, 5), []);
                constraints.Add(constraint);
            }

            constraint.Columns.Add(columnName);
        }

        foreach (var (name, type, table, indexType, columns) in constraints)
        {
            if (type == "P")
            {
                var primaryKey = new DatabasePrimaryKey
                {
                    Table = table,
                    Name = name
                };
                primaryKey[DamengAnnotationNames.IsClustered] = ReadPrimaryKeyClustering(indexType);
                AddColumnsByName(columnLookup[table], columns, primaryKey.Columns);
                table.PrimaryKey = primaryKey;
            }
            else
            {
                if (!string.Equals(indexType, "NORMAL", StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"Dameng unique constraint '{name}' has unsupported backing index type '{indexType ?? "NULL"}'. "
                        + "Reverse engineering cannot preserve this index shape; exclude this table.");
                }

                var uniqueConstraint = new DatabaseUniqueConstraint
                {
                    Table = table,
                    Name = name
                };
                AddColumnsByName(columnLookup[table], columns, uniqueConstraint.Columns);
                table.UniqueConstraints.Add(uniqueConstraint);
            }
        }
    }

    internal static bool ReadPrimaryKeyClustering(string? indexType)
        => indexType switch
        {
            "CLUSTER" => true,
            "NORMAL" => false,
            _ => throw new NotSupportedException(
                $"Cannot preserve Dameng primary-key clustering for backing index type '{indexType ?? "NULL"}'.")
        };

    private static void AddColumnsByName(
        Dictionary<string, DatabaseColumn> columns,
        IEnumerable<string> columnNames,
        IList<DatabaseColumn> target)
    {
        foreach (var columnName in columnNames)
        {
            if (columns.TryGetValue(columnName, out var column))
            {
                target.Add(column);
            }
        }
    }

    private static void LoadIndexes(
        DbConnection connection,
        string schema,
        Dictionary<string, DatabaseTable> tables,
        Dictionary<DatabaseTable, Dictionary<string, DatabaseColumn>> columnLookup)
    {
        // An implicit row-storage index is a system index (FLAG bit 0). An explicit
        // clustered index outside the PK would otherwise disappear from the model.
        using (var clusterCommand = CreateCommand(connection,
            "SELECT I.TABLE_NAME, I.INDEX_NAME, X.FLAG FROM ALL_INDEXES I "
            + "INNER JOIN SYS.SYSOBJECTS S ON S.NAME = I.OWNER AND S.TYPE$ = 'SCH' "
            + "INNER JOIN SYS.SYSOBJECTS O ON O.SCHID = S.ID AND O.NAME = I.INDEX_NAME "
            + "INNER JOIN SYS.SYSINDEXES X ON X.ID = O.ID "
            + "WHERE I.OWNER = :schema AND I.INDEX_TYPE = 'CLUSTER' "
            + "AND NOT EXISTS (SELECT 1 FROM ALL_CONSTRAINTS C WHERE C.OWNER = I.OWNER "
            + "AND C.TABLE_NAME = I.TABLE_NAME AND C.INDEX_NAME = I.INDEX_NAME AND C.CONSTRAINT_TYPE = 'P')"))
        {
            AddParameter(clusterCommand, "schema", schema);
            using var reader = clusterCommand.ExecuteReader();
            while (reader.Read())
            {
                if (tables.ContainsKey(reader.GetString(0))
                    && (Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture) & 1L) == 0)
                {
                    throw new NotSupportedException(
                        $"Dameng table '{reader.GetString(0)}' has clustered index '{reader.GetString(1)}' outside its primary key. "
                        + "Reverse engineering cannot preserve this index shape; exclude this table.");
                }
            }
        }

        var skippedIndexNames = new HashSet<string>(StringComparer.Ordinal);
        using (var command = CreateCommand(
            connection,
            "SELECT I.INDEX_NAME, I.TABLE_NAME, I.INDEX_TYPE, K.TYPE$ "
            + "FROM ALL_INDEXES I "
            + "LEFT JOIN SYS.SYSOBJECTS S ON S.NAME = I.OWNER AND S.TYPE$ = 'SCH' "
            + "LEFT JOIN SYS.SYSOBJECTS O ON O.SCHID = S.ID AND O.NAME = I.INDEX_NAME "
            + "AND O.TYPE$ = 'TABOBJ' AND O.SUBTYPE$ = 'INDEX' "
            + "LEFT JOIN SYS.SYSCONS K ON K.INDEXID = O.ID AND K.TABLEID = O.PID AND K.TYPE$ IN ('P', 'U', 'F') "
            + "WHERE I.OWNER = :schema"))
        {
            AddParameter(command, "schema", schema);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var table = reader.GetString(1);
                if (!tables.ContainsKey(table))
                {
                    continue;
                }

                // Inspect headers before joining columns: specialized indexes with no
                // ALL_IND_COLUMNS rows must not disappear silently either. Native SYSCONS
                // supplies FK index ownership that ALL_CONSTRAINTS.INDEX_NAME omits.
                var readColumns = ShouldReadIndexColumns(
                    table, name, GetNullableString(reader, 2), GetNullableString(reader, 3));
                if (!readColumns)
                {
                    skippedIndexNames.Add(name);
                }
            }
        }

        using var indexCommand = CreateCommand(
            connection,
            """
            SELECT I.INDEX_NAME, I.TABLE_NAME, I.UNIQUENESS, IC.COLUMN_NAME, IC.COLUMN_POSITION, IC.DESCEND
            FROM ALL_INDEXES I
            INNER JOIN ALL_IND_COLUMNS IC
                ON IC.INDEX_OWNER = I.OWNER
                AND IC.INDEX_NAME = I.INDEX_NAME
                AND IC.TABLE_NAME = I.TABLE_NAME
            WHERE I.OWNER = :schema
              AND I.INDEX_TYPE <> 'CLUSTER'
            ORDER BY I.TABLE_NAME, I.INDEX_NAME, IC.COLUMN_POSITION
            """);
        AddParameter(indexCommand, "schema", schema);

        var indexes = new List<(
            string Name,
            DatabaseTable Table,
            bool IsUnique,
            List<string> Columns,
            List<bool> Descending)>();
        using var indexReader = indexCommand.ExecuteReader();
        while (indexReader.Read())
        {
            var indexName = indexReader.GetString(0);
            if (skippedIndexNames.Contains(indexName))
            {
                continue;
            }

            var tableName = indexReader.GetString(1);
            if (!tables.TryGetValue(tableName, out var table))
            {
                continue;
            }

            var isUnique = string.Equals(indexReader.GetString(2), "UNIQUE", StringComparison.Ordinal);
            var columnName = indexReader.GetString(3);

            // Expression/function-based index rows report the base column name with
            // COLUMN_POSITION -1 (the real expression lives in ALL_IND_EXPRESSIONS) and an
            // unreliable DESCEND flag, so they cannot be scaffolded as a column index.
            ValidateIndexColumnPosition(tableName, indexName, GetNullableInt64(indexReader, 4));
            var descending = string.Equals(indexReader.GetString(5), "DESC", StringComparison.Ordinal);

            var index = indexes.LastOrDefault()
                is { } last
                && string.Equals(last.Name, indexName, StringComparison.Ordinal)
                && ReferenceEquals(last.Table, table)
                    ? last
                    : default;
            if (index == default)
            {
                index = (indexName, table, isUnique, [], []);
                indexes.Add(index);
            }

            index.Columns.Add(columnName);
            index.Descending.Add(descending);
        }

        foreach (var (name, table, isUnique, columns, descending) in indexes)
        {
            var databaseIndex = new DatabaseIndex
            {
                Table = table,
                Name = name,
                IsUnique = isUnique
            };
            AddColumnsByName(columnLookup[table], columns, databaseIndex.Columns);
            if (databaseIndex.Columns.Count != columns.Count)
            {
                continue;
            }

            foreach (var isDescending in descending)
            {
                databaseIndex.IsDescending.Add(isDescending);
            }

            table.Indexes.Add(databaseIndex);
        }
    }

    internal static bool ShouldReadIndexColumns(string table, string index, string? type, string? constraintType = null)
        => type switch
        {
            "NORMAL" => constraintType is not ("P" or "U"),
            // Dameng represents a foreign key without a physical backing index as VIRTUAL.
            // A physical NORMAL FK index retains its own definition; a VIRTUAL one does not.
            "VIRTUAL" when constraintType == "F" => false,
            // Clustering is validated separately. Expression and specialized indexes cannot
            // be represented and must reject the selected table, never silently disappear.
            "CLUSTER" => false,
            _ => throw new NotSupportedException(
                $"Dameng table '{table}' has index '{index}' with unsupported type '{type ?? "NULL"}'. "
                + "Reverse engineering only models NORMAL standalone indexes; exclude this table.")
        };

    internal static void ValidateIndexColumnPosition(string table, string index, long? position)
    {
        if (position is null or <= 0)
        {
            throw new NotSupportedException(
                $"Dameng table '{table}' has expression index '{index}' or an unknown index column position. "
                + "Reverse engineering cannot preserve this index definition; exclude this table.");
        }
    }

    private static void LoadForeignKeys(
        DbConnection connection,
        string schema,
        Dictionary<string, DatabaseTable> tables,
        Dictionary<DatabaseTable, Dictionary<string, DatabaseColumn>> columnLookup)
    {
        using var command = CreateCommand(
            connection,
            """
            SELECT C.CONSTRAINT_NAME, C.TABLE_NAME, P.TABLE_NAME AS PRINCIPAL_TABLE_NAME,
                   P.OWNER AS PRINCIPAL_OWNER,
                   CC.COLUMN_NAME, PC.COLUMN_NAME AS PRINCIPAL_COLUMN_NAME, C.DELETE_RULE
            FROM ALL_CONSTRAINTS C
            INNER JOIN ALL_CONSTRAINTS P
                ON P.OWNER = C.R_OWNER AND P.CONSTRAINT_NAME = C.R_CONSTRAINT_NAME
            INNER JOIN ALL_CONS_COLUMNS CC
                ON CC.OWNER = C.OWNER
                AND CC.CONSTRAINT_NAME = C.CONSTRAINT_NAME
                AND CC.TABLE_NAME = C.TABLE_NAME
            INNER JOIN ALL_CONS_COLUMNS PC
                ON PC.OWNER = P.OWNER
                AND PC.CONSTRAINT_NAME = P.CONSTRAINT_NAME
                AND PC.TABLE_NAME = P.TABLE_NAME
                AND PC.POSITION = CC.POSITION
            WHERE C.OWNER = :schema
              AND C.CONSTRAINT_TYPE = 'R'
            ORDER BY C.TABLE_NAME, C.CONSTRAINT_NAME, CC.POSITION
            """);
        AddParameter(command, "schema", schema);

        var foreignKeys = new List<(
            string Name,
            DatabaseTable Table,
            DatabaseTable PrincipalTable,
            ReferentialAction? OnDelete,
            List<string> Columns,
            List<string> PrincipalColumns)>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var tableName = reader.GetString(1);
            var principalTableName = reader.GetString(2);
            var principalOwner = reader.GetString(3);
            if (!string.Equals(principalOwner, schema, StringComparison.Ordinal)
                || !tables.TryGetValue(tableName, out var table)
                || !tables.TryGetValue(principalTableName, out var principalTable))
            {
                // Cross-schema principals are outside the current-schema scope.
                continue;
            }

            var constraintName = reader.GetString(0);
            var onDelete = MapDeleteRule(GetNullableString(reader, 6));

            var foreignKey = foreignKeys.LastOrDefault()
                is { } last
                && string.Equals(last.Name, constraintName, StringComparison.Ordinal)
                && ReferenceEquals(last.Table, table)
                    ? last
                    : default;
            if (foreignKey == default)
            {
                foreignKey = (constraintName, table, principalTable, onDelete, [], []);
                foreignKeys.Add(foreignKey);
            }

            foreignKey.Columns.Add(reader.GetString(4));
            foreignKey.PrincipalColumns.Add(reader.GetString(5));
        }

        foreach (var (name, table, principalTable, onDelete, columns, principalColumns) in foreignKeys)
        {
            var databaseForeignKey = new DatabaseForeignKey
            {
                Table = table,
                PrincipalTable = principalTable,
                Name = name,
                OnDelete = onDelete
            };
            AddColumnsByName(columnLookup[table], columns, databaseForeignKey.Columns);
            AddColumnsByName(columnLookup[principalTable], principalColumns, databaseForeignKey.PrincipalColumns);

            // A partially resolved key would pair remaining columns by position and point the
            // relationship at the wrong columns; drop it instead.
            if (databaseForeignKey.Columns.Count != columns.Count
                || databaseForeignKey.PrincipalColumns.Count != principalColumns.Count)
            {
                continue;
            }

            table.ForeignKeys.Add(databaseForeignKey);
        }
    }

    private static ReferentialAction? MapDeleteRule(string? deleteRule)
        => deleteRule?.ToUpperInvariant() switch
        {
            "CASCADE" => ReferentialAction.Cascade,
            "SET NULL" => ReferentialAction.SetNull,
            "NO ACTION" => ReferentialAction.NoAction,
            "RESTRICT" => ReferentialAction.Restrict,
            _ => null
        };

    private static void LoadComments(
        DbConnection connection,
        string schema,
        Dictionary<string, DatabaseTable> tables,
        Dictionary<DatabaseTable, Dictionary<string, DatabaseColumn>> columnLookup)
    {
        using (var command = CreateCommand(
            connection,
            """
            SELECT TABLE_NAME, COMMENTS
            FROM ALL_TAB_COMMENTS
            WHERE OWNER = :schema AND TABLE_TYPE = 'TABLE'
            """))
        {
            AddParameter(command, "schema", schema);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (tables.TryGetValue(reader.GetString(0), out var table))
                {
                    table.Comment = GetNullableString(reader, 1);
                }
            }
        }

        using (var command = CreateCommand(
            connection,
            """
            SELECT TABLE_NAME, COLUMN_NAME, COMMENTS
            FROM ALL_COL_COMMENTS
            WHERE OWNER = :schema
            """))
        {
            AddParameter(command, "schema", schema);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!tables.TryGetValue(reader.GetString(0), out var table))
                {
                    continue;
                }

                var columnName = reader.GetString(1);
                var comment = GetNullableString(reader, 2);
                if (comment is null)
                {
                    continue;
                }

                if (columnLookup[table].TryGetValue(columnName, out var column))
                {
                    column.Comment = comment;
                }
            }
        }
    }

    internal static string BuildStoreType(
        string dataType,
        long? dataLength,
        long? dataPrecision,
        long? dataScale,
        long? charLength,
        string? charUsed)
    {
        var normalizedType = dataType.Trim().ToUpperInvariant();
        switch (normalizedType)
        {
            case "CHAR":
            case "VARCHAR":
            case "VARCHAR2":
            case "NCHAR":
            case "NVARCHAR":
            case "NVARCHAR2":
                var textLength = string.Equals(charUsed, "C", StringComparison.Ordinal)
                    ? charLength ?? dataLength
                    : dataLength ?? charLength;
                if (textLength is null or <= 0)
                {
                    return normalizedType;
                }

                // Character-declared lengths must keep the CHAR qualifier; otherwise a byte-sized
                // instance truncates multi-byte text. NVARCHAR2/NCHAR are always character-based.
                var isCharacterDeclared = string.Equals(charUsed, "C", StringComparison.Ordinal);
                return isCharacterDeclared
                    && normalizedType is "CHAR" or "VARCHAR" or "VARCHAR2"
                        ? $"{normalizedType}({textLength.Value.ToString(CultureInfo.InvariantCulture)} CHAR)"
                        : $"{normalizedType}({textLength.Value.ToString(CultureInfo.InvariantCulture)})";

            case "DECIMAL":
            case "DEC":
            case "NUMERIC":
            case "NUMBER":
                return dataPrecision is null
                    ? normalizedType
                    : $"{normalizedType}({dataPrecision.Value.ToString(CultureInfo.InvariantCulture)},{(dataScale ?? 0L).ToString(CultureInfo.InvariantCulture)})";

            case "FLOAT":
                return dataPrecision is not null
                    ? $"FLOAT({dataPrecision.Value.ToString(CultureInfo.InvariantCulture)})"
                    : normalizedType;

            case "TIME":
            case "DATETIME":
            case "TIMESTAMP":
                // The catalog reports a concrete scale even for scale zero; the unqualified type
                // would pick up the server's default precision instead of the declared (0).
                return dataScale is not null
                    ? $"{normalizedType}({dataScale.Value.ToString(CultureInfo.InvariantCulture)})"
                    : normalizedType;

            case "DATETIME WITH TIME ZONE":
                return dataScale is not null
                    ? $"DATETIME({dataScale.Value.ToString(CultureInfo.InvariantCulture)}) WITH TIME ZONE"
                    : normalizedType;

            case "TIMESTAMP WITH TIME ZONE":
                return dataScale is not null
                    ? $"TIMESTAMP({dataScale.Value.ToString(CultureInfo.InvariantCulture)}) WITH TIME ZONE"
                    : normalizedType;

            case "TIMESTAMP WITH LOCAL TIME ZONE":
                // The catalog offsets this type's DATA_SCALE by 4096: unqualified reports 4102
                // (default 6), (0) reports 4096, (3) reports 4099.
                return dataScale is >= 4096
                    ? $"TIMESTAMP({(dataScale.Value - 4096).ToString(CultureInfo.InvariantCulture)}) WITH LOCAL TIME ZONE"
                    : normalizedType;

            case "INTERVAL DAY TO SECOND":
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"INTERVAL DAY({dataPrecision ?? 2L}) TO SECOND({dataScale ?? 6L})");

            case "INTERVAL YEAR TO MONTH":
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"INTERVAL YEAR({dataPrecision ?? 2L}) TO MONTH");

            case "BINARY":
            case "VARBINARY":
                return dataLength is > 0 and < int.MaxValue
                    ? $"{normalizedType}({dataLength.Value.ToString(CultureInfo.InvariantCulture)})"
                    : normalizedType;

            default:
                return normalizedType;
        }
    }

    internal static bool TryParseSequenceDefault(
        string defaultValueSql,
        out string sequenceName,
        out string? sequenceSchema)
    {
        var match = NextValDefaultPattern.Match(defaultValueSql);
        if (!match.Success)
        {
            sequenceName = string.Empty;
            sequenceSchema = null;
            return false;
        }

        sequenceName = NormalizeIdentifier(match.Groups["seq"].Value);
        sequenceSchema = match.Groups["schema"].Success
            ? NormalizeIdentifier(match.Groups["schema"].Value)
            : null;
        return true;
    }

    // A table filter entry is a qualified name whose components may be delimited; the schema
    // separator is the first dot outside a double-quoted component, so a legal name containing
    // a dot (APP."A.B" or "MY.SCHEMA".T) still resolves to the catalog's stored names.
    internal static (string? Schema, string Name) SplitQualifiedName(string entry)
    {
        var inQuotes = false;
        for (var index = 0; index < entry.Length; index++)
        {
            var current = entry[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < entry.Length && entry[index + 1] == '"')
                {
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
            }
            else if (current == '.' && !inQuotes)
            {
                return (
                    NormalizeIdentifier(entry[..index]),
                    NormalizeIdentifier(entry[(index + 1)..]));
            }
        }

        return (null, NormalizeIdentifier(entry));
    }

    internal static string NormalizeIdentifier(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : value.ToUpperInvariant();
    }

    private static DbCommand CreateCommand(DbConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static long? GetNullableInt64(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static string? GetNullableString(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
