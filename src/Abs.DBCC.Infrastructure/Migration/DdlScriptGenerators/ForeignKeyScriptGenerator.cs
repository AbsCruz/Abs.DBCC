using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class ForeignKeyScriptGenerator
{
    public static string GenerateDrop(ForeignKeySnapshot foreignKey) =>
        $"ALTER TABLE {foreignKey.ParentTable.Identifier.Quoted} DROP CONSTRAINT {SqlIdentifier.QuotePart(foreignKey.Name)};";

    public static string GenerateCreate(ForeignKeySnapshot foreignKey)
    {
        var parentColumns = string.Join(", ", foreignKey.Columns.Select(c => SqlIdentifier.QuotePart(c.ParentColumn)));
        var referencedColumns = string.Join(", ", foreignKey.Columns.Select(c => SqlIdentifier.QuotePart(c.ReferencedColumn)));
        var notForReplication = foreignKey.IsNotForReplication ? " NOT FOR REPLICATION" : "";

        return $"ALTER TABLE {foreignKey.ParentTable.Identifier.Quoted} ADD CONSTRAINT {SqlIdentifier.QuotePart(foreignKey.Name)} " +
               $"FOREIGN KEY ({parentColumns}) REFERENCES {foreignKey.ReferencedTable.Identifier.Quoted} ({referencedColumns}) " +
               $"ON DELETE {FormatAction(foreignKey.DeleteAction)} ON UPDATE {FormatAction(foreignKey.UpdateAction)}{notForReplication};";
    }

    private static string FormatAction(string action) => action.Replace('_', ' ');
}
