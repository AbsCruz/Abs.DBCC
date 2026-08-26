namespace Abs.DBCC.Domain.Migration;

/// <summary>
/// Identifies a column by schema/table/column name, comparing case-insensitively. Used to match a column
/// captured by one query (e.g. the collation overview) against a column captured by an independent one
/// (e.g. a schema snapshot), where reference/record identity can't be relied on.
/// </summary>
public sealed class ColumnRef(string schemaName, string tableName, string columnName) : IEquatable<ColumnRef>
{
    public string SchemaName { get; } = schemaName;
    public string TableName { get; } = tableName;
    public string ColumnName { get; } = columnName;

    public bool Equals(ColumnRef? other) =>
        other is not null &&
        string.Equals(SchemaName, other.SchemaName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(TableName, other.TableName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ColumnName, other.ColumnName, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ColumnRef);

    public override int GetHashCode() =>
        HashCode.Combine(
            SchemaName.ToUpperInvariant(),
            TableName.ToUpperInvariant(),
            ColumnName.ToUpperInvariant());
}
