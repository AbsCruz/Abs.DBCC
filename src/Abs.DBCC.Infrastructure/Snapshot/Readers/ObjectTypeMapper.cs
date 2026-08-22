using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>Maps a sys.objects.type code to the corresponding <see cref="DatabaseObjectKind"/>, where one exists.</summary>
internal static class ObjectTypeMapper
{
    public static DatabaseObjectKind? TryMap(string typeCode) => typeCode.Trim() switch
    {
        "U" => DatabaseObjectKind.Table,
        "V" => DatabaseObjectKind.View,
        "P" => DatabaseObjectKind.StoredProcedure,
        "FN" or "IF" or "TF" => DatabaseObjectKind.Function,
        "TR" => DatabaseObjectKind.Trigger,
        "SN" => DatabaseObjectKind.Synonym,
        "SO" => DatabaseObjectKind.Sequence,
        // Constraints are rows in sys.objects too (confirmed against a real instance: their
        // parent_object_id resolves straight to the owning table), so the same class-1
        // "Object or Column" extended-property/permission path used for tables/views/etc. covers them.
        "PK" => DatabaseObjectKind.PrimaryKey,
        "UQ" => DatabaseObjectKind.UniqueConstraint,
        "C" => DatabaseObjectKind.CheckConstraint,
        "D" => DatabaseObjectKind.DefaultConstraint,
        "F" => DatabaseObjectKind.ForeignKey,
        _ => null
    };

    public static bool IsConstraintKind(DatabaseObjectKind kind) => kind is
        DatabaseObjectKind.PrimaryKey or DatabaseObjectKind.UniqueConstraint or DatabaseObjectKind.CheckConstraint
        or DatabaseObjectKind.DefaultConstraint or DatabaseObjectKind.ForeignKey;
}
