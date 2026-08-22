using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class IndexScriptGenerator
{
    public static string GenerateDrop(ObjectRef table, IndexSnapshot index) =>
        index.IsTableConstraint
            ? $"ALTER TABLE {table.Identifier.Quoted} DROP CONSTRAINT {SqlIdentifier.QuotePart(index.Name)};"
            : $"DROP INDEX {SqlIdentifier.QuotePart(index.Name)} ON {table.Identifier.Quoted};";

    public static string GenerateCreate(ObjectRef table, IndexSnapshot index)
    {
        var keyColumns = string.Join(", ",
            index.Columns.Where(c => !c.IsIncluded).Select(c => $"{SqlIdentifier.QuotePart(c.ColumnName)} {(c.IsDescending ? "DESC" : "ASC")}"));

        if (index.IsPrimaryKey || index.IsUniqueConstraint)
        {
            var constraintType = index.IsPrimaryKey ? "PRIMARY KEY" : "UNIQUE";
            var clustering = index.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
            return $"ALTER TABLE {table.Identifier.Quoted} ADD CONSTRAINT {SqlIdentifier.QuotePart(index.Name)} " +
                   $"{constraintType} {clustering} ({keyColumns});";
        }

        var uniqueKeyword = index.IsUnique ? "UNIQUE " : "";
        var clusteringKeyword = index.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
        var includeClause = BuildIncludeClause(index);
        var whereClause = string.IsNullOrEmpty(index.FilterDefinition) ? "" : $" WHERE {index.FilterDefinition}";

        return $"CREATE {uniqueKeyword}{clusteringKeyword} INDEX {SqlIdentifier.QuotePart(index.Name)} " +
               $"ON {table.Identifier.Quoted} ({keyColumns}){includeClause}{whereClause};";
    }

    private static string BuildIncludeClause(IndexSnapshot index)
    {
        var includedColumns = index.Columns.Where(c => c.IsIncluded).Select(c => SqlIdentifier.QuotePart(c.ColumnName)).ToList();
        return includedColumns.Count == 0 ? "" : $" INCLUDE ({string.Join(", ", includedColumns)})";
    }
}
