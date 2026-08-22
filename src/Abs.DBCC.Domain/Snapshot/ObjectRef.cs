using Abs.DBCC.Domain.Common;

namespace Abs.DBCC.Domain.Snapshot;

public sealed record ObjectRef(string SchemaName, string Name, DatabaseObjectKind Kind)
{
    public SqlIdentifier Identifier => new(SchemaName, Name);

    public override string ToString() => Identifier.Quoted;
}
