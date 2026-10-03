using Dm;
using W.EntityFrameworkCore.Dameng.Storage.Internal;

#pragma warning disable EF1001 // Test infrastructure intentionally uses the provider lexer.

namespace W.EntityFrameworkCore.Dameng.FunctionalTests;

internal static class DamengScriptExecutor
{
    public static async Task ExecuteAsync(
        DmConnection connection,
        string script,
        bool idempotent,
        Func<string, string> redact)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(redact);

        var batches = idempotent
            ? SplitIdempotent(script)
            : SplitStatements(script);

        foreach (var batch in batches)
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    redact(
                        "Dameng script batch failed:"
                        + Environment.NewLine
                        + batch
                        + Environment.NewLine
                        + exception));
            }
        }
    }

    internal static IReadOnlyList<string> SplitStatements(string script)
    {
        var batches = new List<string>();
        DamengSqlBatchParser.AddScript(batches, script);
        return batches;
    }

    internal static IReadOnlyList<string> SplitIdempotent(string script)
    {
        var batches = new List<string>();
        foreach (var segment in SplitOnDisqlTerminator(script))
        {
            DamengSqlBatchParser.AddScript(batches, segment);
        }

        return batches;
    }

    private static IEnumerable<string> SplitOnDisqlTerminator(string script)
    {
        var start = 0;
        foreach (var token in DamengSqlLexer.Read(script))
        {
            if (token.Text != "/")
            {
                continue;
            }

            var lineStart = token.Start;
            while (lineStart > 0 && script[lineStart - 1] is not ('\r' or '\n'))
            {
                lineStart--;
            }

            var lineEnd = token.End;
            while (lineEnd < script.Length && script[lineEnd] is not ('\r' or '\n'))
            {
                lineEnd++;
            }

            if (script[lineStart..lineEnd].Trim() == "/")
            {
                yield return script[start..lineStart];
                start = lineEnd;
            }
        }

        yield return script[start..];
    }
}
