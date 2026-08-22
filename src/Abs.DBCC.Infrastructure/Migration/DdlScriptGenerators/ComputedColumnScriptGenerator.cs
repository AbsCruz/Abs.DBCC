using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class ComputedColumnScriptGenerator
{
    public static string GenerateDrop(ObjectRef table, ColumnSnapshot column) =>
        $"ALTER TABLE {table.Identifier.Quoted} DROP COLUMN {SqlIdentifier.QuotePart(column.Name)};";

    public static string GenerateCreate(ObjectRef table, ColumnSnapshot column)
    {
        var persisted = column.IsComputedPersisted ? " PERSISTED" : "";
        return $"ALTER TABLE {table.Identifier.Quoted} ADD {SqlIdentifier.QuotePart(column.Name)} " +
               $"AS {column.ComputedDefinition}{persisted};";
    }
}
