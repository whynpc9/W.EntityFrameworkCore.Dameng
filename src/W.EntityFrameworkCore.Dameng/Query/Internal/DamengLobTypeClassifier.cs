using Microsoft.EntityFrameworkCore.Storage;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal static class DamengLobTypeClassifier
{
    public static LobKind? GetLobKind(RelationalTypeMapping? typeMapping)
        => typeMapping?.StoreTypeNameBase.ToUpperInvariant() switch
        {
            "CLOB" or "NCLOB" or "TEXT" or "NTEXT" or "LONG" or "LONGVARCHAR"
                => LobKind.Text,
            "BLOB" or "IMAGE" or "LONGVARBINARY" or "BFILE"
                => LobKind.Binary,
            _ => null
        };

    public enum LobKind
    {
        Text,
        Binary
    }
}
