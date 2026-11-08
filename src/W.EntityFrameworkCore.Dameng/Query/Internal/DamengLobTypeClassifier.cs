using Microsoft.EntityFrameworkCore.Storage;
using W.EntityFrameworkCore.Dameng.Storage.Internal;

namespace W.EntityFrameworkCore.Dameng.Query.Internal;

internal static class DamengLobTypeClassifier
{
    public static LobKind? GetLobKind(RelationalTypeMapping? typeMapping)
        => DamengLobStoreType.Classify(typeMapping?.StoreTypeNameBase) switch
        {
            DamengLobStoreType.Kind.Text => LobKind.Text,
            DamengLobStoreType.Kind.Binary => LobKind.Binary,
            _ => null
        };

    public enum LobKind
    {
        Text,
        Binary
    }
}
