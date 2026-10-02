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
                if (word == "CREATE")
                {
                    var kind = index + 1;
                    if (kind + 1 < tokens.Count
                        && tokens[kind].Text.Equals("OR", StringComparison.OrdinalIgnoreCase)
                        && tokens[kind + 1].Text.Equals("REPLACE", StringComparison.OrdinalIgnoreCase))
                    {
                        kind += 2;
                    }

                    if (kind < tokens.Count && tokens[kind].Text.ToUpperInvariant()
                        is "PROCEDURE" or "FUNCTION" or "TRIGGER" or "PACKAGE")
                    {
                        throw new NotSupportedException(
                            "The test script executor does not split CREATE routine/package/trigger bodies. "
                            + "Execute the complete definition as one command instead.");
                    }
                }
            }

            if (anonymous)
            {
                if (word == "DECLARE")
                {
                    stack.Push("DECLARE");
                }
                else if (word is "PROCEDURE" or "FUNCTION"
                    && stack.TryPeek(out var scope) && scope == "DECLARE")
                {
                    // A local routine owns a declaration region and body of its own. Its
                    // END must leave the containing DECLARE region on the stack.
                    stack.Push("ROUTINE_HEADER");
                }
                else if (word is "IS" or "AS"
                    && stack.TryPeek(out var header) && header == "ROUTINE_HEADER")
                {
                    stack.Pop();
                    stack.Push("DECLARE");
                }
                else if (word == ";" && stack.TryPeek(out var forward) && forward == "ROUTINE_HEADER")
                {
                    // Forward declaration with no body.
                    stack.Pop();
                }
                else if (word is "BEGIN" or "CASE")
                {
                    if (word == "BEGIN" && stack.TryPeek(out var declaration) && declaration == "DECLARE")
                    {
                        stack.Pop();
                    }

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
