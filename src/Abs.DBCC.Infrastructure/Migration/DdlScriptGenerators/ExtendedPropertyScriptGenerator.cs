using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class ExtendedPropertyScriptGenerator
{
    public static string GenerateAdd(ExtendedPropertySnapshot property) =>
        property.ParentTable is not null
            ? GenerateForConstraintOrIndex(property, property.ParentTable)
            : GenerateForTableLikeObject(property);

    private static string GenerateForTableLikeObject(ExtendedPropertySnapshot property)
    {
        var level1Type = Level1TypeFor(property.Object.Kind);
        var level2Clause = property.ColumnName is null
            ? string.Empty
            : $", @level2type = N'COLUMN', @level2name = N'{Escape(property.ColumnName)}'";

        return "EXEC sys.sp_addextendedproperty " +
               $"@name = N'{Escape(property.PropertyName)}', @value = N'{Escape(property.PropertyValue)}', " +
               $"@level0type = N'SCHEMA', @level0name = N'{Escape(property.Object.SchemaName)}', " +
               $"@level1type = N'{level1Type}', @level1name = N'{Escape(property.Object.Name)}'{level2Clause};";
    }

    /// <summary>A constraint (PK/UQ/CHECK/DEFAULT/FK) or an index (on a table or an indexed view): the
    /// property's own <see cref="ExtendedPropertySnapshot.Object"/> is the constraint/index itself, one
    /// level below the table/view named in <paramref name="parentTable"/>.</summary>
    private static string GenerateForConstraintOrIndex(ExtendedPropertySnapshot property, ObjectRef parentTable)
    {
        var level1Type = Level1TypeFor(parentTable.Kind);
        var level2Type = property.Object.Kind switch
        {
            DatabaseObjectKind.Index => "INDEX",
            DatabaseObjectKind.PrimaryKey or DatabaseObjectKind.UniqueConstraint or DatabaseObjectKind.CheckConstraint
                or DatabaseObjectKind.DefaultConstraint or DatabaseObjectKind.ForeignKey => "CONSTRAINT",
            _ => throw new ArgumentOutOfRangeException(nameof(property), property.Object.Kind, "Not a supported constraint/index extended-property kind.")
        };

        return "EXEC sys.sp_addextendedproperty " +
               $"@name = N'{Escape(property.PropertyName)}', @value = N'{Escape(property.PropertyValue)}', " +
               $"@level0type = N'SCHEMA', @level0name = N'{Escape(parentTable.SchemaName)}', " +
               $"@level1type = N'{level1Type}', @level1name = N'{Escape(parentTable.Name)}', " +
               $"@level2type = N'{level2Type}', @level2name = N'{Escape(property.Object.Name)}';";
    }

    private static string Level1TypeFor(DatabaseObjectKind kind) => kind switch
    {
        DatabaseObjectKind.Table => "TABLE",
        DatabaseObjectKind.View => "VIEW",
        DatabaseObjectKind.StoredProcedure => "PROCEDURE",
        DatabaseObjectKind.Function => "FUNCTION",
        DatabaseObjectKind.Trigger => "TRIGGER",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a supported extended-property level-1 object kind.")
    };

    private static string Escape(string value) => value.Replace("'", "''");
}
