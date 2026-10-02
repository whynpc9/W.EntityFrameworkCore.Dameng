using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Metadata.Internal;

namespace W.EntityFrameworkCore.Dameng.Migrations.Internal;

internal sealed class DamengMigrationsSqlGenerator : MigrationsSqlGenerator
{
    private const int MaxDynamicSqlLiteralUtf8Length = 32767;

    private bool _suppressTransaction;

    public DamengMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var commands = base.Generate(operations, model, options);

        if (!options.HasFlag(MigrationsSqlGenerationOptions.Idempotent))
        {
            return commands;
        }

        var builder = new MigrationCommandListBuilder(Dependencies);
        var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

        foreach (var command in commands)
        {
            // EF places this text inside a DMSQL IF block. Dynamic SQL is required both
            // for DDL and to avoid binding skipped DML against an old schema.
            var commandText = command.CommandText.TrimEnd();
            var commandLiteral = stringTypeMapping.GenerateSqlLiteral(commandText);
            if (Encoding.UTF8.GetByteCount(commandLiteral) > MaxDynamicSqlLiteralUtf8Length)
            {
                throw new NotSupportedException(
                    "A Dameng idempotent migration command exceeds the conservative 32767-byte "
                    + "dynamic SQL literal limit after escaping. Split the migration operation "
                    + "into smaller commands.");
            }

            builder
                .Append("EXECUTE IMMEDIATE ")
                .Append(commandLiteral)
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(command.TransactionSuppressed);
        }

        return builder.GetCommandList();
    }

    protected override void Generate(
        MigrationOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        var previousSuppressTransaction = _suppressTransaction;
        _suppressTransaction = operation is not (
            InsertDataOperation
            or UpdateDataOperation
            or DeleteDataOperation
            or SqlOperation);

        try
        {
            base.Generate(operation, model, builder);
        }
        finally
        {
            _suppressTransaction = previousSuppressTransaction;
        }
    }

    protected override void Generate(
        SqlOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        ThrowIfContainsDisqlBatchTerminator(operation.Sql);
        base.Generate(operation, model, builder);
    }

    protected override void Generate(
        AlterColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        if (IsIdentity(operation.OldColumn) && !IsIdentity(operation))
        {
            if (!IsIdentityRemovalOnly(operation))
            {
                throw new NotSupportedException(
                    "The Dameng provider supports removing IDENTITY only when all other column "
                    + "definitions and annotations remain unchanged. Split other changes into separate operations.");
            }

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" DROP IDENTITY")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
            return;
        }

        if (IsIdentity(operation) && !IsIdentity(operation.OldColumn))
        {
            throw new NotSupportedException(
                "The Dameng provider does not support adding or restoring IDENTITY on an existing column. "
                + "This change requires a reviewed migration plan that preserves existing data. "
                + "Dropping and recreating a column does not preserve existing data.");
        }

        if (IsIdentity(operation)
            && (GetIdentitySeed(operation) != GetIdentitySeed(operation.OldColumn)
                || GetIdentityIncrement(operation) != GetIdentityIncrement(operation.OldColumn)))
        {
            throw new NotSupportedException(
                "The Dameng provider does not support changing an identity column's seed or increment with ALTER COLUMN. "
                + "This change requires a reviewed migration plan that preserves existing data. "
                + "Dropping and recreating a column does not preserve existing data.");
        }

        if (operation.ComputedColumnSql != operation.OldColumn.ComputedColumnSql
            || operation.IsStored != operation.OldColumn.IsStored)
        {
            throw new NotSupportedException(
                "Changing a Dameng computed column requires dropping and recreating the column.");
        }

        if ((operation.DefaultValue is null
                && operation.DefaultValueSql is null
                && (operation.OldColumn.DefaultValue is not null
                    || operation.OldColumn.DefaultValueSql is not null))
            || (IsSequence(operation.OldColumn) && !IsSequence(operation)))
        {
            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" ALTER COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" DROP DEFAULT")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            EndStatement(builder);
        }

        builder
            .Append("ALTER TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" MODIFY ");

        ColumnDefinition(
            operation.Schema,
            operation.Table,
            operation.Name,
            operation,
            model,
            builder,
            includeIdentity: false);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    protected override void Generate(
        CreateIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        if (!string.IsNullOrWhiteSpace(operation.Filter))
        {
            throw new NotSupportedException("Dameng does not support filtered indexes.");
        }

        builder.Append("CREATE ");

        if (operation.IsUnique)
        {
            builder.Append("UNIQUE ");
        }

        builder
            .Append("INDEX ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" ON ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" (");

        GenerateIndexColumnList(operation, model, builder);
        builder.Append(")");

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    protected override void Generate(
        DropIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        builder
            .Append("DROP INDEX ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema));

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }

    protected override void Generate(
        EnsureSchemaOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder
            .Append("CREATE SCHEMA ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void Generate(
        InsertDataOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        if (!TargetsIdentityColumn(operation, model))
        {
            base.Generate(operation, model, builder, terminate);
            return;
        }

        var table = Dependencies.SqlGenerationHelper.DelimitIdentifier(
            operation.Table,
            operation.Schema);

        builder
            .Append("SET IDENTITY_INSERT ")
            .Append(table)
            .AppendLine(" ON;");
        EndStatement(builder);

        base.Generate(operation, model, builder, terminate: true);

        builder
            .Append("SET IDENTITY_INSERT ")
            .Append(table)
            .AppendLine(" OFF;");

        EndStatement(builder);
    }

    protected override void Generate(
        RenameColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder
            .Append("ALTER TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" RENAME COLUMN ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void Generate(
        RenameIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        builder
            .Append("ALTER INDEX ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .Append(" RENAME TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void Generate(
        RenameTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        ThrowIfCrossSchemaRename(
            operation.Schema,
            operation.NewSchema,
            operation.NewName,
            "table");

        builder
            .Append("ALTER TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .Append(" RENAME TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName!))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void Generate(
        RenameSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        ThrowIfCrossSchemaRename(
            operation.Schema,
            operation.NewSchema,
            operation.NewName,
            "sequence");

        builder
            .Append("ALTER SEQUENCE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .Append(" RENAME TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName!))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void Generate(
        CreateSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        var longTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(long));

        builder
            .Append("CREATE SEQUENCE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .Append(" START WITH ")
            .Append(longTypeMapping.GenerateSqlLiteral(operation.StartValue));

        SequenceOptions(operation.Schema, operation.Name, operation, model, builder, forAlter: false);

        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    protected override void Generate(
        RestartSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        if (operation.StartValue is null)
        {
            throw new NotSupportedException(
                "Dameng requires an explicit value when restarting a sequence.");
        }

        var longTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(long));

        builder
            .Append("ALTER SEQUENCE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema))
            .Append(" CURRENT VALUE ")
            .Append(longTypeMapping.GenerateSqlLiteral(operation.StartValue.Value))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        EndStatement(builder);
    }

    protected override void SequenceOptions(
        string? schema,
        string name,
        SequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool forAlter)
    {
        var intTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(int));
        var longTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(long));

        builder
            .Append(" INCREMENT BY ")
            .Append(intTypeMapping.GenerateSqlLiteral(operation.IncrementBy));

        if (operation.MinValue is { } minValue)
        {
            builder
                .Append(" MINVALUE ")
                .Append(longTypeMapping.GenerateSqlLiteral(minValue));
        }
        else if (forAlter)
        {
            builder.Append(" NOMINVALUE");
        }

        if (operation.MaxValue is { } maxValue)
        {
            builder
                .Append(" MAXVALUE ")
                .Append(longTypeMapping.GenerateSqlLiteral(maxValue));
        }
        else if (forAlter)
        {
            builder.Append(" NOMAXVALUE");
        }

        builder.Append(operation.IsCyclic ? " CYCLE" : " NOCYCLE");
    }

    protected override void ColumnDefinition(
        string? schema,
        string table,
        string name,
        ColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
        => ColumnDefinition(
            schema,
            table,
            name,
            operation,
            model,
            builder,
            includeIdentity: true);

    protected override void ComputedColumnDefinition(
        string? schema,
        string table,
        string name,
        ColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        if (operation.IsStored == true)
        {
            throw new NotSupportedException(
                "Dameng supports virtual computed columns, but not stored computed columns.");
        }

        builder
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
            .Append(" AS (")
            .Append(operation.ComputedColumnSql!)
            .Append(")");
    }

    protected override void EndStatement(
        MigrationCommandListBuilder builder,
        bool suppressTransaction = false)
        => base.EndStatement(builder, suppressTransaction || _suppressTransaction);

    private void ColumnDefinition(
        string? schema,
        string table,
        string name,
        ColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool includeIdentity)
    {
        if (operation.ComputedColumnSql is not null)
        {
            ComputedColumnDefinition(schema, table, name, operation, model, builder);
            return;
        }

        var columnType = operation.ColumnType
            ?? GetColumnType(schema, table, name, operation, model);
        var isIdentity = IsIdentity(operation);
        var defaultValueSql = operation.DefaultValueSql;

        if (isIdentity
            && (operation.DefaultValue is not null || operation.DefaultValueSql is not null))
        {
            throw new InvalidOperationException(
                $"The identity column '{name}' cannot also define a default value.");
        }

        if (IsSequence(operation))
        {
            var sequenceName = operation[DamengAnnotationNames.SequenceName] as string
                ?? throw new InvalidOperationException(
                    $"The sequence-backed column '{table}.{name}' is missing its Dameng sequence name.");
            var sequenceSchema = operation[DamengAnnotationNames.SequenceSchema] as string;

            defaultValueSql =
                Dependencies.SqlGenerationHelper.DelimitIdentifier(sequenceName, sequenceSchema)
                + ".NEXTVAL";
        }

        builder
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
            .Append(" ")
            .Append(columnType);

        if (operation.Collation is not null)
        {
            builder
                .Append(" COLLATE ")
                .Append(operation.Collation);
        }

        if (includeIdentity && isIdentity)
        {
            builder
                .Append(" IDENTITY(")
                .Append(GetIdentitySeed(operation).ToString(CultureInfo.InvariantCulture))
                .Append(",")
                .Append(GetIdentityIncrement(operation).ToString(CultureInfo.InvariantCulture))
                .Append(")");
        }

        builder.Append(operation.IsNullable ? " NULL" : " NOT NULL");
        DefaultValue(operation.DefaultValue, defaultValueSql, columnType, builder);
    }

    private static bool IsIdentity(ColumnOperation operation)
        => operation[DamengAnnotationNames.ValueGenerationStrategy] switch
        {
            DamengValueGenerationStrategy.IdentityColumn => true,
            string value => string.Equals(
                value,
                nameof(DamengValueGenerationStrategy.IdentityColumn),
                StringComparison.Ordinal),
            _ => false
        };

    private static bool IsIdentityRemovalOnly(AlterColumnOperation operation)
    {
        var old = operation.OldColumn;
        var strategy = operation[DamengAnnotationNames.ValueGenerationStrategy];
        if (strategy is not null
            && !Equals(strategy, DamengValueGenerationStrategy.None)
            && !Equals(strategy, nameof(DamengValueGenerationStrategy.None)))
        {
            return false;
        }

        // OldColumn's name/table/schema are not populated by EF's model differ.
        // Compare column facets and semantic annotations, excluding only the removed identity metadata.
        return operation.ClrType == old.ClrType
            && operation.ColumnType == old.ColumnType
            && operation.IsUnicode == old.IsUnicode
            && operation.IsFixedLength == old.IsFixedLength
            && operation.MaxLength == old.MaxLength
            && operation.Precision == old.Precision
            && operation.Scale == old.Scale
            && operation.IsRowVersion == old.IsRowVersion
            && operation.IsNullable == old.IsNullable
            && Equals(operation.DefaultValue, old.DefaultValue)
            && operation.DefaultValueSql == old.DefaultValueSql
            && operation.ComputedColumnSql == old.ComputedColumnSql
            && operation.IsStored == old.IsStored
            && operation.Comment == old.Comment
            && operation.Collation == old.Collation
            && OtherColumnAnnotations(operation).SequenceEqual(OtherColumnAnnotations(old));
    }

    private static IEnumerable<(string Name, object? Value)> OtherColumnAnnotations(ColumnOperation operation)
        => operation.GetAnnotations()
            .Where(annotation => annotation.Name is not (
                DamengAnnotationNames.ValueGenerationStrategy
                or DamengAnnotationNames.IdentitySeed
                or DamengAnnotationNames.IdentityIncrement))
            .OrderBy(annotation => annotation.Name, StringComparer.Ordinal)
            .Select(annotation => (annotation.Name, annotation.Value));

    private static bool IsSequence(ColumnOperation operation)
        => operation[DamengAnnotationNames.ValueGenerationStrategy] switch
        {
            DamengValueGenerationStrategy.Sequence => true,
            string value => string.Equals(
                value,
                nameof(DamengValueGenerationStrategy.Sequence),
                StringComparison.Ordinal),
            _ => false
        };

    private static long GetIdentitySeed(ColumnOperation operation)
        => Convert.ToInt64(
            operation[DamengAnnotationNames.IdentitySeed] ?? 1L,
            CultureInfo.InvariantCulture);

    private static int GetIdentityIncrement(ColumnOperation operation)
        => Convert.ToInt32(
            operation[DamengAnnotationNames.IdentityIncrement] ?? 1,
            CultureInfo.InvariantCulture);

    private static bool TargetsIdentityColumn(
        InsertDataOperation operation,
        IModel? model)
    {
        var table = model?.GetRelationalModel().FindTable(
            operation.Table,
            operation.Schema);
        if (table is null)
        {
            return false;
        }

        var storeObject = StoreObjectIdentifier.Table(
            operation.Table,
            operation.Schema);

        return operation.Columns.Any(
            columnName => table.FindColumn(columnName)?.PropertyMappings.Any(
                mapping => mapping.Property.GetDamengValueGenerationStrategy(storeObject)
                    == DamengValueGenerationStrategy.IdentityColumn) == true);
    }

    private static void ThrowIfCrossSchemaRename(
        string? schema,
        string? newSchema,
        string? newName,
        string objectType)
    {
        if (newName is null
            || (newSchema is not null
                && !string.Equals(schema, newSchema, StringComparison.Ordinal)))
        {
            throw new NotSupportedException(
                $"Dameng does not support moving a {objectType} between schemas with RENAME.");
        }
    }

    private static void ThrowIfContainsDisqlBatchTerminator(string commandText)
    {
        var inSingleQuotedLiteral = false;
        var inDelimitedIdentifier = false;
        var inBlockComment = false;

        foreach (var line in commandText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
        {
            if (!inSingleQuotedLiteral
                && !inDelimitedIdentifier
                && !inBlockComment
                && string.Equals(line.Trim(), "/", StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "A Dameng migration SQL operation cannot contain the DIsql '/' batch terminator. "
                    + "The terminator is a client-side script delimiter, not part of an ADO.NET command.");
            }

            for (var i = 0; i < line.Length; i++)
            {
                var current = line[i];
                var next = i + 1 < line.Length ? line[i + 1] : '\0';

                if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (inSingleQuotedLiteral)
                {
                    if (current == '\'' && next == '\'')
                    {
                        i++;
                    }
                    else if (current == '\'')
                    {
                        inSingleQuotedLiteral = false;
                    }

                    continue;
                }

                if (inDelimitedIdentifier)
                {
                    if (current == '"' && next == '"')
                    {
                        i++;
                    }
                    else if (current == '"')
                    {
                        inDelimitedIdentifier = false;
                    }

                    continue;
                }

                if (current == '-' && next == '-')
                {
                    break;
                }

                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                }
                else if (current == '\'')
                {
                    inSingleQuotedLiteral = true;
                }
                else if (current == '"')
                {
                    inDelimitedIdentifier = true;
                }
            }
        }
    }
}
