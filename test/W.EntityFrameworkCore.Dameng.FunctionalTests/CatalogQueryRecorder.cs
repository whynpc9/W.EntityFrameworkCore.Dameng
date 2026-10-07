using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

// Records catalog commands without changing the driver's reader or connection lifetime.
internal sealed class CatalogQueryRecorder(DbConnection inner) : DbConnection
{
    internal sealed record Query(string Sql, (string Name, object Value)[] Parameters, bool ReturnsRows);
    public List<Query> Queries { get; } = [];
    [AllowNull]
    public override string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value; }
    public override string Database => inner.Database;
    public override string DataSource => inner.DataSource;
    public override string ServerVersion => inner.ServerVersion;
    public override ConnectionState State => inner.State;
    public override void Open() => inner.Open();
    public override void Close() => inner.Close();
    public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => inner.BeginTransaction(isolationLevel);
    protected override DbCommand CreateDbCommand() => new RecordingCommand(this, inner.CreateCommand());

    private sealed class RecordingCommand(CatalogQueryRecorder recorder, DbCommand innerCommand) : DbCommand
    {
        [AllowNull]
        public override string CommandText { get => innerCommand.CommandText; set => innerCommand.CommandText = value; }
        public override int CommandTimeout { get => innerCommand.CommandTimeout; set => innerCommand.CommandTimeout = value; }
        public override CommandType CommandType { get => innerCommand.CommandType; set => innerCommand.CommandType = value; }
        public override bool DesignTimeVisible { get => innerCommand.DesignTimeVisible; set => innerCommand.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => innerCommand.UpdatedRowSource; set => innerCommand.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => recorder; set => throw new NotSupportedException(); }
        protected override DbTransaction? DbTransaction { get => innerCommand.Transaction; set => innerCommand.Transaction = value; }
        protected override DbParameterCollection DbParameterCollection => innerCommand.Parameters;
        public override void Cancel() => innerCommand.Cancel();
        public override void Prepare() => innerCommand.Prepare();
        protected override DbParameter CreateDbParameter() => innerCommand.CreateParameter();
        public override int ExecuteNonQuery() => throw new NotSupportedException("The catalog recorder is read-only.");
        public override object? ExecuteScalar()
        {
            Record(false);
            return innerCommand.ExecuteScalar();
        }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Record(true);
            return innerCommand.ExecuteReader(behavior);
        }
        private void Record(bool returnsRows)
            => recorder.Queries.Add(new Query(CommandText,
                Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, parameter.Value!)).ToArray(), returnsRows));
        protected override void Dispose(bool disposing)
        {
            if (disposing) innerCommand.Dispose();
            base.Dispose(disposing);
        }
    }
}
