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
        // Constraints are sys.objects rows too, with parent_object_id pointing to the owning table, so
        // they share the class-1 extended-property/permission path used for tables and views.
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
