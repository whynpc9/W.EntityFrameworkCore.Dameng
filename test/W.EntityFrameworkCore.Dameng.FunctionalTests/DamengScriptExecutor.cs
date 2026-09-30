using System.Text;
using Dm;

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

    // Anonymous DMSQL blocks (BEGIN ... END;) are single commands even though they contain
    // semicolons. Blocks may nest (the idempotent history guard wraps the EnsureSchema
    // guard), so depth is tracked and only the outermost END; completes the batch.
    // Provider-generated blocks place BEGIN / END; on their own lines.
    private static void AddScript(List<string> batches, string script)
    {
        var prologue = new StringBuilder();
        var block = new StringBuilder();
        var blockDepth = 0;

        foreach (var rawLine in script.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (blockDepth == 0 && !string.Equals(line, "BEGIN", StringComparison.Ordinal))
            {
                prologue.AppendLine(rawLine);
                continue;
            }

            if (blockDepth == 0)
            {
                AddStatements(batches, prologue.ToString());
                prologue.Clear();
            }

            block.AppendLine(rawLine);
            if (string.Equals(line, "BEGIN", StringComparison.Ordinal))
            {
                blockDepth++;
            }
            else if (string.Equals(line, "END;", StringComparison.Ordinal))
            {
                blockDepth--;
                if (blockDepth == 0)
                {
                    var text = block.ToString().Trim();
                    if (text.Length > 0)
                    {
                        batches.Add(text);
                    }

                    block.Clear();
                }
            }
        }

        if (blockDepth != 0)
        {
            throw new InvalidOperationException(
                "A Dameng script contains a BEGIN block without END;.");
        }

        AddStatements(batches, prologue.ToString());
    }

    private static List<string> SplitOnDisqlTerminator(string script)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var line = new StringBuilder();

        for (var index = 0; index < script.Length; index++)
        {
            var currentChar = script[index];
            var next = index + 1 < script.Length ? script[index + 1] : '\0';

            if (!inDouble && currentChar == '\'')
            {
                if (inSingle && next == '\'')
                {
                    line.Append(currentChar);
                    line.Append(next);
                    index++;
                    continue;
                }

                inSingle = !inSingle;
                line.Append(currentChar);
                continue;
            }

            if (!inSingle && currentChar == '"')
            {
                if (inDouble && next == '"')
                {
                    line.Append(currentChar);
                    line.Append(next);
                    index++;
                    continue;
                }

                inDouble = !inDouble;
                line.Append(currentChar);
                continue;
            }

            if (currentChar is '\n' or '\r')
            {
                var terminator = !inSingle
                    && !inDouble
                    && string.Equals(line.ToString().Trim(), "/", StringComparison.Ordinal);
                if (currentChar == '\r' && next == '\n')
                {
                    index++;
                }

                if (terminator)
                {
                    var segment = current.ToString().Trim();
                    if (segment.Length > 0)
                    {
                        segments.Add(segment);
                    }

                    current.Clear();
                }
                else
                {
                    current.Append(line);
                    current.Append('\n');
                }

                line.Clear();
                continue;
            }

            line.Append(currentChar);
        }

        if (line.Length > 0)
        {
            if (!inSingle
                && !inDouble
                && string.Equals(line.ToString().Trim(), "/", StringComparison.Ordinal))
            {
                var segment = current.ToString().Trim();
                if (segment.Length > 0)
                {
                    segments.Add(segment);
                }
            }
            else
            {
                current.Append(line);
                var tail = current.ToString().Trim();
                if (tail.Length > 0)
                {
                    segments.Add(tail);
                }
            }
        }
        else
        {
            var tail = current.ToString().Trim();
            if (tail.Length > 0)
            {
                segments.Add(tail);
            }
        }

        return segments;
    }

    private static void AddStatements(List<string> batches, string script)
    {
        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < script.Length; index++)
        {
            var currentChar = script[index];
            var next = index + 1 < script.Length ? script[index + 1] : '\0';

            if (inLineComment)
            {
                current.Append(currentChar);
                if (currentChar == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                current.Append(currentChar);
                if (currentChar == '*' && next == '/')
                {
                    current.Append(next);
                    index++;
                    inBlockComment = false;
                }

                continue;
            }

            if (inSingle)
            {
                current.Append(currentChar);
                if (currentChar == '\'' && next == '\'')
                {
                    current.Append(next);
                    index++;
                }
                else if (currentChar == '\'')
                {
                    inSingle = false;
                }

                continue;
            }

            if (inDouble)
            {
                current.Append(currentChar);
                if (currentChar == '"' && next == '"')
                {
                    current.Append(next);
                    index++;
                }
                else if (currentChar == '"')
                {
                    inDouble = false;
                }

                continue;
            }

            if (currentChar == '-' && next == '-')
            {
                current.Append(currentChar);
                current.Append(next);
                index++;
                inLineComment = true;
                continue;
            }

            if (currentChar == '/' && next == '*')
            {
                current.Append(currentChar);
                current.Append(next);
                index++;
                inBlockComment = true;
                continue;
            }

            if (currentChar == '\'')
            {
                current.Append(currentChar);
                inSingle = true;
                continue;
            }

            if (currentChar == '"')
            {
                current.Append(currentChar);
                inDouble = true;
                continue;
            }

            if (currentChar == ';')
            {
                AddIfPresent(batches, current.ToString());
                current.Clear();
                continue;
            }

            current.Append(currentChar);
        }

        AddIfPresent(batches, current.ToString());
    }

    private static void AddIfPresent(List<string> batches, string statement)
    {
        var trimmed = statement.Trim();
        if (trimmed.Length > 0)
        {
            batches.Add(trimmed);
        }
    }
}
