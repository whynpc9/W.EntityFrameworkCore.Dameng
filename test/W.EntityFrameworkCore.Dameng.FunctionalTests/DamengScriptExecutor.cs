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
        AddScript(batches, script);
        return batches;
    }

    internal static IReadOnlyList<string> SplitIdempotent(string script)
    {
        var batches = new List<string>();
        foreach (var segment in SplitOnDisqlTerminator(script))
        {
            AddScript(batches, segment);
        }

        return batches;
    }

    // Split on tokens outside quotes/comments. Keep the source text (including leading
    // comments) intact, and keep DECLARE declarations until their outer BEGIN/END closes.
    private static void AddScript(List<string> batches, string script)
    {
        var tokens = DamengSqlLexer.Read(script).ToList();
        var stack = new Stack<string>();
        var start = 0;
        var hasStatement = false;
        var anonymous = false;
        var bodySeen = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var word = token.Text.ToUpperInvariant();
            if (!hasStatement && word != ";")
            {
                hasStatement = true;
                anonymous = word is "BEGIN" or "DECLARE";
            }

            if (anonymous)
            {
                if (word is "BEGIN" or "CASE")
                {
                    stack.Push(word);
                    bodySeen |= word == "BEGIN";
                }
                else if (word == "END")
                {
                    var next = index + 1 < tokens.Count ? tokens[index + 1].Text.ToUpperInvariant() : "";
                    if (next is "IF" or "LOOP")
                    {
                        index++;
                    }
                    else
                    {
                        if (stack.Count == 0 || (next == "CASE" && stack.Peek() != "CASE"))
                        {
                            throw new InvalidOperationException("Unbalanced Dameng SQL END token.");
                        }

                        stack.Pop();
                        if (next == "CASE")
                        {
                            index++;
                        }
                    }
                }
            }

            if (word == ";" && (!anonymous || (bodySeen && stack.Count == 0)))
            {
                if (hasStatement)
                {
                    batches.Add(script[start..(anonymous ? token.End : token.Start)].Trim());
                }

                start = token.End;
                hasStatement = false;
                anonymous = false;
                bodySeen = false;
            }
        }

        if (anonymous)
        {
            throw new InvalidOperationException("A Dameng script contains an incomplete anonymous block.");
        }

        if (hasStatement)
        {
            batches.Add(script[start..].Trim());
        }
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
