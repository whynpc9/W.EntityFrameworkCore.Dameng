namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

// Small lexical reader for command boundaries, not a DMSQL grammar. Quoted text is one
// opaque token; comments and whitespace are skipped while source offsets are preserved.
internal static class DamengSqlLexer
{
    internal readonly record struct Token(string Text, int Start, int End);

    internal static IEnumerable<Token> Read(string sql)
    {
        var index = 0;
        while (index < sql.Length)
        {
            var current = sql[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                while (index < sql.Length && sql[index] is not ('\r' or '\n'))
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                var end = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw new InvalidOperationException("Unterminated Dameng SQL comment.");
                }

                index = end + 2;
                continue;
            }

            var start = index++;
            if (current is '\'' or '"')
            {
                var closed = false;
                while (index < sql.Length)
                {
                    if (sql[index++] != current)
                    {
                        continue;
                    }

                    if (index < sql.Length && sql[index] == current)
                    {
                        index++;
                        continue;
                    }

                    closed = true;
                    break;
                }

                if (!closed)
                {
                    throw new InvalidOperationException("Unterminated Dameng SQL quoted text.");
                }
            }
            else if (IsIdentifierCharacter(current))
            {
                while (index < sql.Length && IsIdentifierCharacter(sql[index]))
                {
                    index++;
                }
            }

            yield return new Token(sql[start..index], start, index);
        }
    }

    private static bool IsIdentifierCharacter(char value)
        => char.IsLetterOrDigit(value) || value is '_' or '$' or '#';
}
