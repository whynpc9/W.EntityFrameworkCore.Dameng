namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal static class DamengLobStoreType
{
    public static Kind? Classify(string? storeType)
        => storeType?.Trim().ToUpperInvariant() switch
        {
            "CLOB" or "NCLOB" or "TEXT" or "NTEXT" or "LONG" or "LONGVARCHAR" => Kind.Text,
            "BLOB" or "IMAGE" or "LONGVARBINARY" or "BFILE" => Kind.Binary,
            _ => null
        };

    public enum Kind
    {
        Text,
        Binary
    }
}
