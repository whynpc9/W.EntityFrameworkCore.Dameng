using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using W.EntityFrameworkCore.Dameng.Infrastructure;

namespace W.EntityFrameworkCore.Dameng.Tests.TestUtilities;

// A complete, allow-listed catalog protocol. Unexpected commands fail rather than
// silently returning no rows. Only this test driver accepts the synthetic connection.
internal sealed class DamengCatalogFixture : DbConnection
{
    public string[] Tables { get; init; } = ["T"];
    public string[] Views { get; init; } = [];
    public string[] NativeColumns { get; init; } = ["Id", "Payload"];
    public string[] CompatibilityColumns { get; init; } = ["Id", "Payload"];
    public string? VirtualColumn { get; init; }
    public bool HasSequence { get; init; }
    public bool OmitExcludedCompatibilityColumns { get; init; }
    public bool HasPrimaryKey { get; init; }
    public List<string> Commands { get; } = [];
    private ConnectionState _state;
    [AllowNull] public override string ConnectionString { get; set; } = "";
    public override string Database => "SyntheticCatalog";
    public override string DataSource => "SyntheticCatalog";
    public override string ServerVersion => "Synthetic";
    public override ConnectionState State => _state;
    public override void Open() => _state = ConnectionState.Open;
    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => new Command(this);

    internal sealed class Driver : DamengDriver
    {
        public override string Name => "SyntheticCatalog";
        public override string ProviderName => "W.EntityFrameworkCore.Dameng";
        public override MethodInfo UseProviderMethod => typeof(DamengDbContextOptionsBuilderExtensions).GetMethods()
            .Single(method => method.Name == "UseDameng" && !method.IsGenericMethod
                && method.GetParameters()[1].ParameterType == typeof(string));
        public override DbConnection CreateConnection(string connectionString) => new DamengCatalogFixture();
        public override bool IsDriverConnection(DbConnection connection) => connection is DamengCatalogFixture;
        public override void SetParameterType(DbParameter parameter, DamengParameterType type) => throw new NotSupportedException();
    }

    private object?[][] Rows(string sql)
    {
        Commands.Add(sql);
        if (sql.StartsWith("SELECT T.TABLE_NAME, T.TEMPORARY", StringComparison.Ordinal))
            return Tables.Select(t => new object?[] { t, "N", "NO", 0L, 0L, 0L }).ToArray();
        if (sql.StartsWith("SELECT VIEW_NAME FROM ALL_VIEWS", StringComparison.Ordinal))
            return Views.Select(t => new object?[] { t }).ToArray();
        if (sql.StartsWith("SELECT SEQUENCE_NAME, INCREMENT_BY", StringComparison.Ordinal))
            return HasSequence ? [["S", 1L, 1L, 100L, "N", 1L, 0L, "N"]] : [];
        if (sql.StartsWith("SELECT T.NAME, C.NAME, COALESCE(I.INFO1", StringComparison.Ordinal))
            return Tables.SelectMany(t => NativeColumns.Select(c => new object?[] { t, c, c == VirtualColumn ? 1L : 0L, 0L, c == VirtualColumn ? "(1 + 1)" : null })).ToArray();
        if (sql.Contains("FROM ALL_TAB_COLS", StringComparison.Ordinal))
            return Tables.Concat(Views).Where(t => !OmitExcludedCompatibilityColumns || t != "Excluded").SelectMany(t => CompatibilityColumns.Select((c, i) => new object?[] { t, c, "INT", 4L, null, null, c == "Id" ? "N" : "Y", null, null, null, (long)i + 1, "NO" })).ToArray();
        if (sql.StartsWith("SELECT I.INDEX_NAME, I.TABLE_NAME, I.INDEX_TYPE, K.TYPE$", StringComparison.Ordinal))
            return Tables.Select(t => new object?[] { "SYS_STORAGE", t, "CLUSTER", null, 1L, 1L, 0L, "VALID", 0L, null }).ToArray();
        if (sql.Contains("SELECT C.CONSTRAINT_NAME, C.CONSTRAINT_TYPE", StringComparison.Ordinal))
            return HasPrimaryKey ? Tables.Select(t => new object?[] { "PK_T", "P", t, "Id", 1L, "NORMAL" }).ToArray() : [];
        if (sql.StartsWith("SELECT TABLE_NAME, TRIGGER_NAME FROM ALL_TRIGGERS", StringComparison.Ordinal)
            || sql.StartsWith("SELECT T.NAME, C.NAME FROM SYS.SYSCONS K", StringComparison.Ordinal)
            || sql.StartsWith("SELECT TABLE_NAME, CONSTRAINT_NAME, STATUS", StringComparison.Ordinal)
            || sql.Contains("SELECT O.NAME AS TABLE_NAME, C.NAME AS COLUMN_NAME, O.INFO6", StringComparison.Ordinal)
            || sql.StartsWith("SELECT I.TABLE_NAME, I.INDEX_NAME, X.FLAG", StringComparison.Ordinal)
            || sql.Contains("SELECT I.INDEX_NAME, I.TABLE_NAME, I.UNIQUENESS", StringComparison.Ordinal)
            || sql.Contains("SELECT C.CONSTRAINT_NAME, C.TABLE_NAME, P.TABLE_NAME AS PRINCIPAL_TABLE_NAME", StringComparison.Ordinal)
            || sql.Contains("FROM ALL_TAB_COMMENTS", StringComparison.Ordinal)
            || sql.Contains("FROM ALL_COL_COMMENTS", StringComparison.Ordinal)) return [];
        throw new InvalidOperationException("Unexpected synthetic catalog SQL: " + sql);
    }

    private sealed class Command(DamengCatalogFixture owner) : DbCommand
    {
        private readonly Parameters _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get => owner; set => throw new NotSupportedException(); }
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        public override void Cancel() => throw new NotSupportedException();
        public override void Prepare() => throw new NotSupportedException();
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object ExecuteScalar()
            => CommandText == "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM dual" ? "APP" : throw new InvalidOperationException(CommandText);
        protected override DbParameter CreateDbParameter() => new Parameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            var rows = owner.Rows(CommandText);
            var table = new DataTable();
            for (var i = 0; i < (rows.FirstOrDefault()?.Length ?? 0); i++) table.Columns.Add("C" + i, typeof(object));
            foreach (var row in rows) table.Rows.Add(row.Select(value => value ?? DBNull.Value).ToArray());
            return table.CreateDataReader();
        }
    }
    private sealed class Parameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = "";
        [AllowNull] public override string SourceColumn { get; set; } = "";
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }
    private sealed class Parameters : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => this;
        public override int Add(object value) { _items.Add((DbParameter)value); return Count - 1; }
        public override void AddRange(Array values) { foreach (var value in values) Add(value); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
    }
}
