using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Common;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

public static class AlterColumnScriptGenerator
{
    public static string Generate(ObjectRef table, ColumnSnapshot column, SqlCollationName targetCollation)
    {
        var typeClause = BuildTypeClause(column);
        var nullability = column.IsNullable ? "NULL" : "NOT NULL";

        return $"ALTER TABLE {table.Identifier.Quoted} ALTER COLUMN {SqlIdentifier.QuotePart(column.Name)} " +
               $"{typeClause} COLLATE {targetCollation.Value} {nullability};";
    }

    private static string BuildTypeClause(ColumnSnapshot column)
    {
        var typeName = column.SqlDataType.ToLowerInvariant();

        return typeName switch
        {
            "nvarchar" or "nchar" => $"{typeName}({FormatLength(column.MaxLength, isUnicode: true)})",
            "varchar" or "char" => $"{typeName}({FormatLength(column.MaxLength, isUnicode: false)})",
            _ => typeName
        };
    }

    private static string FormatLength(int? maxLength, bool isUnicode)
    {
        if (maxLength is null or -1)
            return "MAX";

        return isUnicode ? (maxLength.Value / 2).ToString() : maxLength.Value.ToString();
    }
}
