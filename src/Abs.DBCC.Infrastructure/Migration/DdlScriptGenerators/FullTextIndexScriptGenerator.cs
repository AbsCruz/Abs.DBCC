using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class FullTextIndexScriptGenerator
{
    public static string GenerateDrop(FullTextIndexSnapshot index) =>
        $"DROP FULLTEXT INDEX ON {index.Table.Identifier.Quoted};";

    public static string GenerateCreate(FullTextIndexSnapshot index)
    {
        var columns = string.Join(", ", index.Columns.Select(c => $"{SqlIdentifier.QuotePart(c.ColumnName)} LANGUAGE {c.LanguageId}"));

        return $"CREATE FULLTEXT INDEX ON {index.Table.Identifier.Quoted} ({columns}) " +
               $"KEY INDEX {SqlIdentifier.QuotePart(index.KeyIndexName)} ON {SqlIdentifier.QuotePart(index.CatalogName)} " +
               $"WITH CHANGE_TRACKING {index.ChangeTracking};";
    }
}
