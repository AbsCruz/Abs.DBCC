using Abs.DBCC.Domain.Collation;

namespace Abs.DBCC.Domain.Inspection;

public sealed record ColumnCollationState(
    string SchemaName,
    string TableName,
    string ColumnName,
    string SqlDataType,
    bool IsCharacterType,
    SqlCollationName? Collation);
